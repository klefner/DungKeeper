using System;
using System.Collections.Generic;

namespace DungKeeper
{
    // =========================================================================
    // Event payload types
    // =========================================================================

    /// <summary>Published when the overlord slaps a unit.</summary>
    public sealed class UnitSlappedEvent
    {
        public UnitData     Unit     { get; }
        public float        Force    { get; }
        public SlapResponse Response { get; }

        public UnitSlappedEvent(UnitData unit, float force, SlapResponse response)
        {
            Unit     = unit     ?? throw new ArgumentNullException(nameof(unit));
            Force    = force;
            Response = response;
        }
    }

    /// <summary>Published whenever a unit's <see cref="UnitState"/> changes.</summary>
    public sealed class UnitStateChangedEvent
    {
        public UnitData   Unit          { get; }
        public UnitState  PreviousState { get; }
        public UnitState  NewState      { get; }

        public UnitStateChangedEvent(UnitData unit, UnitState previousState, UnitState newState)
        {
            Unit          = unit ?? throw new ArgumentNullException(nameof(unit));
            PreviousState = previousState;
            NewState      = newState;
        }
    }

    /// <summary>Published when a unit's health reaches zero.</summary>
    public sealed class UnitDiedEvent
    {
        public UnitData Unit { get; }

        public UnitDiedEvent(UnitData unit)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
        }
    }

    /// <summary>Published when a unit transitions into a rebellion state.</summary>
    public sealed class UnitRebellionStartedEvent
    {
        public UnitData   Unit       { get; }
        public StrikeType StrikeType { get; }

        public UnitRebellionStartedEvent(UnitData unit, StrikeType strikeType)
        {
            Unit       = unit ?? throw new ArgumentNullException(nameof(unit));
            StrikeType = strikeType;
        }
    }

    /// <summary>Published when a new room is successfully constructed.</summary>
    public sealed class RoomBuiltEvent
    {
        public RoomData Room { get; }

        public RoomBuiltEvent(RoomData room)
        {
            Room = room ?? throw new ArgumentNullException(nameof(room));
        }
    }

    /// <summary>Published whenever a tracked resource amount changes.</summary>
    public sealed class ResourceChangedEvent
    {
        public ResourceType Type     { get; }
        public float        Delta    { get; }
        public float        NewTotal { get; }

        public ResourceChangedEvent(ResourceType type, float delta, float newTotal)
        {
            Type     = type;
            Delta    = delta;
            NewTotal = newTotal;
        }
    }

    /// <summary>Published when an enemy incursion enters the dungeon.</summary>
    public sealed class ThreatSpawnedEvent
    {
        public ThreatLevel Level        { get; }
        public int         InvaderCount { get; }

        public ThreatSpawnedEvent(ThreatLevel level, int invaderCount)
        {
            Level        = level;
            InvaderCount = invaderCount;
        }
    }

    /// <summary>Published when an enemy incursion is resolved.</summary>
    public sealed class ThreatResolvedEvent
    {
        public bool PlayerWon { get; }

        public ThreatResolvedEvent(bool playerWon)
        {
            PlayerWon = playerWon;
        }
    }

    /// <summary>Published when game state is persisted to a save slot.</summary>
    public sealed class GameSavedEvent
    {
        public string SaveSlot { get; }

        public GameSavedEvent(string saveSlot)
        {
            SaveSlot = saveSlot ?? throw new ArgumentNullException(nameof(saveSlot));
        }
    }

    // =========================================================================
    // Event bus
    // =========================================================================

    /// <summary>
    /// Type-safe publish/subscribe event bus.
    ///
    /// Usage:
    ///   EventBus.Global.Subscribe&lt;UnitDiedEvent&gt;(OnUnitDied);
    ///   EventBus.Global.Publish(new UnitDiedEvent(unit));
    ///   EventBus.Global.Unsubscribe&lt;UnitDiedEvent&gt;(OnUnitDied);
    ///
    /// Systems should unsubscribe when they are disposed or destroyed to
    /// avoid memory leaks and phantom callbacks.
    /// </summary>
    public sealed class EventBus
    {
        // ------------------------------------------------------------------
        // Singleton convenience accessor
        // ------------------------------------------------------------------

        /// <summary>Process-wide default bus. Use this unless isolated testing requires a fresh instance.</summary>
        public static readonly EventBus Global = new EventBus();

        // ------------------------------------------------------------------
        // Internal state
        // ------------------------------------------------------------------

        // Maps event type -> list of raw Delegate so we keep a single
        // dictionary without per-type generic fields.
        private readonly Dictionary<Type, List<Delegate>> _handlers
            = new Dictionary<Type, List<Delegate>>();

        // Guard against modifying the handler list while iterating (e.g.
        // a handler that unsubscribes itself during Publish).
        private bool _isPublishing;

        // Deferred operations collected while publishing.
        private readonly List<(bool subscribe, Type eventType, Delegate handler)> _pending
            = new List<(bool, Type, Delegate)>();

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>
        /// Registers <paramref name="handler"/> to receive events of type <typeparamref name="T"/>.
        /// Subscribing the same delegate instance more than once is a no-op for that extra call
        /// (duplicate guard applied).
        /// </summary>
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (_isPublishing)
            {
                _pending.Add((true, typeof(T), handler));
                return;
            }

            AddHandler(typeof(T), handler);
        }

        /// <summary>
        /// Removes a previously registered <paramref name="handler"/> for events of type <typeparamref name="T"/>.
        /// Safe to call even if the handler was never registered.
        /// </summary>
        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (_isPublishing)
            {
                _pending.Add((false, typeof(T), handler));
                return;
            }

            RemoveHandler(typeof(T), handler);
        }

        /// <summary>
        /// Publishes <paramref name="evt"/> to all subscribers registered for type <typeparamref name="T"/>.
        /// Handlers are invoked synchronously in subscription order.
        /// Exceptions thrown by individual handlers are caught, logged to
        /// <see cref="Console.Error"/>, and do not prevent subsequent handlers from running.
        /// </summary>
        public void Publish<T>(T evt)
        {
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            Type key = typeof(T);
            if (!_handlers.TryGetValue(key, out List<Delegate> handlers) || handlers.Count == 0)
                return;

            // Snapshot so removals during iteration are safe.
            // We still set the flag to defer add/remove from within handlers.
            _isPublishing = true;
            try
            {
                // Iterate over a copy to allow safe mutation of the original list
                // once _isPublishing is cleared.
                Delegate[] snapshot = handlers.ToArray();
                foreach (Delegate d in snapshot)
                {
                    try
                    {
                        ((Action<T>)d).Invoke(evt);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[EventBus] Handler {d.Method.DeclaringType?.Name}.{d.Method.Name} " +
                            $"threw an exception for event {typeof(T).Name}: {ex}");
                    }
                }
            }
            finally
            {
                _isPublishing = false;
                FlushPending();
            }
        }

        /// <summary>
        /// Removes all subscriptions for every event type.
        /// Useful when resetting between scenes or test runs.
        /// </summary>
        public void Clear()
        {
            if (_isPublishing)
                throw new InvalidOperationException(
                    "Cannot call Clear() from within a Publish() call.");

            _handlers.Clear();
            _pending.Clear();
        }

        /// <summary>
        /// Returns the number of subscribers currently registered for event type <typeparamref name="T"/>.
        /// Useful for debugging and tests.
        /// </summary>
        public int SubscriberCount<T>()
        {
            return _handlers.TryGetValue(typeof(T), out List<Delegate> list) ? list.Count : 0;
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private void AddHandler(Type eventType, Delegate handler)
        {
            if (!_handlers.TryGetValue(eventType, out List<Delegate> list))
            {
                list = new List<Delegate>(4);
                _handlers[eventType] = list;
            }

            // Duplicate guard
            if (!list.Contains(handler))
                list.Add(handler);
        }

        private void RemoveHandler(Type eventType, Delegate handler)
        {
            if (_handlers.TryGetValue(eventType, out List<Delegate> list))
                list.Remove(handler);
        }

        private void FlushPending()
        {
            if (_pending.Count == 0) return;

            // Copy + clear before processing in case flushing triggers more deferred ops.
            var toProcess = new List<(bool subscribe, Type eventType, Delegate handler)>(_pending);
            _pending.Clear();

            foreach (var (subscribe, eventType, handler) in toProcess)
            {
                if (subscribe)
                    AddHandler(eventType, handler);
                else
                    RemoveHandler(eventType, handler);
            }
        }
    }
}

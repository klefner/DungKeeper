using System;
using System.Collections.Generic;

namespace DungKeeper
{
    // =========================================================================
    // Base state contract
    // =========================================================================

    /// <summary>
    /// Contract every concrete unit state must satisfy.
    /// States are inner classes of <see cref="UnitStateMachine"/> and share
    /// direct access to their owner machine and its <see cref="UnitData"/>.
    /// </summary>
    internal interface IUnitState
    {
        /// <summary>Called once when the state machine enters this state.</summary>
        void Enter();

        /// <summary>Called every simulation tick while this state is active.</summary>
        /// <param name="deltaTime">Seconds elapsed since last tick.</param>
        void Tick(float deltaTime);

        /// <summary>Called once immediately before leaving this state.</summary>
        void Exit();
    }

    // =========================================================================
    // State machine
    // =========================================================================

    /// <summary>
    /// Drives all behavioural logic for a single dungeon unit.
    ///
    /// Design principles:
    ///   - Pure C# — no Unity dependency.
    ///   - Each concrete state lives as an inner class; the machine owns them.
    ///   - Transitions are requested via <see cref="TransitionTo"/> and always
    ///     fire <see cref="UnitStateChangedEvent"/> through the supplied bus.
    ///   - The machine holds a reference to the shared <see cref="GameSettings"/>
    ///     so states can read balance values without static coupling.
    /// </summary>
    public sealed class UnitStateMachine
    {
        // ------------------------------------------------------------------
        // Public surface
        // ------------------------------------------------------------------

        /// <summary>The unit this machine is driving.</summary>
        public UnitData Unit { get; }

        /// <summary>Current active state identifier.</summary>
        public UnitState CurrentState { get; private set; }

        // ------------------------------------------------------------------
        // Internal wiring
        // ------------------------------------------------------------------

        private readonly EventBus    _bus;
        private readonly GameSettings _settings;
        private          IUnitState   _activeState;

        /// <summary>All concrete state instances, keyed by enum value.</summary>
        private readonly Dictionary<UnitState, IUnitState> _states;

        // ------------------------------------------------------------------
        // Construction
        // ------------------------------------------------------------------

        /// <param name="unit">The unit data record to drive.</param>
        /// <param name="bus">Event bus used for state-change and death events.</param>
        /// <param name="settings">Balance settings read by state logic.</param>
        public UnitStateMachine(UnitData unit, EventBus bus, GameSettings settings)
        {
            Unit      = unit     ?? throw new ArgumentNullException(nameof(unit));
            _bus      = bus      ?? throw new ArgumentNullException(nameof(bus));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            // Build all state instances once; they share the machine reference.
            _states = new Dictionary<UnitState, IUnitState>
            {
                { UnitState.Idle,        new IdleState(this)       },
                { UnitState.Working,     new WorkingState(this)    },
                { UnitState.Resting,     new RestingState(this)    },
                { UnitState.Eating,      new EatingState(this)     },
                { UnitState.Fighting,    new FightingState(this)   },
                { UnitState.Fleeing,     new FleeingState(this)    },
                { UnitState.Rebelling,   new RebellionState(this)  },
                { UnitState.Imprisoned,  new ImprisonedState(this) },
                { UnitState.Impressed,   new ImpressedState(this)  },
                { UnitState.Dead,        new DeadState(this)       },
            };

            // Start in whatever state the unit data already records.
            CurrentState = unit.CurrentState;
            _activeState = _states[CurrentState];
            _activeState.Enter();
        }

        // ------------------------------------------------------------------
        // Tick
        // ------------------------------------------------------------------

        /// <summary>
        /// Advances the state machine by one simulation step.
        /// Call once per simulation frame; <paramref name="deltaTime"/> is
        /// elapsed real-seconds (or scaled game-seconds, caller's choice).
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;

            // Passive stat recovery happens every tick regardless of state.
            Unit.RecoverOverTime(deltaTime, _settings);

            _activeState.Tick(deltaTime);
        }

        // ------------------------------------------------------------------
        // Transition
        // ------------------------------------------------------------------

        /// <summary>
        /// Requests a transition to <paramref name="next"/>.
        /// Ignored if already in that state or if the unit is dead
        /// (dead is a terminal state).
        /// </summary>
        public void TransitionTo(UnitState next)
        {
            if (next == CurrentState) return;
            if (CurrentState == UnitState.Dead) return; // terminal

            UnitState previous = CurrentState;

            _activeState.Exit();

            CurrentState      = next;
            Unit.CurrentState = next;
            _activeState      = _states[next];

            _activeState.Enter();

            _bus.Publish(new UnitStateChangedEvent(Unit, previous, next));
        }

        // ------------------------------------------------------------------
        // Convenience internal helpers available to all state inner classes
        // ------------------------------------------------------------------

        private bool NeedsFood()    => Unit.Hunger  > _settings.HungerDistressThreshold;
        private bool NeedsRest()    => Unit.Fatigue > _settings.FatigueDistressThreshold;
        private bool IsLowHealth()  => Unit.CurrentHealth < Unit.MaxHealth
                                        * (_settings.LowHealthFleeThreshold / 100f);
        private bool ShouldRebel()  => Unit.Anger   >= _settings.AngerRebellionThreshold;
        private bool HasTask()      => Unit.CurrentTask != TaskType.None
                                        && Unit.AssignedRoomId != null;

        // =====================================================================
        // Concrete states (inner classes)
        // =====================================================================

        // -----------------------------------------------------------------
        // IDLE
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit stands around and looks for something to do.
        /// Transitions: Working (task found), Resting (fatigued),
        ///              Eating (hungry), Rebelling (anger maxed).
        /// </summary>
        private sealed class IdleState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private float _idleTimer;

            public IdleState(UnitStateMachine m) => _m = m;

            public void Enter()  { _idleTimer = 0f; }
            public void Exit()   { }

            public void Tick(float dt)
            {
                if (_m.ShouldRebel())  { _m.TransitionTo(UnitState.Rebelling); return; }
                if (_m.NeedsFood())    { _m.TransitionTo(UnitState.Eating);    return; }
                if (_m.NeedsRest())    { _m.TransitionTo(UnitState.Resting);   return; }
                if (_m.HasTask())      { _m.TransitionTo(UnitState.Working);   return; }

                // Units get increasingly annoyed sitting idle with no task.
                _idleTimer += dt;
                if (_idleTimer > 5f) // every 5 seconds of doing nothing, tick anger slightly
                {
                    _idleTimer = 0f;
                    _m.Unit.Anger = Math.Min(100f, _m.Unit.Anger + 1f);
                }
            }
        }

        // -----------------------------------------------------------------
        // WORKING
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit actively performs its assigned task.
        /// Transitions: Idle (task complete/removed), Resting, Eating, Rebelling.
        /// </summary>
        private sealed class WorkingState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private float _workProgress; // 0-100, resets when task cycle completes

            public WorkingState(UnitStateMachine m) => _m = m;

            public void Enter()  { _workProgress = 0f; }
            public void Exit()   { }

            public void Tick(float dt)
            {
                // Priority interrupts
                if (_m.ShouldRebel()) { _m.TransitionTo(UnitState.Rebelling); return; }
                if (_m.NeedsFood())   { _m.TransitionTo(UnitState.Eating);    return; }
                if (_m.NeedsRest())   { _m.TransitionTo(UnitState.Resting);   return; }

                if (!_m.HasTask())
                {
                    _m.Unit.CurrentTask = TaskType.None;
                    _m.TransitionTo(UnitState.Idle);
                    return;
                }

                // Advance work cycle
                float rate = _m.Unit.GetProductivityMultiplier() * 10f; // 10 units/s at 1.0x
                _workProgress += rate * dt;

                if (_workProgress >= 100f)
                {
                    _workProgress = 0f;
                    // Task yield event could be raised here by a higher-level system.
                    // The state machine itself stays in Working until the task is removed.
                }
            }

            /// <summary>Fraction of the current work cycle completed (0-1).</summary>
            public float Progress => _workProgress / 100f;
        }

        // -----------------------------------------------------------------
        // RESTING
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit sleeps or lounges to recover Fatigue.
        /// Transitions: Idle (sufficiently rested), Eating (hungry).
        /// </summary>
        private sealed class RestingState : IUnitState
        {
            private readonly UnitStateMachine _m;

            public RestingState(UnitStateMachine m) => _m = m;

            public void Enter() { _m.Unit.CurrentTask = TaskType.Rest; }
            public void Exit()  { _m.Unit.CurrentTask = TaskType.None; }

            public void Tick(float dt)
            {
                _m.Unit.Fatigue = Math.Max(0f,
                    _m.Unit.Fatigue - _m._settings.RestRecoveryRate * dt);

                // Morale recovers a little while resting
                _m.Unit.Morale = Math.Min(100f, _m.Unit.Morale + 0.5f * dt);

                if (_m.NeedsFood())
                {
                    _m.TransitionTo(UnitState.Eating);
                    return;
                }

                // Once fatigue is below distress threshold the unit gets up.
                if (_m.Unit.Fatigue < _m._settings.FatigueDistressThreshold * 0.4f)
                    _m.TransitionTo(UnitState.Idle);
            }
        }

        // -----------------------------------------------------------------
        // EATING
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit visits the feasting hall to recover Hunger.
        /// Transitions: Idle (sated), Resting (still fatigued after eating).
        /// </summary>
        private sealed class EatingState : IUnitState
        {
            private readonly UnitStateMachine _m;

            public EatingState(UnitStateMachine m) => _m = m;

            public void Enter() { _m.Unit.CurrentTask = TaskType.Feast; }
            public void Exit()  { _m.Unit.CurrentTask = TaskType.None;  }

            public void Tick(float dt)
            {
                _m.Unit.Hunger = Math.Max(0f,
                    _m.Unit.Hunger - _m._settings.EatRecoveryRate * dt);

                // A good meal raises morale slightly
                _m.Unit.Morale = Math.Min(100f, _m.Unit.Morale + 0.3f * dt);

                if (_m.Unit.Hunger < 10f)
                {
                    // Done eating — check if rest is needed next
                    if (_m.NeedsRest())
                        _m.TransitionTo(UnitState.Resting);
                    else
                        _m.TransitionTo(UnitState.Idle);
                }
            }
        }

        // -----------------------------------------------------------------
        // FIGHTING
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit engages an enemy threat.
        /// Combat resolution is delegated to a higher-level combat system;
        /// the state machine tracks elapsed time and health to decide
        /// when to flee or return to idle.
        ///
        /// Transitions: Fleeing (low health), Idle (threat gone).
        /// </summary>
        private sealed class FightingState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private float _threatTimer; // simulates how long a threat lasts locally

            // Injected by the combat system via property (avoids constructor params).
            public bool ThreatPresent { get; set; } = true;

            public FightingState(UnitStateMachine m) => _m = m;

            public void Enter()
            {
                _threatTimer  = 0f;
                ThreatPresent = true;
                _m.Unit.CurrentTask = TaskType.Fight;
            }

            public void Exit() { _m.Unit.CurrentTask = TaskType.None; }

            public void Tick(float dt)
            {
                if (_m.IsLowHealth())
                {
                    // Cowardly and non-aggressive units flee; aggressive units fight to the death.
                    if (_m.Unit.Personality != PersonalityTrait.Aggressive
                        && _m.Unit.Personality != PersonalityTrait.Masochistic)
                    {
                        _m.TransitionTo(UnitState.Fleeing);
                        return;
                    }
                }

                // Simulate passive damage received in combat (combat system applies real damage).
                // Here we model the fear and fatigue cost of fighting.
                _m.Unit.Fear    = Math.Min(100f, _m.Unit.Fear    + 2f * dt);
                _m.Unit.Fatigue = Math.Min(100f, _m.Unit.Fatigue + 1.5f * dt);
                _m.Unit.Anger   = Math.Min(100f, _m.Unit.Anger   + 0.5f * dt);

                _threatTimer += dt;

                if (!ThreatPresent)
                    _m.TransitionTo(UnitState.Idle);
            }
        }

        // -----------------------------------------------------------------
        // FLEEING
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit runs away from a threat toward a safe area.
        /// Cowardly units flee faster; Aggressive units resist fleeing.
        ///
        /// Transitions: Idle (safe, health above critical),
        ///              Dead (health reaches zero while fleeing).
        /// </summary>
        private sealed class FleeingState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private float _fleeTimer;
            private const float FleeResolveSeconds = 5f; // time to reach safety

            public FleeingState(UnitStateMachine m) => _m = m;

            public void Enter()
            {
                _fleeTimer = 0f;
                // Fear spikes on entering flee
                _m.Unit.Fear = Math.Min(100f, _m.Unit.Fear + 20f);
            }

            public void Exit() { }

            public void Tick(float dt)
            {
                if (!_m.Unit.IsAlive)
                {
                    _m.TransitionTo(UnitState.Dead);
                    return;
                }

                float speed = _m._settings.FleeSpeedMultiplier;
                if (_m.Unit.Personality == PersonalityTrait.Cowardly)
                    speed *= 1.3f;

                _fleeTimer += dt * speed;

                // Reduce fear slightly as distance from threat increases
                _m.Unit.Fear = Math.Max(0f, _m.Unit.Fear - 5f * dt);

                if (_fleeTimer >= FleeResolveSeconds)
                {
                    // Reached safety — decide next state
                    if (_m.Unit.CurrentHealth <= 0f)
                        _m.TransitionTo(UnitState.Dead);
                    else if (_m.NeedsRest())
                        _m.TransitionTo(UnitState.Resting);
                    else
                        _m.TransitionTo(UnitState.Idle);
                }
            }
        }

        // -----------------------------------------------------------------
        // REBELLING
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit is in open revolt: sabotages rooms, may convert nearby units,
        /// and will eventually calm down if fear overwhelms anger or the
        /// rebellion duration expires.
        ///
        /// The strike type determines destructive behaviour.
        ///
        /// Transitions: Idle (anger drops below threshold or duration expires),
        ///              Imprisoned (captured by player/guards),
        ///              Dead (killed during suppression).
        /// </summary>
        private sealed class RebellionState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private float     _rebellionTimer;
            private StrikeType _strikeType;

            public bool Suppressed { get; set; } // set externally by guard/player system

            public RebellionState(UnitStateMachine m) => _m = m;

            public void Enter()
            {
                _rebellionTimer = 0f;
                Suppressed      = false;
                _m.Unit.RebelCount++;

                // Determine strike type from anger severity and personality
                _strikeType = DetermineStrikeType();

                _m._bus.Publish(new UnitRebellionStartedEvent(_m.Unit, _strikeType));
            }

            public void Exit() { }

            public void Tick(float dt)
            {
                if (!_m.Unit.IsAlive)     { _m.TransitionTo(UnitState.Dead);        return; }
                if (Suppressed)            { _m.TransitionTo(UnitState.Imprisoned);  return; }

                _rebellionTimer += dt;

                PerformRebellionAction(dt);

                // Rebellion ends if:
                //   a) Fear overwhelms anger (overlord managed to scare them into submission)
                //   b) Duration has elapsed and anger has decayed below threshold
                bool fearOverwhelms = _m.Unit.Fear > 90f && _m.Unit.Fear > _m.Unit.Anger + 20f;
                bool timedOut       = _rebellionTimer >= _m._settings.RebellionBaseDuration
                                      && _m.Unit.Anger < _m._settings.AngerRebellionThreshold;

                if (fearOverwhelms || timedOut)
                    _m.TransitionTo(UnitState.Idle);
            }

            private StrikeType DetermineStrikeType()
            {
                float anger = _m.Unit.Anger;

                return _m.Unit.Personality switch
                {
                    PersonalityTrait.Aggressive  => anger >= 95f
                                                    ? StrikeType.FullRebellion
                                                    : StrikeType.FightEachOther,
                    PersonalityTrait.Lazy        => StrikeType.SlowDown,
                    PersonalityTrait.Mercenary   => StrikeType.Walkout,
                    PersonalityTrait.Cowardly    => StrikeType.Walkout,
                    PersonalityTrait.Masochistic => StrikeType.Sabotage,
                    PersonalityTrait.Loyal       => anger >= 98f
                                                    ? StrikeType.SlowDown
                                                    : StrikeType.SlowDown, // Loyal rarely reach this point
                    _                            => anger >= 95f
                                                    ? StrikeType.FullRebellion
                                                    : StrikeType.Sabotage,
                };
            }

            private void PerformRebellionAction(float dt)
            {
                // The real damage delivery is handled by the room management system
                // which listens for UnitRebellionStartedEvent and tracks the unit's
                // assigned room. Here we model the psychological cost of rebelling.
                switch (_strikeType)
                {
                    case StrikeType.SlowDown:
                        // Unit drags its feet — anger decays faster, morale stays low
                        _m.Unit.Anger  = Math.Max(0f, _m.Unit.Anger  - 2f  * dt);
                        _m.Unit.Morale = Math.Max(0f, _m.Unit.Morale - 0.5f * dt);
                        break;

                    case StrikeType.Walkout:
                        // Attempts to leave — anger stays high until resolved
                        _m.Unit.AssignedRoomId = null;
                        _m.Unit.CurrentTask    = TaskType.None;
                        break;

                    case StrikeType.Sabotage:
                        // Physical damage to rooms is handled externally; unit gains anger relief
                        _m.Unit.Anger  = Math.Max(0f, _m.Unit.Anger  - 1.5f * dt);
                        _m.Unit.Morale = Math.Min(100f, _m.Unit.Morale + 1f * dt); // cathartic
                        break;

                    case StrikeType.FightEachOther:
                        // Reduces loyalty among nearby units (handled by higher-level system)
                        _m.Unit.Anger  = Math.Max(0f, _m.Unit.Anger  - 1f  * dt);
                        _m.Unit.Fatigue= Math.Min(100f, _m.Unit.Fatigue + 2f * dt);
                        break;

                    case StrikeType.FullRebellion:
                        // All-out; drains the unit but is hardest to suppress
                        _m.Unit.Fatigue = Math.Min(100f, _m.Unit.Fatigue + 3f * dt);
                        _m.Unit.Anger   = Math.Max(0f,   _m.Unit.Anger   - 0.5f * dt);
                        break;
                }
            }
        }

        // -----------------------------------------------------------------
        // IMPRISONED
        // -----------------------------------------------------------------
        /// <summary>
        /// Unit is locked up and cannot act. Anger decays slowly; Loyalty may erode.
        /// Transitions only via external player/system action (released or executed).
        /// </summary>
        private sealed class ImprisonedState : IUnitState
        {
            private readonly UnitStateMachine _m;

            public ImprisonedState(UnitStateMachine m) => _m = m;

            public void Enter()
            {
                _m.Unit.IsImprisoned = true;
                _m.Unit.CurrentTask  = TaskType.None;
            }

            public void Exit()
            {
                _m.Unit.IsImprisoned = false;
            }

            public void Tick(float dt)
            {
                if (!_m.Unit.IsAlive) { _m.TransitionTo(UnitState.Dead); return; }

                // Imprisonment decays anger slowly (no stimulation to sustain rage)
                _m.Unit.Anger  = Math.Max(0f, _m.Unit.Anger  - 1f   * dt);
                // Morale suffers
                _m.Unit.Morale = Math.Max(0f, _m.Unit.Morale - 0.5f * dt);
                // Long imprisonment erodes loyalty
                _m.Unit.Loyalty = Math.Max(0f, _m.Unit.Loyalty - 0.2f * dt);

                // No automatic exit — external system must call TransitionTo.
            }
        }

        // -----------------------------------------------------------------
        // IMPRESSED
        // -----------------------------------------------------------------
        /// <summary>
        /// Temporary high-morale state granted after a strong reward (gold bonus,
        /// praise, special feast). Unit works faster for a limited duration.
        ///
        /// Transitions: Working (duration expires).
        /// </summary>
        private sealed class ImpressedState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private float _impressedTimer;

            public ImpressedState(UnitStateMachine m) => _m = m;

            public void Enter()
            {
                _impressedTimer = 0f;
                // Temporary morale and productivity boost
                _m.Unit.Morale = Math.Min(100f,
                    _m.Unit.Morale + _m._settings.ImpressedProductivityBonus);
                _m.Unit.Anger  = Math.Max(0f, _m.Unit.Anger - 20f);
                _m.Unit.Fear   = Math.Max(0f, _m.Unit.Fear  - 10f);
            }

            public void Exit()
            {
                // Morale returns to a healthy but not max level when state ends
                _m.Unit.Morale = Math.Max(50f, _m.Unit.Morale - _m._settings.ImpressedProductivityBonus * 0.5f);
            }

            public void Tick(float dt)
            {
                if (!_m.Unit.IsAlive) { _m.TransitionTo(UnitState.Dead); return; }

                _impressedTimer += dt;

                // Even in this state, basic needs still apply
                if (_m.NeedsFood())  { _m.TransitionTo(UnitState.Eating);  return; }
                if (_m.NeedsRest())  { _m.TransitionTo(UnitState.Resting); return; }

                if (_impressedTimer >= _m._settings.ImpressedDuration)
                    _m.TransitionTo(UnitState.Working);
            }
        }

        // -----------------------------------------------------------------
        // DEAD
        // -----------------------------------------------------------------
        /// <summary>
        /// Terminal state. On entry publishes <see cref="UnitDiedEvent"/> and
        /// clears the unit's task. No further transitions are possible.
        /// </summary>
        private sealed class DeadState : IUnitState
        {
            private readonly UnitStateMachine _m;
            private bool _eventPublished;

            public DeadState(UnitStateMachine m) => _m = m;

            public void Enter()
            {
                _m.Unit.Kill(); // idempotent if already killed
                _m.Unit.CurrentTask    = TaskType.None;
                _m.Unit.AssignedRoomId = null;

                if (!_eventPublished)
                {
                    _eventPublished = true;
                    _m._bus.Publish(new UnitDiedEvent(_m.Unit));
                }
            }

            public void Exit()  { /* terminal — never exits */ }
            public void Tick(float dt) { /* nothing to do */ }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Central scene-level manager.  Maintains live lists of
    /// <see cref="UnitController"/> and <see cref="RoomController"/> instances
    /// so that other systems (e.g. <see cref="InvaderController"/>) can query
    /// the nearest valid target without expensive FindObjectOfType calls.
    ///
    /// <para>
    ///   Owns the singleton <see cref="Instance"/> reference.
    ///   Exactly one GameManager should exist per dungeon scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        // =====================================================================
        // Singleton
        // =====================================================================

        public static GameManager Instance { get; private set; }

        // =====================================================================
        // Runtime registries
        // =====================================================================

        private readonly List<UnitController> _units = new List<UnitController>();
        private readonly List<RoomController> _rooms = new List<RoomController>();

        // =====================================================================
        // Live system references
        // =====================================================================

        /// <summary>
        /// The active <see cref="CombatSystem"/> instance.
        /// Populated at startup; null until the dungeon combat loop begins.
        /// </summary>
        public CombatSystem CombatSystem { get; private set; }

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameManager] Duplicate instance — destroying new one.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            var settings = GameSettings.Default();
            CombatSystem = new CombatSystem(settings);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // =====================================================================
        // Registry management
        // =====================================================================

        /// <summary>Called by <see cref="UnitController"/> in its Awake/OnEnable.</summary>
        public void RegisterUnit(UnitController unit)
        {
            if (unit != null && !_units.Contains(unit))
                _units.Add(unit);
        }

        /// <summary>Called by <see cref="UnitController"/> in its OnDisable/OnDestroy.</summary>
        public void UnregisterUnit(UnitController unit)
        {
            _units.Remove(unit);
        }

        /// <summary>Called by <see cref="RoomController"/> when it is initialised.</summary>
        public void RegisterRoom(RoomController room)
        {
            if (room != null && !_rooms.Contains(room))
                _rooms.Add(room);
        }

        /// <summary>Called by <see cref="RoomController"/> when it is destroyed.</summary>
        public void UnregisterRoom(RoomController room)
        {
            _rooms.Remove(room);
        }

        // =====================================================================
        // Query API — used by InvaderController
        // =====================================================================

        /// <summary>
        /// Returns the nearest alive <see cref="UnitController"/> that is in the
        /// <see cref="UnitState.Fighting"/> state, or <c>null</c> if none exist.
        /// </summary>
        public UnitController GetNearestFightingUnit(Vector3 from)
        {
            UnitController best     = null;
            float          bestDist = float.MaxValue;

            foreach (var unit in _units)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit.Data == null || unit.Data.CurrentState != UnitState.Fighting) continue;

                float dist = Vector3.SqrMagnitude(unit.transform.position - from);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = unit;
                }
            }

            return best;
        }

        /// <summary>
        /// Returns the nearest alive and operational <see cref="RoomController"/>,
        /// or <c>null</c> if none exist.
        /// </summary>
        public RoomController GetNearestRoom(Vector3 from)
        {
            RoomController best     = null;
            float          bestDist = float.MaxValue;

            foreach (var room in _rooms)
            {
                if (room == null || !room.IsOperational) continue;

                float dist = Vector3.SqrMagnitude(room.transform.position - from);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = room;
                }
            }

            return best;
        }

        /// <summary>Returns a read-only snapshot of all registered units.</summary>
        public IReadOnlyList<UnitController> GetAllUnits() => _units;

        /// <summary>Returns a read-only snapshot of all registered rooms.</summary>
        public IReadOnlyList<RoomController> GetAllRooms() => _rooms;
    }
}

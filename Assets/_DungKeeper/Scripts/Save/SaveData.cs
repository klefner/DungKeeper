using System;
using System.Collections.Generic;

namespace DungKeeper
{
    // =========================================================================
    // Top-level save data container
    // =========================================================================

    /// <summary>
    /// Complete serializable snapshot of one save slot.
    /// All fields use primitive types (no Unity or MonoBehaviour types) so the
    /// class survives JSON round-trips without a custom serializer.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        // -------------------------------------------------------------------------
        // Meta
        // -------------------------------------------------------------------------

        /// <summary>Semantic version of the save format. Bump when fields change incompatibly.</summary>
        public string  Version   = "1.0.0";

        /// <summary>Identifier for this save slot (e.g. "slot0", "slot1").</summary>
        public string  SaveSlot;

        /// <summary>UTC timestamp when the save was written.</summary>
        public DateTime SaveTimestamp;

        /// <summary>Cumulative seconds the player has been active in this slot.</summary>
        public float   TotalPlayTime;

        // -------------------------------------------------------------------------
        // Population
        // -------------------------------------------------------------------------

        public List<UnitSaveData> Units = new List<UnitSaveData>();

        // -------------------------------------------------------------------------
        // Dungeon layout
        // -------------------------------------------------------------------------

        public List<RoomSaveData> Rooms = new List<RoomSaveData>();

        // -------------------------------------------------------------------------
        // Economy
        // -------------------------------------------------------------------------

        /// <summary>
        /// Resource pool amounts keyed by <see cref="ResourceType"/>.ToString().
        /// Stored as strings to survive JSON serialization without a custom converter.
        /// </summary>
        public Dictionary<string, float> Resources = new Dictionary<string, float>();

        // -------------------------------------------------------------------------
        // Statistics
        // -------------------------------------------------------------------------

        public int TotalSlapsDelivered;
        public int TotalUnitsLost;
        public int TotalThreatsDefeated;
        public int TotalThreatsLost;

        // -------------------------------------------------------------------------
        // Progression
        // -------------------------------------------------------------------------

        /// <summary>Room type names unlocked so far (populated from <see cref="RoomType"/>.ToString()).</summary>
        public List<string> UnlockedRoomTypes = new List<string>();

        // -------------------------------------------------------------------------
        // Per-slot overrides
        // -------------------------------------------------------------------------

        /// <summary>Player-specific balance overrides stored with the save.</summary>
        public GameSettings SettingsOverride = GameSettings.Default();
    }

    // =========================================================================
    // Unit save data
    // =========================================================================

    /// <summary>
    /// Serializable mirror of <see cref="UnitData"/> using only primitive fields.
    /// </summary>
    [Serializable]
    public sealed class UnitSaveData
    {
        // Identity
        public string          Id;
        public string          Name;
        public string          Role;        // UnitRole.ToString()
        public string          Personality; // PersonalityTrait.ToString()
        public string          State;       // UnitState.ToString()

        // Assignment
        public string          AssignedRoomId;
        public string          CurrentTask; // TaskType.ToString()

        // Vitals
        public float           Health;
        public float           MaxHealth;
        public float           Hunger;
        public float           Fatigue;

        // Psychology
        public float           Morale;
        public float           Fear;
        public float           Anger;
        public float           Loyalty;

        // Work
        public float           Productivity;

        // Combat
        public float           Attack;
        public float           Defense;

        // -------------------------------------------------------------------------
        // Conversion helpers
        // -------------------------------------------------------------------------

        /// <summary>Creates a <see cref="UnitSaveData"/> snapshot from a live <see cref="UnitData"/>.</summary>
        public static UnitSaveData FromUnit(UnitData u)
        {
            if (u == null) throw new ArgumentNullException(nameof(u));
            return new UnitSaveData
            {
                Id             = u.Id,
                Name           = u.Name,
                Role           = u.Role.ToString(),
                Personality    = u.Personality.ToString(),
                State          = u.State.ToString(),
                AssignedRoomId = u.AssignedRoomId,
                CurrentTask    = u.CurrentTask.ToString(),
                Health         = u.Health,
                MaxHealth      = u.MaxHealth,
                Hunger         = u.Hunger,
                Fatigue        = u.Fatigue,
                Morale         = u.Morale,
                Fear           = u.Fear,
                Anger          = u.Anger,
                Loyalty        = u.Loyalty,
                Productivity   = u.Productivity,
                Attack         = u.Attack,
                Defense        = u.Defense,
            };
        }

        /// <summary>Reconstructs a live <see cref="UnitData"/> from a persisted snapshot.</summary>
        public static UnitData ToUnit(UnitSaveData s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            if (!Enum.TryParse(s.Role,        out UnitRole        role))        role        = UnitRole.Worker;
            if (!Enum.TryParse(s.Personality, out PersonalityTrait personality)) personality = PersonalityTrait.Loyal;
            if (!Enum.TryParse(s.State,       out UnitState       state))       state       = UnitState.Idle;
            if (!Enum.TryParse(s.CurrentTask, out TaskType        task))        task        = TaskType.None;

            var u = new UnitData(s.Id, s.Name, role, personality)
            {
                State          = state,
                AssignedRoomId = s.AssignedRoomId,
                CurrentTask    = task,
                Health         = s.Health,
                MaxHealth      = s.MaxHealth,
                Hunger         = s.Hunger,
                Fatigue        = s.Fatigue,
                Morale         = s.Morale,
                Fear           = s.Fear,
                Anger          = s.Anger,
                Loyalty        = s.Loyalty,
                Productivity   = s.Productivity,
                Attack         = s.Attack,
                Defense        = s.Defense,
            };

            return u;
        }
    }

    // =========================================================================
    // Room save data
    // =========================================================================

    /// <summary>
    /// Serializable snapshot of one placed dungeon room.
    /// </summary>
    [Serializable]
    public sealed class RoomSaveData
    {
        public string       Id;
        public string       RoomType;   // RoomType.ToString()
        public int          GridX;
        public int          GridY;
        public float        Health;
        public bool         IsActive;
        public List<string> AssignedUnitIds = new List<string>();

        // -------------------------------------------------------------------------
        // Conversion helpers
        // -------------------------------------------------------------------------

        /// <summary>Creates a <see cref="RoomSaveData"/> snapshot from a live <see cref="RoomData"/>.</summary>
        /// <param name="r">The room to snapshot.</param>
        /// <param name="gridX">Grid X coordinate — sourced from <see cref="RoomManager"/> grid key.</param>
        /// <param name="gridY">Grid Y (Z in Unity 3-D) coordinate.</param>
        public static RoomSaveData FromRoom(RoomData r, int gridX = 0, int gridY = 0)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return new RoomSaveData
            {
                Id              = r.Id,
                RoomType        = r.Type.ToString(),
                GridX           = gridX,
                GridY           = gridY,
                Health          = r.Health,
                IsActive        = r.IsActive,
                AssignedUnitIds = new List<string>(r.AssignedUnitIds),
            };
        }

        /// <summary>Reconstructs a live <see cref="RoomData"/> from a persisted snapshot.</summary>
        public static RoomData ToRoom(RoomSaveData s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));

            if (!Enum.TryParse(s.RoomType, out DungKeeper.RoomType type))
                type = DungKeeper.RoomType.ProductionChamber;

            // Use conservative defaults for capacity and production rate;
            // the RoomManager will override these from RoomTypeSO after load.
            var r = RoomData.Create(
                name: type.ToString(),
                type: type,
                capacity: 4,
                productionRate: 1f,
                maxHealth: 200f);

            // Restore persisted mutable state.
            r.Health   = s.Health;
            r.IsActive = s.IsActive;

            r.AssignedUnitIds.Clear();
            if (s.AssignedUnitIds != null)
                r.AssignedUnitIds.AddRange(s.AssignedUnitIds);

            return r;
        }
    }
}

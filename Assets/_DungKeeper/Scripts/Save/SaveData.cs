using System;
using System.Collections.Generic;

namespace DungKeeper
{
    // =========================================================================
    // Top-level save data container
    // =========================================================================

    /// <summary>
    /// Complete serializable snapshot of one save slot.
    /// All fields use serialization-friendly types (primitives, enums, and lists)
    /// so the class survives Unity's <c>JsonUtility</c> round-trips without a
    /// custom serializer.
    ///
    /// Instantiated by <see cref="GameManager.GetSaveData"/> and consumed by
    /// <see cref="GameManager.LoadFromSaveData"/>; persisted by <see cref="SaveSystem"/>.
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

        /// <summary>Cumulative in-game time accrued in this slot, in seconds.</summary>
        public float   GameTime;

        // -------------------------------------------------------------------------
        // Resources (flat floats to match GameManager field access)
        // -------------------------------------------------------------------------

        public float Gold;
        public float Essence;
        public float FearResource;

        // -------------------------------------------------------------------------
        // Population
        // -------------------------------------------------------------------------

        public List<UnitSaveData> Units = new List<UnitSaveData>();

        // -------------------------------------------------------------------------
        // Dungeon layout
        // -------------------------------------------------------------------------

        public List<RoomSaveData> Rooms = new List<RoomSaveData>();

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

        /// <summary>
        /// Room type names the player has unlocked.
        /// Stored as strings (via <c>RoomType.ToString()</c>) for forward compatibility.
        /// </summary>
        public List<string> UnlockedRoomTypes = new List<string>();

        // -------------------------------------------------------------------------
        // Per-slot overrides
        // -------------------------------------------------------------------------

        /// <summary>Player-specific balance overrides stored alongside the save.</summary>
        public GameSettings SettingsOverride = GameSettings.Default();
    }

    // =========================================================================
    // Unit save data
    // =========================================================================

    /// <summary>
    /// Serializable snapshot of one unit's persistent state.
    /// Enum fields are stored directly so they survive Unity's <c>JsonUtility</c>.
    /// </summary>
    [Serializable]
    public sealed class UnitSaveData
    {
        // Identity
        public string          Id;
        public string          Name;
        public UnitRole        Role;
        public PersonalityTrait Personality;
        public UnitState       State;

        // Assignment
        public string          AssignedRoomId;
        public TaskType        CurrentTask;

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

        // World position for respawning the visual controller.
        public float           PosX;
        public float           PosY;
        public float           PosZ;
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
        public string       Name;
        public RoomType     Type;
        public int          Capacity;
        public float        ProductionRate;
        public float        BaseProductionRate;
        public float        Health;
        public float        MaxHealth;
        public bool         IsActive;
        public List<string> AssignedUnitIds = new List<string>();

        // Grid position (world-space coordinates rounded to nearest int by GameManager).
        public int          GridX;
        public int          GridY;
        public int          GridZ;
    }
}

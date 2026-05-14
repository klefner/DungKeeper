using System;
using System.Collections.Generic;

namespace DungKeeper
{
    /// <summary>
    /// All mutable runtime state for a single dungeon room.
    /// Pure data object — no MonoBehaviour dependency.
    /// </summary>
    [Serializable]
    public sealed class RoomData
    {
        // -------------------------------------------------------------------------
        // Identity
        // -------------------------------------------------------------------------

        /// <summary>Stable GUID assigned at construction time.</summary>
        public string Id { get; }

        /// <summary>Display name shown in the UI.</summary>
        public string Name { get; set; }

        public RoomType Type { get; }

        // -------------------------------------------------------------------------
        // Capacity & assignment
        // -------------------------------------------------------------------------

        /// <summary>Maximum number of units that can be assigned to this room.</summary>
        public int Capacity { get; set; }

        /// <summary>IDs of units currently assigned to this room.</summary>
        public List<string> AssignedUnitIds { get; } = new List<string>();

        // -------------------------------------------------------------------------
        // Economy
        // -------------------------------------------------------------------------

        /// <summary>
        /// Base resource production rate per second.
        /// Reduced by sabotage; restored by repair actions.
        /// </summary>
        public float ProductionRate { get; set; }

        /// <summary>Production rate at full health — used as the repair target.</summary>
        public float BaseProductionRate { get; }

        // -------------------------------------------------------------------------
        // Structural health
        // -------------------------------------------------------------------------

        public float Health    { get; set; }
        public float MaxHealth { get; set; }

        public bool IsOperational => Health > 0f && ProductionRate > 0f;

        /// <summary>Whether the overlord has toggled the room on. Inactive rooms do not produce resources.</summary>
        public bool IsActive { get; set; } = true;

        // -------------------------------------------------------------------------
        // Convenience
        // -------------------------------------------------------------------------

        public int WorkerCount => AssignedUnitIds.Count;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public RoomData(string id, string name, RoomType type, int capacity, float productionRate, float maxHealth = 200f)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Room id must not be null or empty.", nameof(id));

            Id                 = id;
            Name               = name;
            Type               = type;
            Capacity           = capacity;
            ProductionRate     = productionRate;
            BaseProductionRate = productionRate;
            MaxHealth          = maxHealth;
            Health             = maxHealth;
        }

        // -------------------------------------------------------------------------
        // Factory helpers
        // -------------------------------------------------------------------------

        /// <summary>Creates a room with a new random GUID.</summary>
        public static RoomData Create(string name, RoomType type, int capacity, float productionRate, float maxHealth = 200f)
            => new RoomData(Guid.NewGuid().ToString(), name, type, capacity, productionRate, maxHealth);

        /// <summary>
        /// Restores ProductionRate toward BaseProductionRate by the given delta.
        /// Clamps to BaseProductionRate.
        /// </summary>
        public void RepairProductionRate(float delta)
        {
            ProductionRate = Math.Min(BaseProductionRate, ProductionRate + delta);
        }

        public override string ToString()
            => $"[Room {Name} | {Type} | Workers:{WorkerCount}/{Capacity} | Rate:{ProductionRate:F2}]";
    }
}

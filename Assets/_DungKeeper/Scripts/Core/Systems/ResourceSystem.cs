using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungKeeper
{
    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Fired whenever a resource pool value changes.</summary>
    public sealed class ResourceChangedEvent
    {
        public ResourceType Type        { get; }
        public float        PreviousValue { get; }
        public float        NewValue    { get; }
        public float        Delta       => NewValue - PreviousValue;

        public ResourceChangedEvent(ResourceType type, float previousValue, float newValue)
        {
            Type          = type;
            PreviousValue = previousValue;
            NewValue      = newValue;
        }
    }

    // -------------------------------------------------------------------------
    // ResourcePool — value type representing one pool of a single resource
    // -------------------------------------------------------------------------

    /// <summary>
    /// Snapshot of a single resource pool.
    /// Mutated only by <see cref="ResourceSystem"/> — treat as read-only externally.
    /// </summary>
    public struct ResourcePool
    {
        public ResourceType Type;
        public float        Current;
        public float        Max;
        public float        ProductionRate;   // net rate for this tick, recalculated each Tick()

        public ResourcePool(ResourceType type, float current, float max, float productionRate)
        {
            Type           = type;
            Current        = current;
            Max            = max;
            ProductionRate = productionRate;
        }

        public float Percentage => Max > 0f ? Current / Max : 0f;

        public override string ToString()
            => $"{Type}: {Current:F1}/{Max:F1} (+{ProductionRate:F2}/s)";
    }

    // -------------------------------------------------------------------------
    // ResourceSystem
    // -------------------------------------------------------------------------

    /// <summary>
    /// Manages all dungeon economy resources: Gold, Essence, and Fear.
    /// <para>
    ///   Production is derived each tick from active rooms and their assigned workers.
    ///   Capacity for Gold can be expanded by <see cref="RoomType.TreasureVault"/> rooms.
    /// </para>
    /// <para>Pure C# — no Unity or MonoBehaviour dependency.</para>
    /// </summary>
    public sealed class ResourceSystem
    {
        // -------------------------------------------------------------------------
        // Constants
        // -------------------------------------------------------------------------

        // Base Gold max before TreasureVault bonuses
        private const float BaseGoldMax         = 9999f;
        private const float BaseEssenceMax      = 500f;
        private const float BaseFearMax         = 100f;

        // Per-room bonus capacity for TreasureVault
        private const float TreasureVaultGoldBonus = 5000f;

        // Worker productivity contribution per unit of Productivity stat
        private const float WorkerProductivityScale = 1.0f;

        // Fear ambient calculation: fear decays toward the "ambient" level each tick
        private const float FearDecayRate       = GameConstants.FearDecayRate; // per second

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        /// <summary>Live resource pools, keyed by <see cref="ResourceType"/>.</summary>
        public Dictionary<ResourceType, ResourcePool> Pools { get; }

        /// <summary>
        /// Fired whenever TrySpend or Add changes a pool value.
        /// Subscribe before calling Tick to receive production events too.
        /// </summary>
        public event Action<ResourceChangedEvent> OnResourceChanged;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ResourceSystem()
        {
            Pools = new Dictionary<ResourceType, ResourcePool>
            {
                [ResourceType.Gold]    = new ResourcePool(ResourceType.Gold,    0f, BaseGoldMax,    0f),
                [ResourceType.Essence] = new ResourcePool(ResourceType.Essence, 0f, BaseEssenceMax, 0f),
                [ResourceType.Fear]    = new ResourcePool(ResourceType.Fear,    0f, BaseFearMax,    0f),
            };
        }

        // -------------------------------------------------------------------------
        // Tick — called once per simulation frame
        // -------------------------------------------------------------------------

        /// <summary>
        /// Advances resource production by <paramref name="deltaTime"/> seconds.
        /// <list type="bullet">
        ///   <item>ProductionChamber + Worker units → Gold income.</item>
        ///   <item>SummoningCircle + Researcher units → Essence income.</item>
        ///   <item>Fear is ambient: rises with unit average fear, decays slowly toward that level.</item>
        ///   <item>TreasureVault rooms expand Gold max capacity.</item>
        /// </list>
        /// </summary>
        public void Tick(IEnumerable<UnitData> units, IEnumerable<RoomData> rooms, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            var unitList = units  as IReadOnlyList<UnitData> ?? units.ToList();
            var roomList = rooms  as IReadOnlyList<RoomData> ?? rooms.ToList();

            // --- 1. Recalculate Gold capacity from TreasureVaults --------------------

            float goldMax = BaseGoldMax;
            foreach (var room in roomList)
            {
                if (room.Type == RoomType.TreasureVault && room.IsOperational)
                    goldMax += TreasureVaultGoldBonus;
            }

            var goldPool = Pools[ResourceType.Gold];
            goldPool.Max = goldMax;
            Pools[ResourceType.Gold] = goldPool;

            // --- 2. Gold production from ProductionChambers + Workers ----------------

            float goldRate = 0f;
            foreach (var room in roomList)
            {
                if (room.Type != RoomType.ProductionChamber || !room.IsOperational)
                    continue;

                // Sum productivity of assigned Worker units
                float workerContribution = 0f;
                foreach (var unitId in room.AssignedUnitIds)
                {
                    var unit = FindUnit(unitList, unitId);
                    if (unit == null || !unit.IsAlive) continue;
                    if (unit.Role != UnitRole.Worker) continue;
                    if (unit.State == UnitState.Working || unit.State == UnitState.Impressed)
                        workerContribution += unit.Productivity * WorkerProductivityScale;
                }

                goldRate += room.ProductionRate * workerContribution;
            }

            // --- 3. Essence production from SummoningCircles + Researchers -----------

            float essenceRate = 0f;
            foreach (var room in roomList)
            {
                if (room.Type != RoomType.SummoningCircle || !room.IsOperational)
                    continue;

                float researcherContribution = 0f;
                foreach (var unitId in room.AssignedUnitIds)
                {
                    var unit = FindUnit(unitList, unitId);
                    if (unit == null || !unit.IsAlive) continue;
                    if (unit.Role != UnitRole.Researcher) continue;
                    if (unit.State == UnitState.Working || unit.State == UnitState.Impressed)
                        researcherContribution += unit.Productivity * WorkerProductivityScale;
                }

                essenceRate += room.ProductionRate * researcherContribution;
            }

            // --- 4. Fear: ambient based on average unit fear, decays toward it -------

            float ambientFear = 0f;
            int   aliveCount  = 0;
            foreach (var unit in unitList)
            {
                if (!unit.IsAlive) continue;
                ambientFear += unit.Fear;
                aliveCount++;
            }

            float fearCapacity = Pools[ResourceType.Fear].Max;
            float fearTarget   = aliveCount > 0
                ? (ambientFear / aliveCount / 100f) * fearCapacity
                : 0f;

            float currentFear = Pools[ResourceType.Fear].Current;
            float fearDelta;
            if (currentFear < fearTarget)
            {
                // Rise toward ambient fairly quickly
                fearDelta = Math.Min(fearTarget - currentFear, (fearTarget - currentFear) * 2f * deltaTime);
            }
            else
            {
                // Decay slowly back down
                fearDelta = -FearDecayRate * deltaTime;
                // Don't overshoot below the ambient floor
                if (currentFear + fearDelta < fearTarget)
                    fearDelta = fearTarget - currentFear;
            }

            float fearRate = aliveCount > 0 ? fearDelta / deltaTime : -FearDecayRate;

            // --- 5. Store computed rates then apply production -----------------------

            UpdateRate(ResourceType.Gold,    goldRate);
            UpdateRate(ResourceType.Essence, essenceRate);
            UpdateRate(ResourceType.Fear,    fearRate);

            InternalAdd(ResourceType.Gold,    goldRate    * deltaTime);
            InternalAdd(ResourceType.Essence, essenceRate * deltaTime);
            InternalAdd(ResourceType.Fear,    fearDelta);
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Deducts <paramref name="amount"/> from the pool if available.
        /// Fires <see cref="OnResourceChanged"/> on success.
        /// </summary>
        /// <returns>True if the amount was available and was deducted.</returns>
        public bool TrySpend(ResourceType type, float amount)
        {
            if (amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount), "Spend amount must be non-negative.");

            var pool = Pools[type];
            if (pool.Current < amount) return false;

            float previous = pool.Current;
            pool.Current  -= amount;
            pool.Current   = Math.Max(0f, pool.Current);
            Pools[type]    = pool;

            FireEvent(type, previous, pool.Current);
            return true;
        }

        /// <summary>
        /// Adds <paramref name="amount"/> to the pool, clamped to its max capacity.
        /// Fires <see cref="OnResourceChanged"/>.
        /// </summary>
        public void Add(ResourceType type, float amount)
        {
            if (amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount), "Add amount must be non-negative.");

            InternalAdd(type, amount);
        }

        /// <summary>Returns the current value of a resource pool.</summary>
        public float Get(ResourceType type) => Pools[type].Current;

        /// <summary>
        /// Overrides the stored production rate for a resource.
        /// Useful for external modifiers (buffs, events) that bypass room calculation.
        /// </summary>
        public void SetProductionRate(ResourceType type, float rate) => UpdateRate(type, rate);

        /// <summary>
        /// Returns a human-readable summary of all resource pools for UI display.
        /// </summary>
        public string GetResourceSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Resources ===");
            foreach (var pool in Pools.Values)
            {
                string arrow = pool.ProductionRate >= 0f ? "▲" : "▼";
                sb.AppendLine(
                    $"  {pool.Type,-10} {pool.Current,8:F1} / {pool.Max,8:F1}  " +
                    $"{arrow} {Math.Abs(pool.ProductionRate),6:F2}/s  " +
                    $"({pool.Percentage * 100f:F0}%)");
            }
            return sb.ToString().TrimEnd();
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        /// <summary>Adds to a pool, clamping to [0, max], firing an event only if value changed.</summary>
        private void InternalAdd(ResourceType type, float delta)
        {
            if (Math.Abs(delta) < 0.0001f) return;

            var pool     = Pools[type];
            float before = pool.Current;
            pool.Current = Math.Clamp(pool.Current + delta, 0f, pool.Max);
            Pools[type]  = pool;

            if (Math.Abs(pool.Current - before) > 0.0001f)
                FireEvent(type, before, pool.Current);
        }

        private void UpdateRate(ResourceType type, float rate)
        {
            var pool           = Pools[type];
            pool.ProductionRate = rate;
            Pools[type]        = pool;
        }

        private void FireEvent(ResourceType type, float previous, float current)
            => OnResourceChanged?.Invoke(new ResourceChangedEvent(type, previous, current));

        /// <summary>Linear scan — in production replace with a lookup dictionary if unit counts grow large.</summary>
        private static UnitData FindUnit(IReadOnlyList<UnitData> units, string id)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Id == id) return units[i];
            return null;
        }
    }
}

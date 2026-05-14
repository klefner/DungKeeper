using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DungKeeper
{
    // ResourceChangedEvent is defined in EventBus.cs.

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

        /// <summary>Current amount held.</summary>
        public float Current;

        /// <summary>Hard ceiling for this pool; clamped on every mutation.</summary>
        public float Max;

        /// <summary>Net production rate in units/second, recalculated each Tick.</summary>
        public float ProductionRate;

        public ResourcePool(ResourceType type, float current, float max, float productionRate)
        {
            Type           = type;
            Current        = current;
            Max            = max;
            ProductionRate = productionRate;
        }

        /// <summary>Current as a 0–1 fraction of Max.</summary>
        public float Percentage => Max > 0f ? Current / Max : 0f;

        public override string ToString()
            => $"{Type}: {Current:F1}/{Max:F1}  ({ProductionRate:+0.00;-0.00;0.00}/s)";
    }

    // -------------------------------------------------------------------------
    // ResourceSystem
    // -------------------------------------------------------------------------

    /// <summary>
    /// Manages all dungeon economy resources: Gold, Essence, and Fear.
    ///
    /// Production rules per Tick:
    /// <list type="bullet">
    ///   <item><see cref="RoomType.ProductionChamber"/> rooms with assigned Worker units → Gold.</item>
    ///   <item><see cref="RoomType.SummoningCircle"/> rooms with Researcher units → Essence.</item>
    ///   <item>Fear is ambient: converges toward the average unit Fear, decays slowly.</item>
    ///   <item><see cref="RoomType.TreasureVault"/> rooms expand Gold max capacity.</item>
    /// </list>
    ///
    /// <para>Pure C# — no Unity or MonoBehaviour dependency.</para>
    /// </summary>
    public sealed class ResourceSystem
    {
        // -------------------------------------------------------------------------
        // Tuning constants
        // -------------------------------------------------------------------------

        private const float BaseGoldMax            = 9999f;
        private const float BaseEssenceMax         = 500f;
        private const float BaseFearMax            = 100f;
        private const float TreasureVaultGoldBonus = 5000f;

        // How quickly Fear rises toward the ambient target (fraction of gap per second).
        private const float FearRiseSpeed   = 2.0f;

        // Passive Fear decay rate when ambient target is below current value.
        private const float FearDecayPerSec = GameConstants.FearDecayRate;

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        /// <summary>Live resource pools, keyed by <see cref="ResourceType"/>.</summary>
        public Dictionary<ResourceType, ResourcePool> Pools { get; }

        /// <summary>
        /// Raised whenever a pool value changes (production, spend, or manual add).
        /// Subscribe before calling Tick if you want production-generated events too.
        /// </summary>
        public event Action<ResourceChangedEvent> OnResourceChanged;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        /// <summary>Initialises all pools: Gold(0/9999), Essence(0/500), Fear(0/100).</summary>
        public ResourceSystem()
        {
            Pools = new Dictionary<ResourceType, ResourcePool>(3)
            {
                [ResourceType.Gold]    = new ResourcePool(ResourceType.Gold,    0f, BaseGoldMax,    0f),
                [ResourceType.Essence] = new ResourcePool(ResourceType.Essence, 0f, BaseEssenceMax, 0f),
                [ResourceType.Fear]    = new ResourcePool(ResourceType.Fear,    0f, BaseFearMax,    0f),
            };
        }

        // -------------------------------------------------------------------------
        // Tick — primary simulation entry point
        // -------------------------------------------------------------------------

        /// <summary>
        /// Advances resource production/decay by <paramref name="deltaTime"/> seconds.
        /// Reads <paramref name="units"/> and <paramref name="rooms"/> but never mutates them.
        /// </summary>
        public void Tick(IEnumerable<UnitData> units, IEnumerable<RoomData> rooms, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            // Materialise once to allow multiple passes without re-allocating.
            var unitList = units as IList<UnitData> ?? units.ToList();
            var roomList = rooms as IList<RoomData> ?? rooms.ToList();

            // ------------------------------------------------------------------
            // 1. Expand Gold capacity for each active TreasureVault
            // ------------------------------------------------------------------

            float goldMax = BaseGoldMax;
            for (int i = 0; i < roomList.Count; i++)
            {
                var r = roomList[i];
                if (r.Type == RoomType.TreasureVault && r.IsOperational && r.IsActive)
                    goldMax += TreasureVaultGoldBonus;
            }

            var gp  = Pools[ResourceType.Gold];
            gp.Max  = goldMax;
            Pools[ResourceType.Gold] = gp;

            // ------------------------------------------------------------------
            // 2. Gold income — ProductionChambers × worker productivity
            // ------------------------------------------------------------------

            float goldRate = 0f;
            for (int i = 0; i < roomList.Count; i++)
            {
                var room = roomList[i];
                if (room.Type != RoomType.ProductionChamber) continue;
                if (!room.IsOperational || !room.IsActive)   continue;

                float workerSum = 0f;
                for (int j = 0; j < room.AssignedUnitIds.Count; j++)
                {
                    var unit = FindUnit(unitList, room.AssignedUnitIds[j]);
                    if (unit == null || !unit.IsAlive)                        continue;
                    if (unit.Role != UnitRole.Worker)                         continue;
                    if (unit.CurrentState != UnitState.Working &&
                        unit.CurrentState != UnitState.Impressed)             continue;

                    workerSum += unit.GetProductivityMultiplier();
                }

                goldRate += room.ProductionRate * workerSum;
            }

            // ------------------------------------------------------------------
            // 3. Essence income — SummoningCircles × researcher productivity
            // ------------------------------------------------------------------

            float essenceRate = 0f;
            for (int i = 0; i < roomList.Count; i++)
            {
                var room = roomList[i];
                if (room.Type != RoomType.SummoningCircle) continue;
                if (!room.IsOperational || !room.IsActive)  continue;

                float researcherSum = 0f;
                for (int j = 0; j < room.AssignedUnitIds.Count; j++)
                {
                    var unit = FindUnit(unitList, room.AssignedUnitIds[j]);
                    if (unit == null || !unit.IsAlive)                          continue;
                    if (unit.Role != UnitRole.Researcher)                       continue;
                    if (unit.CurrentState != UnitState.Working &&
                        unit.CurrentState != UnitState.Impressed)               continue;

                    researcherSum += unit.GetProductivityMultiplier();
                }

                essenceRate += room.ProductionRate * researcherSum;
            }

            // ------------------------------------------------------------------
            // 4. Fear — ambient level driven by average unit Fear stat
            // ------------------------------------------------------------------

            float totalUnitFear = 0f;
            int   aliveCount    = 0;
            for (int i = 0; i < unitList.Count; i++)
            {
                if (!unitList[i].IsAlive) continue;
                totalUnitFear += unitList[i].Fear;
                aliveCount++;
            }

            float fearCapacity = Pools[ResourceType.Fear].Max;
            // Ambient target: average unit Fear maps linearly onto Fear pool capacity.
            float fearTarget = aliveCount > 0
                ? (totalUnitFear / aliveCount / 100f) * fearCapacity
                : 0f;

            float currentFear = Pools[ResourceType.Fear].Current;
            float fearDelta;
            if (currentFear < fearTarget)
            {
                // Rise quickly toward ambient
                float gap  = fearTarget - currentFear;
                fearDelta  = Math.Min(gap, gap * FearRiseSpeed * deltaTime);
            }
            else
            {
                // Passive decay back toward ambient floor
                fearDelta = -FearDecayPerSec * deltaTime;
                // Clamp: never decay past the ambient floor in one tick
                if (currentFear + fearDelta < fearTarget)
                    fearDelta = fearTarget - currentFear;
            }

            float fearRate = deltaTime > 0f ? fearDelta / deltaTime : 0f;

            // ------------------------------------------------------------------
            // 5. Commit rates and apply deltas to all pools
            // ------------------------------------------------------------------

            WriteRate(ResourceType.Gold,    goldRate);
            WriteRate(ResourceType.Essence, essenceRate);
            WriteRate(ResourceType.Fear,    fearRate);

            InternalAdd(ResourceType.Gold,    goldRate    * deltaTime);
            InternalAdd(ResourceType.Essence, essenceRate * deltaTime);
            InternalAdd(ResourceType.Fear,    fearDelta);
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Deducts <paramref name="amount"/> from the pool if sufficient funds exist.
        /// Fires <see cref="OnResourceChanged"/> and publishes to <see cref="EventBus.Global"/> on success.
        /// </summary>
        /// <returns>True when the amount was available and has been deducted.</returns>
        public bool TrySpend(ResourceType type, float amount)
        {
            if (amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount), "Spend amount must be non-negative.");

            var pool = Pools[type];
            if (pool.Current < amount) return false;

            float before   = pool.Current;
            pool.Current   = Math.Max(0f, pool.Current - amount);
            Pools[type]    = pool;

            float actualDelta = pool.Current - before;   // negative
            FireEvent(type, actualDelta, pool.Current);
            return true;
        }

        /// <summary>
        /// Adds <paramref name="amount"/> to the pool, clamped to its max capacity.
        /// Fires <see cref="OnResourceChanged"/> and publishes to <see cref="EventBus.Global"/>.
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
        /// Overrides the stored production rate for a resource type.
        /// The value will be replaced on the next call to Tick unless the caller keeps setting it.
        /// Useful for external buffs/events that add a flat rate on top of room production.
        /// </summary>
        public void SetProductionRate(ResourceType type, float rate) => WriteRate(type, rate);

        /// <summary>
        /// Returns a formatted, multi-line string summarising all resource pools for UI display.
        /// </summary>
        public string GetResourceSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Resources ===");
            foreach (var pool in Pools.Values)
            {
                char trend = pool.ProductionRate > 0.005f ? '▲'
                           : pool.ProductionRate < -0.005f ? '▼'
                           : '─';
                sb.AppendLine(
                    $"  {pool.Type,-10}  {pool.Current,8:F1} / {pool.Max,8:F1}" +
                    $"  {trend} {Math.Abs(pool.ProductionRate),6:F2}/s" +
                    $"  ({pool.Percentage * 100f:F0}%)");
            }
            return sb.ToString().TrimEnd();
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        /// <summary>Adds delta to pool, clamping to [0, max], firing event only if value actually changed.</summary>
        private void InternalAdd(ResourceType type, float delta)
        {
            if (Math.Abs(delta) < 0.00001f) return;

            var   pool   = Pools[type];
            float before = pool.Current;
            pool.Current = Math.Clamp(pool.Current + delta, 0f, pool.Max);
            Pools[type]  = pool;

            float actualDelta = pool.Current - before;
            if (Math.Abs(actualDelta) > 0.00001f)
                FireEvent(type, actualDelta, pool.Current);
        }

        private void WriteRate(ResourceType type, float rate)
        {
            var pool            = Pools[type];
            pool.ProductionRate = rate;
            Pools[type]         = pool;
        }

        private void FireEvent(ResourceType type, float delta, float newTotal)
        {
            var evt = new ResourceChangedEvent(type, delta, newTotal);
            OnResourceChanged?.Invoke(evt);
            EventBus.Global.Publish(evt);
        }

        /// <summary>Linear scan — O(n) acceptable for MaxUnitCount ≤ 50.</summary>
        private static UnitData FindUnit(IList<UnitData> units, string id)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Id == id) return units[i];
            return null;
        }
    }
}

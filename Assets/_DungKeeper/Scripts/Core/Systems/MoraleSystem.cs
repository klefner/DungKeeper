using System;
using System.Collections.Generic;

namespace DungKeeper
{
    // =========================================================================
    // MoraleReport
    // =========================================================================

    /// <summary>
    /// Snapshot of dungeon-wide morale health returned by
    /// <see cref="MoraleSystem.GetMoraleReport"/>.
    /// </summary>
    public sealed class MoraleReport
    {
        /// <summary>Mean morale across all living units (0-100).</summary>
        public float AverageMorale    { get; }

        /// <summary>Mean fear across all living units (0-100).</summary>
        public float AverageFear      { get; }

        /// <summary>Mean anger across all living units (0-100).</summary>
        public float AverageAnger     { get; }

        /// <summary>Mean loyalty across all living units (0-100).</summary>
        public float AverageLoyalty   { get; }

        /// <summary>
        /// Count of living units whose combined distress metrics put them at
        /// elevated risk of rebellion or walkout in the near future.
        /// </summary>
        public int AtRiskCount        { get; }

        /// <summary>Count of living units currently in <see cref="UnitState.Rebelling"/>.</summary>
        public int RebellingCount     { get; }

        /// <summary>Total number of living units evaluated in this report.</summary>
        public int TotalUnits         { get; }

        internal MoraleReport(
            float avgMorale, float avgFear, float avgAnger, float avgLoyalty,
            int atRisk, int rebelling, int total)
        {
            AverageMorale  = avgMorale;
            AverageFear    = avgFear;
            AverageAnger   = avgAnger;
            AverageLoyalty = avgLoyalty;
            AtRiskCount    = atRisk;
            RebellingCount = rebelling;
            TotalUnits     = total;
        }

        /// <summary>Returns an empty report for use when the dungeon has no living units.</summary>
        public static MoraleReport Empty { get; } = new MoraleReport(0f, 0f, 0f, 0f, 0, 0, 0);
    }

    // =========================================================================
    // MoraleSystem
    // =========================================================================

    /// <summary>
    /// Pure-C# simulation system that advances biological needs, emotional
    /// states, and loyalty for every unit each simulation tick, and performs
    /// probabilistic strike checks when anger spills over the rebellion
    /// threshold.
    ///
    /// This class is stateless with respect to individual units — all mutable
    /// state lives on <see cref="UnitData"/>.  The only internal state is the
    /// random-number generator, seeded at construction time.
    ///
    /// Intended usage:
    /// <code>
    ///   // Construction
    ///   var moraleSystem = new MoraleSystem(bus);
    ///
    ///   // Each frame
    ///   moraleSystem.Tick(dungeon.Units, Time.deltaTime, settings);
    ///
    ///   // UI refresh
    ///   MoraleReport report = moraleSystem.GetMoraleReport(dungeon.Units);
    /// </code>
    /// </summary>
    public sealed class MoraleSystem
    {
        // ------------------------------------------------------------------ //
        // Loyalty drift rates (per second)
        // ------------------------------------------------------------------ //

        private const float LoyaltyGoodWorkRate     =  0.01f;  // +/tick while working well
        private const float LoyaltyFearStressRate   = -0.02f;  // −/tick when fear > threshold
        private const float LoyaltyRecentSlapRate   = -0.05f;  // −/tick when recently slapped
        private const float LoyaltyDistressRate     = -0.01f;  // −/tick during hunger/fatigue distress

        // ------------------------------------------------------------------ //
        // Morale drift rates (per second)
        // ------------------------------------------------------------------ //

        private const float MoraleHungerPenalty     = -0.15f;
        private const float MoraleFatiguePenalty    = -0.10f;
        private const float MoraleFearPenalty       = -0.10f;

        // ------------------------------------------------------------------ //
        // Anger drift rates (per second, during distress)
        // ------------------------------------------------------------------ //

        private const float AngerHungerBoost        =  0.08f;
        private const float AngerFatigueBoost       =  0.06f;

        // ------------------------------------------------------------------ //
        // Fatigue productivity impact (advisory — applied by caller)
        // ------------------------------------------------------------------ //

        private const float FatigueDistressProductivityPenalty = -0.15f;

        // ------------------------------------------------------------------ //
        // Slap recency window
        // ------------------------------------------------------------------ //

        /// <summary>
        /// A unit is "recently slapped" if its <see cref="UnitData.LastSlappedTime"/>
        /// is within this many seconds of the current game time.
        /// </summary>
        private const float RecentSlapWindowSeconds = 30f;

        // ------------------------------------------------------------------ //
        // Strike probability (per-tick base, before loyalty scaling)
        // ------------------------------------------------------------------ //

        private const float BaseStrikeProbabilityPerTick  = 0.02f;
        private const float LowLoyaltyStrikeMultiplier    = 4.0f;
        private const float HighLoyaltyStrikeMultiplier   = 0.2f;

        // ------------------------------------------------------------------ //
        // At-risk thresholds for MoraleReport
        // ------------------------------------------------------------------ //

        private const float AtRiskAngerThreshold   = 65f;
        private const float AtRiskFearThreshold    = 70f;
        private const float AtRiskLoyaltyThreshold = 40f;

        // ------------------------------------------------------------------ //
        // Dependencies
        // ------------------------------------------------------------------ //

        private readonly EventBus _bus;
        private readonly Random   _rng;

        // ------------------------------------------------------------------ //
        // Optional external clock (same contract as SlapSystem)
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the current game time in seconds.  Used to determine
        /// whether a unit was "recently slapped".
        /// Defaults to <c>Environment.TickCount64 / 1000f</c>; override in
        /// tests for determinism.
        /// </summary>
        public Func<float> GameTimeClock { get; set; }
            = static () => Environment.TickCount64 / 1000f;

        // ------------------------------------------------------------------ //
        // Construction
        // ------------------------------------------------------------------ //

        /// <param name="bus">Event bus for strike and rebellion events.</param>
        /// <param name="seed">
        /// RNG seed.  Pass any non-negative integer for a deterministic run
        /// (useful in replay tests).  Pass -1 (default) for time-based seeding.
        /// </param>
        public MoraleSystem(EventBus bus, int seed = -1)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _rng = seed < 0 ? new Random() : new Random(seed);
        }

        // ------------------------------------------------------------------ //
        // Primary tick
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Advances the morale simulation for all living units by
        /// <paramref name="deltaTime"/> seconds.  Dead units are silently skipped.
        /// </summary>
        /// <param name="units">Current dungeon population snapshot.</param>
        /// <param name="deltaTime">Elapsed seconds since the last tick.</param>
        /// <param name="settings">Active game balance settings.</param>
        public void Tick(IEnumerable<UnitData> units, float deltaTime, GameSettings settings)
        {
            if (units    == null) throw new ArgumentNullException(nameof(units));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (deltaTime <= 0f)  return;

            float now = GameTimeClock();

            foreach (UnitData unit in units)
            {
                if (!unit.IsAlive || unit.CurrentState == UnitState.Dead)
                    continue;

                TickUnit(unit, deltaTime, settings, now);
            }
        }

        // ------------------------------------------------------------------ //
        // Per-unit tick
        // ------------------------------------------------------------------ //

        private void TickUnit(UnitData unit, float deltaTime, GameSettings settings, float now)
        {
            // ----------------------------------------------------------------
            // Biological needs
            // ----------------------------------------------------------------

            // Hunger accumulates unless eating
            if (unit.CurrentState != UnitState.Eating)
                unit.Hunger = Clamp(unit.Hunger + settings.HungerRate * deltaTime);
            else
                unit.Hunger = Clamp(unit.Hunger - settings.EatRecoveryRate * deltaTime);

            // Fatigue accumulates only during physical activity; recovers while resting
            bool isPhysicallyActive = unit.CurrentState == UnitState.Working
                                   || unit.CurrentState == UnitState.Fighting;

            float effectiveFatigueRate = unit.FatigueRate_Override > 0f
                ? unit.FatigueRate_Override
                : settings.FatigueRate;

            if (isPhysicallyActive)
                unit.Fatigue = Clamp(unit.Fatigue + effectiveFatigueRate * deltaTime);
            else if (unit.CurrentState == UnitState.Resting)
                unit.Fatigue = Clamp(unit.Fatigue - settings.RestRecoveryRate * deltaTime);

            // ----------------------------------------------------------------
            // Distress flags (evaluated once and reused)
            // ----------------------------------------------------------------
            bool hungerDistress  = unit.Hunger  > settings.HungerDistressThreshold;
            bool fatigueDistress = unit.Fatigue > settings.FatigueDistressThreshold;
            bool fearStress      = unit.Fear    > settings.FearStressThreshold;
            bool inDistress      = hungerDistress || fatigueDistress || fearStress;

            // ----------------------------------------------------------------
            // Hunger distress → morale loss + anger creep
            // ----------------------------------------------------------------
            if (hungerDistress)
            {
                unit.Morale = Clamp(unit.Morale + MoraleHungerPenalty  * deltaTime);
                unit.Anger  = Clamp(unit.Anger  + AngerHungerBoost     * deltaTime);
            }

            // ----------------------------------------------------------------
            // Fatigue distress → morale loss + anger creep
            // ----------------------------------------------------------------
            if (fatigueDistress)
            {
                unit.Morale = Clamp(unit.Morale + MoraleFatiguePenalty * deltaTime);
                unit.Anger  = Clamp(unit.Anger  + AngerFatigueBoost    * deltaTime);

                // Productivity penalty flag (advisory; caller reads this)
                unit.Productivity = Math.Max(
                    0f,
                    unit.Productivity + FatigueDistressProductivityPenalty * deltaTime);
            }

            // ----------------------------------------------------------------
            // Fear stress → loyalty bleed + morale damage
            // ----------------------------------------------------------------
            if (fearStress)
            {
                unit.Loyalty = Clamp(unit.Loyalty + LoyaltyFearStressRate * deltaTime);
                unit.Morale  = Clamp(unit.Morale  + MoraleFearPenalty     * deltaTime);
            }

            // ----------------------------------------------------------------
            // Fear and anger decay
            // ----------------------------------------------------------------
            unit.Fear = Clamp(unit.Fear - settings.FearDecayRate * deltaTime);

            // Anger decays only when the unit is not under active distress
            if (!inDistress)
                unit.Anger = Clamp(unit.Anger - settings.AngerDecayRate * deltaTime);

            // ----------------------------------------------------------------
            // Morale recovery when conditions are good
            // ----------------------------------------------------------------
            bool goodConditions = !inDistress
                                  && unit.CurrentState != UnitState.Rebelling
                                  && unit.CurrentState != UnitState.Imprisoned;
            if (goodConditions)
                unit.Morale = Clamp(unit.Morale + settings.MoraleRecoveryRate * deltaTime);

            // ----------------------------------------------------------------
            // Loyalty drift
            // ----------------------------------------------------------------
            TickLoyalty(unit, deltaTime, settings, hungerDistress, fatigueDistress, fearStress, now);

            // ----------------------------------------------------------------
            // SlapAccumulation natural decay
            // ----------------------------------------------------------------
            unit.CurrentSlapAccumulation = Math.Max(
                0f,
                unit.CurrentSlapAccumulation - settings.SlapAccumulationDecay * deltaTime);

            // ----------------------------------------------------------------
            // Rebellion / strike check
            // ----------------------------------------------------------------
            if (unit.Anger >= settings.AngerRebellionThreshold
                && unit.CurrentState != UnitState.Rebelling)
            {
                TryTriggerStrike(unit, deltaTime, settings);
            }
        }

        // ------------------------------------------------------------------ //
        // Loyalty drift helper
        // ------------------------------------------------------------------ //

        private static void TickLoyalty(
            UnitData unit, float deltaTime, GameSettings settings,
            bool hungerDistress, bool fatigueDistress, bool fearStress,
            float now)
        {
            // Working in acceptable conditions slowly builds loyalty
            bool workingComfortably = unit.CurrentState == UnitState.Working
                                      && !hungerDistress
                                      && !fatigueDistress
                                      && !fearStress;
            if (workingComfortably)
                unit.Loyalty = Clamp(unit.Loyalty + LoyaltyGoodWorkRate * deltaTime);

            // Fear stress bleeds loyalty (stacks with the morale-system penalty above)
            if (fearStress)
                unit.Loyalty = Clamp(unit.Loyalty + LoyaltyFearStressRate * deltaTime);

            // Recently slapped? Loyalty erodes faster
            bool recentlySlapped = unit.LastSlappedTime > 0f
                                   && (now - unit.LastSlappedTime) < RecentSlapWindowSeconds;
            if (recentlySlapped)
                unit.Loyalty = Clamp(unit.Loyalty + LoyaltyRecentSlapRate * deltaTime);

            // Hunger or fatigue distress chips at loyalty
            if (hungerDistress || fatigueDistress)
                unit.Loyalty = Clamp(unit.Loyalty + LoyaltyDistressRate * deltaTime);
        }

        // ------------------------------------------------------------------ //
        // Strike check
        // ------------------------------------------------------------------ //

        private void TryTriggerStrike(UnitData unit, float deltaTime, GameSettings settings)
        {
            // Scale per-second probability to per-tick
            float probability = BaseStrikeProbabilityPerTick * deltaTime;

            if (unit.Loyalty < settings.AngerRebellionThreshold / 3f)      // low loyalty (< 30)
                probability *= LowLoyaltyStrikeMultiplier;
            else if (unit.Loyalty > settings.AngerRebellionThreshold * 2f / 3f) // high loyalty (> 60)
                probability *= HighLoyaltyStrikeMultiplier;

            if (_rng.NextDouble() > probability)
                return; // no strike this tick

            StrikeType strike = DetermineStrikeType(unit);
            ApplyStrikeStateEffect(unit, strike);

            _bus.Publish(new UnitRebellionStartedEvent(unit, strike));
        }

        private static StrikeType DetermineStrikeType(UnitData unit)
        {
            float loyalty = unit.Loyalty;
            float roll    = DeterministicRoll(unit);

            if (loyalty < 30f)
            {
                // Low loyalty — escalation to walkout or full rebellion is likely
                if (roll < 0.20f) return StrikeType.SlowDown;
                if (roll < 0.45f) return StrikeType.Walkout;
                if (roll < 0.70f) return StrikeType.Sabotage;
                if (roll < 0.85f) return StrikeType.FightEachOther;
                return StrikeType.FullRebellion;
            }
            else if (loyalty < 60f)
            {
                // Mid loyalty — passive resistance or walkout
                if (roll < 0.50f) return StrikeType.SlowDown;
                if (roll < 0.80f) return StrikeType.Walkout;
                return StrikeType.Sabotage;
            }
            else
            {
                // High loyalty — only passive resistance, even at max anger
                return StrikeType.SlowDown;
            }
        }

        /// <summary>
        /// Deterministic roll based on stable unit state to avoid RNG bias
        /// when units hover near the anger threshold across many ticks.
        /// Uses only integer arithmetic.
        /// </summary>
        private static float DeterministicRoll(UnitData unit)
        {
            unchecked
            {
                int hash = (unit.Id?.GetHashCode() ?? 0)
                           ^ (unit.SlapCount * 2654435761)
                           ^ ((int)unit.Anger);
                return Math.Abs(hash % 1000) / 1000f;
            }
        }

        private static void ApplyStrikeStateEffect(UnitData unit, StrikeType strike)
        {
            switch (strike)
            {
                case StrikeType.SlowDown:
                    // Slow-down is passive; state stays as-is, productivity
                    // penalty is applied by the caller reading unit.Productivity.
                    // Reduce productivity slightly to signal the slow-down.
                    unit.Productivity = Math.Max(0.1f, unit.Productivity - 0.10f);
                    break;

                case StrikeType.Walkout:
                    // Unit abandons its room
                    unit.AssignedRoomId = null;
                    unit.CurrentTask    = TaskType.None;
                    unit.CurrentState   = UnitState.Rebelling;
                    break;

                case StrikeType.Sabotage:
                case StrikeType.FightEachOther:
                case StrikeType.FullRebellion:
                    unit.CurrentState = UnitState.Rebelling;
                    break;
            }
        }

        // ------------------------------------------------------------------ //
        // Report
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns a dungeon-wide morale snapshot.  Only living, non-dead units
        /// are included.  Returns <see cref="MoraleReport.Empty"/> if the
        /// collection is null or contains no qualifying units.
        /// </summary>
        public MoraleReport GetMoraleReport(IEnumerable<UnitData> units)
        {
            if (units == null) return MoraleReport.Empty;

            float totalMorale  = 0f;
            float totalFear    = 0f;
            float totalAnger   = 0f;
            float totalLoyalty = 0f;
            int   atRisk       = 0;
            int   rebelling    = 0;
            int   count        = 0;

            foreach (UnitData unit in units)
            {
                if (!unit.IsAlive || unit.CurrentState == UnitState.Dead)
                    continue;

                count++;
                totalMorale  += unit.Morale;
                totalFear    += unit.Fear;
                totalAnger   += unit.Anger;
                totalLoyalty += unit.Loyalty;

                if (unit.CurrentState == UnitState.Rebelling)
                    rebelling++;

                // At-risk: two or more distress indicators simultaneously
                bool highAnger   = unit.Anger   > AtRiskAngerThreshold;
                bool highFear    = unit.Fear    > AtRiskFearThreshold;
                bool lowLoyalty  = unit.Loyalty < AtRiskLoyaltyThreshold;
                int  riskFactors = (highAnger ? 1 : 0) + (highFear ? 1 : 0) + (lowLoyalty ? 1 : 0);
                if (riskFactors >= 2)
                    atRisk++;
            }

            if (count == 0) return MoraleReport.Empty;

            return new MoraleReport(
                totalMorale  / count,
                totalFear    / count,
                totalAnger   / count,
                totalLoyalty / count,
                atRisk,
                rebelling,
                count);
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private static float Clamp(float value) => Math.Max(0f, Math.Min(100f, value));
    }
}

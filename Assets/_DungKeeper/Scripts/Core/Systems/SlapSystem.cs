using System;
using System.Collections.Generic;

namespace DungKeeper
{
    // =========================================================================
    // Result types
    // =========================================================================

    /// <summary>
    /// Full result of a single slap interaction, returned by
    /// <see cref="SlapSystem.ProcessSlap"/>.
    /// </summary>
    public sealed class SlapResult
    {
        /// <summary>Discrete behavioural response the unit settled on.</summary>
        public SlapResponse Response           { get; init; }

        /// <summary>Unit's fear value after the slap is processed.</summary>
        public float NewFear                  { get; init; }

        /// <summary>Unit's anger value after the slap is processed.</summary>
        public float NewAnger                 { get; init; }

        /// <summary>Unit's loyalty value after the slap is processed.</summary>
        public float NewLoyalty               { get; init; }

        /// <summary>
        /// Signed productivity multiplier delta applied immediately.
        /// Positive means faster work; negative means slowdown / disruption.
        /// </summary>
        public float ProductivityModifier     { get; init; }

        /// <summary>How long (seconds) the productivity modifier persists.</summary>
        public float Duration                 { get; init; }

        /// <summary>Human-readable UI message describing the outcome.</summary>
        public string FeedbackMessage         { get; init; }
    }

    // =========================================================================
    // Events
    // =========================================================================

    /// <summary>Raised every time a unit is slapped, regardless of outcome.</summary>
    public sealed class UnitSlappedEvent
    {
        public UnitData Unit       { get; init; }
        public float    SlapForce  { get; init; }
        public SlapResult Result   { get; init; }
    }

    /// <summary>Raised when a slap causes a unit to die (Quit response).</summary>
    public sealed class UnitDiedEvent
    {
        public UnitData Unit   { get; init; }
        public string   Reason { get; init; }
    }

    /// <summary>Raised when a unit transitions into the Rebelling state.</summary>
    public sealed class UnitRebellionStartedEvent
    {
        public UnitData Unit { get; init; }
    }

    // =========================================================================
    // SlapSystem
    // =========================================================================

    /// <summary>
    /// Central, pure-C# slap simulation system.  Contains no Unity runtime
    /// dependencies so it can be exercised in Edit-Mode unit tests.
    ///
    /// Call order per slap:
    ///   1. Cooldown / streak check
    ///   2. Force adjustment via SlapAccumulation / SlapTolerance ratio
    ///   3. Base response from UnitData.EvaluateSlapResponse
    ///   4. Personality modifiers
    ///   5. State-context modifiers
    ///   6. Threshold overrides (Rebel / Quit)
    ///   7. Stat write-back on UnitData
    ///   8. Event dispatch
    ///   9. SlapResult construction
    /// </summary>
    public sealed class SlapSystem
    {
        // ------------------------------------------------------------------ //
        // Tunables (could be promoted to GameSettings if desired)
        // ------------------------------------------------------------------ //

        private const float StreakWindowSeconds    = 10f;   // window for streak detection
        private const int   StreakThreshold        = 3;     // slaps inside window = streak
        private const float StreakDiminishFactor   = 0.5f;  // force multiplier during streak
        private const float CooldownDiminishFactor = 0.4f;  // force multiplier when on cooldown

        // Accumulation fraction at which rebellion override kicks in
        private const float RebellionAccumFraction = 0.8f;

        // Personality-specific constants
        private const float CowardlyFearBonus      = 15f;
        private const float AggressiveAngerBonus   = 20f;
        private const float MasochisticFearDrain   = 10f;  // fear → excitement drain
        private const float LoyalToleranceBonus    = 0.2f; // ratio bonus before fear
        private const float MercenaryQuitThreshold = 30f;  // gold threshold
        private const float LazyIgnoreThreshold    = 0.4f; // accumFraction at which lazy ignores

        // State-context constants
        private const float WorkingFearBonus       = 5f;
        private const float RestingAngerBonus      = 12f;
        private const float FightingAngerBonus     = 15f;
        private const float EatingAngerBonus       = 10f;
        private const float EatingHungerReset      = 0.5f;  // fraction of current hunger removed
        private const float RebellionEscalateForce = 0.6f;  // threshold where slap reduces rebellion
        private const float ImprisonedDespairFear  = 8f;

        // Productivity modifier magnitudes (signed)
        private const float SpeedUpBonus          =  0.25f;
        private const float ExcitedBonus          =  0.35f;
        private const float AngryPenalty          = -0.20f;
        private const float IgnorePenalty         = -0.05f;
        private const float QuitPenalty           = -1.00f;
        private const float RebelPenalty          = -0.40f;

        // Effect durations (seconds)
        private const float SpeedUpDuration       = 15f;
        private const float ExcitedDuration       = 20f;
        private const float AngryDuration         = 25f;
        private const float IgnoreDuration        =  5f;
        private const float RebellionDuration     = 30f; // overridden by GameSettings

        // ------------------------------------------------------------------ //
        // Streak tracking — keyed by unit ID to avoid cross-unit interference
        // ------------------------------------------------------------------ //

        private readonly Dictionary<string, Queue<float>> _slapTimestampsByUnit
            = new Dictionary<string, Queue<float>>(StringComparer.Ordinal);

        // ------------------------------------------------------------------ //
        // Public event hooks
        // ------------------------------------------------------------------ //

        /// <summary>Fired after every completed slap interaction.</summary>
        public event Action<UnitSlappedEvent>           OnUnitSlapped;

        /// <summary>Fired when a Quit response kills the unit.</summary>
        public event Action<UnitDiedEvent>              OnUnitDied;

        /// <summary>Fired when a Rebel response is finalised.</summary>
        public event Action<UnitRebellionStartedEvent>  OnUnitRebellionStarted;

        // ------------------------------------------------------------------ //
        // Primary API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Processes one slap against <paramref name="unit"/> and returns the
        /// full outcome.  All stat mutations are applied to <paramref name="unit"/>
        /// before returning; events are fired in the order listed above.
        /// </summary>
        /// <param name="unit">The unit being slapped.  Must not be null.</param>
        /// <param name="slapForce">Raw force value supplied by the player/AI (0–100 typical range).</param>
        /// <param name="settings">Active game settings; must not be null.</param>
        public SlapResult ProcessSlap(UnitData unit, float slapForce, GameSettings settings)
        {
            if (unit == null)    throw new ArgumentNullException(nameof(unit));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            float now = unit.GameTime; // monotonic game clock carried by UnitData

            // ----------------------------------------------------------------
            // 1. Cooldown check — too recent?
            // ----------------------------------------------------------------
            bool onCooldown = (now - unit.LastSlappedTime) < settings.SlapCooldownSeconds;
            float adjustedForce = slapForce;

            if (onCooldown)
                adjustedForce *= CooldownDiminishFactor;

            // ----------------------------------------------------------------
            // 2. Streak detection — 3+ slaps within StreakWindowSeconds
            // ----------------------------------------------------------------
            if (!_slapTimestampsByUnit.TryGetValue(unit.Id, out var timestamps))
            {
                timestamps = new Queue<float>();
                _slapTimestampsByUnit[unit.Id] = timestamps;
            }

            // Prune timestamps outside the window
            while (timestamps.Count > 0 && (now - timestamps.Peek()) > StreakWindowSeconds)
                timestamps.Dequeue();

            bool inStreak = timestamps.Count >= StreakThreshold;
            if (inStreak)
                adjustedForce *= StreakDiminishFactor;

            timestamps.Enqueue(now);

            // ----------------------------------------------------------------
            // 3. SlapAccumulation / SlapTolerance ratio adjustment
            // ----------------------------------------------------------------
            float tolerance      = Math.Max(1f, unit.SlapTolerance);
            float accumRatio     = unit.CurrentSlapAccumulation / tolerance; // 0–1+
            float toleranceFactor = Math.Max(0.1f, 1f - accumRatio * 0.6f); // diminishes as ratio rises
            adjustedForce *= toleranceFactor;

            // ----------------------------------------------------------------
            // 4. Base response from unit
            // ----------------------------------------------------------------
            SlapResponse response = unit.EvaluateSlapResponse(adjustedForce);

            float fearDelta    = 0f;
            float angerDelta   = 0f;
            float loyaltyDelta = 0f;

            // Set base stat deltas from raw response
            switch (response)
            {
                case SlapResponse.SpeedUp:
                    fearDelta    =  10f;
                    loyaltyDelta = -2f;
                    break;
                case SlapResponse.BecomeExcited:
                    fearDelta    =  -5f;
                    angerDelta   =   5f;
                    break;
                case SlapResponse.BecomeAngry:
                    angerDelta   =  20f;
                    fearDelta    =   5f;
                    loyaltyDelta =  -5f;
                    break;
                case SlapResponse.Ignore:
                    angerDelta   =   3f;
                    break;
                case SlapResponse.Quit:
                    fearDelta    =  30f;
                    loyaltyDelta = -100f;
                    break;
                case SlapResponse.Rebel:
                    angerDelta   =  25f;
                    fearDelta    =  10f;
                    loyaltyDelta = -10f;
                    break;
            }

            // ----------------------------------------------------------------
            // 5. Personality modifiers
            // ----------------------------------------------------------------
            response = ApplyPersonalityModifiers(
                unit, adjustedForce, settings, response,
                ref fearDelta, ref angerDelta, ref loyaltyDelta);

            // ----------------------------------------------------------------
            // 6. State-context modifiers
            // ----------------------------------------------------------------
            response = ApplyStateContextModifiers(
                unit, adjustedForce, response,
                ref fearDelta, ref angerDelta, ref loyaltyDelta);

            // ----------------------------------------------------------------
            // 7. Accumulation threshold override
            // ----------------------------------------------------------------
            unit.CurrentSlapAccumulation += adjustedForce * 0.5f;

            if (unit.CurrentSlapAccumulation > unit.SlapTolerance * RebellionAccumFraction
                && response != SlapResponse.Quit)
            {
                response = SlapResponse.Rebel;
            }

            // ----------------------------------------------------------------
            // 8. Finalise stat values (clamped 0–100)
            // ----------------------------------------------------------------
            float newFear    = Clamp01_100(unit.Fear    + fearDelta);
            float newAnger   = Clamp01_100(unit.Anger   + angerDelta);
            float newLoyalty = Clamp01_100(unit.Loyalty + loyaltyDelta);

            unit.Fear    = newFear;
            unit.Anger   = newAnger;
            unit.Loyalty = newLoyalty;
            unit.SlapCount++;
            unit.LastSlappedTime = now;

            // ----------------------------------------------------------------
            // 9. Build productivity modifier and duration
            // ----------------------------------------------------------------
            float productivityMod = ResponseToProductivityModifier(response);
            float duration        = ResponseToDuration(response, settings);

            // ----------------------------------------------------------------
            // 10. Feedback message
            // ----------------------------------------------------------------
            string message = BuildFeedbackMessage(unit, response, adjustedForce, onCooldown, inStreak);

            // ----------------------------------------------------------------
            // 11. Post-response side effects & events
            // ----------------------------------------------------------------
            if (response == SlapResponse.Quit)
            {
                unit.IsAlive       = false;
                unit.CurrentState  = UnitState.Dead;

                OnUnitDied?.Invoke(new UnitDiedEvent
                {
                    Unit   = unit,
                    Reason = "Slapped to the point of no return"
                });
            }
            else if (response == SlapResponse.Rebel
                     && unit.CurrentState != UnitState.Rebelling)
            {
                unit.CurrentState = UnitState.Rebelling;
                OnUnitRebellionStarted?.Invoke(new UnitRebellionStartedEvent { Unit = unit });
            }

            var result = new SlapResult
            {
                Response            = response,
                NewFear             = newFear,
                NewAnger            = newAnger,
                NewLoyalty          = newLoyalty,
                ProductivityModifier = productivityMod,
                Duration            = duration,
                FeedbackMessage     = message
            };

            OnUnitSlapped?.Invoke(new UnitSlappedEvent
            {
                Unit      = unit,
                SlapForce = slapForce,
                Result    = result
            });

            return result;
        }

        // ------------------------------------------------------------------ //
        // Personality modifier application
        // ------------------------------------------------------------------ //

        private SlapResponse ApplyPersonalityModifiers(
            UnitData unit, float adjustedForce, GameSettings settings,
            SlapResponse response,
            ref float fearDelta, ref float angerDelta, ref float loyaltyDelta)
        {
            switch (unit.Personality)
            {
                case PersonalityTrait.Cowardly:
                    // Amplified fear; always leans toward SpeedUp
                    fearDelta += CowardlyFearBonus;
                    if (response == SlapResponse.BecomeAngry || response == SlapResponse.Ignore)
                        response = SlapResponse.SpeedUp;
                    break;

                case PersonalityTrait.Aggressive:
                    // Extra anger; can flip SpeedUp into BecomeAngry
                    angerDelta += AggressiveAngerBonus;
                    if (response == SlapResponse.SpeedUp && unit.Anger + angerDelta > 50f)
                        response = SlapResponse.BecomeAngry;
                    break;

                case PersonalityTrait.Masochistic:
                    // Fear converts to excitement; BecomeExcited is likely
                    fearDelta   -= MasochisticFearDrain;
                    angerDelta  -= 5f;
                    loyaltyDelta += 2f;
                    if (response != SlapResponse.Quit && response != SlapResponse.Rebel)
                        response = SlapResponse.BecomeExcited;
                    break;

                case PersonalityTrait.Loyal:
                    // Higher effective tolerance; more likely SpeedUp
                    loyaltyDelta += 3f;
                    fearDelta    -= 5f;
                    angerDelta   -= 5f;
                    if (response == SlapResponse.BecomeAngry)
                        response = SlapResponse.SpeedUp;
                    break;

                case PersonalityTrait.Mercenary:
                {
                    // If pay is poor, quit chance scales with force
                    if (unit.CurrentGoldPerTick < MercenaryQuitThreshold)
                    {
                        float quitChance = (1f - unit.CurrentGoldPerTick / MercenaryQuitThreshold)
                                           * (adjustedForce / 100f);
                        if (quitChance > 0.6f)
                        {
                            response     = SlapResponse.Quit;
                            loyaltyDelta = -100f;
                        }
                        else
                        {
                            angerDelta  += 10f;
                            loyaltyDelta -= 8f;
                        }
                    }
                    else
                    {
                        // Well-paid — grudgingly accepts slap
                        angerDelta  += 5f;
                        loyaltyDelta -= 2f;
                    }
                    break;
                }

                case PersonalityTrait.Lazy:
                {
                    float accumRatio = unit.CurrentSlapAccumulation / Math.Max(1f, unit.SlapTolerance);
                    if (accumRatio < LazyIgnoreThreshold)
                    {
                        // Shrugs it off
                        response    = SlapResponse.Ignore;
                        fearDelta   = 0f;
                        angerDelta  = 0f;
                    }
                    else
                    {
                        // Had enough — quits rather than works
                        response     = SlapResponse.Quit;
                        loyaltyDelta = -100f;
                    }
                    break;
                }
            }

            return response;
        }

        // ------------------------------------------------------------------ //
        // State-context modifier application
        // ------------------------------------------------------------------ //

        private SlapResponse ApplyStateContextModifiers(
            UnitData unit, float adjustedForce, SlapResponse response,
            ref float fearDelta, ref float angerDelta, ref float loyaltyDelta)
        {
            switch (unit.CurrentState)
            {
                case UnitState.Working:
                    if (response == SlapResponse.SpeedUp)
                        fearDelta += WorkingFearBonus;
                    break;

                case UnitState.Resting:
                    angerDelta   += RestingAngerBonus;
                    loyaltyDelta -= 3f;
                    if (response == SlapResponse.SpeedUp)
                        response = SlapResponse.BecomeAngry;
                    break;

                case UnitState.Fighting:
                    angerDelta += FightingAngerBonus;
                    // Aggressive response dominates
                    if (response == SlapResponse.SpeedUp || response == SlapResponse.Ignore)
                        response = SlapResponse.BecomeAngry;
                    break;

                case UnitState.Eating:
                    angerDelta       += EatingAngerBonus;
                    unit.Hunger      -= unit.Hunger * EatingHungerReset;
                    loyaltyDelta     -= 4f;
                    if (response == SlapResponse.SpeedUp)
                        response = SlapResponse.BecomeAngry;
                    break;

                case UnitState.Rebelling:
                    if (adjustedForce > RebellionEscalateForce * 100f)
                    {
                        // Overwhelming force intimidates the rebel back
                        response    = SlapResponse.SpeedUp;
                        fearDelta  += 20f;
                        angerDelta -= 10f;
                    }
                    else
                    {
                        // Slapping a rebel just makes things worse
                        angerDelta  += 15f;
                        fearDelta   += 5f;
                        loyaltyDelta -= 8f;
                        response     = SlapResponse.Rebel;
                    }
                    break;

                case UnitState.Imprisoned:
                    fearDelta    += ImprisonedDespairFear;
                    loyaltyDelta -= 5f;
                    angerDelta   -= 5f; // resignation rather than anger
                    if (response == SlapResponse.BecomeAngry)
                        response = SlapResponse.Ignore; // too broken to fight back
                    break;

                case UnitState.Idle:
                case UnitState.Fleeing:
                case UnitState.Impressed:
                case UnitState.Dead:
                    // No additional context modifications
                    break;
            }

            return response;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        private static float Clamp01_100(float value)
            => Math.Max(0f, Math.Min(100f, value));

        private static float ResponseToProductivityModifier(SlapResponse response)
        {
            return response switch
            {
                SlapResponse.SpeedUp       => SpeedUpBonus,
                SlapResponse.BecomeExcited => ExcitedBonus,
                SlapResponse.BecomeAngry   => AngryPenalty,
                SlapResponse.Ignore        => IgnorePenalty,
                SlapResponse.Quit          => QuitPenalty,
                SlapResponse.Rebel         => RebelPenalty,
                _                          => 0f
            };
        }

        private static float ResponseToDuration(SlapResponse response, GameSettings settings)
        {
            return response switch
            {
                SlapResponse.SpeedUp       => SpeedUpDuration,
                SlapResponse.BecomeExcited => ExcitedDuration,
                SlapResponse.BecomeAngry   => AngryDuration,
                SlapResponse.Ignore        => IgnoreDuration,
                SlapResponse.Quit          => 0f,
                SlapResponse.Rebel         => settings.RebellionBaseDuration,
                _                          => 0f
            };
        }

        private static string BuildFeedbackMessage(
            UnitData unit, SlapResponse response,
            float adjustedForce, bool onCooldown, bool inStreak)
        {
            string name = unit.DisplayName;

            string prefix = onCooldown ? "[Cooldown] "
                          : inStreak   ? "[Diminishing] "
                          : string.Empty;

            string core = response switch
            {
                SlapResponse.SpeedUp =>
                    $"{name} yelps and scrambles back to work!",
                SlapResponse.BecomeExcited =>
                    $"{name} grins unsettlingly and works faster.",
                SlapResponse.BecomeAngry =>
                    $"{name} snarls — that was a mistake.",
                SlapResponse.Ignore =>
                    $"{name} barely notices the slap.",
                SlapResponse.Quit =>
                    $"{name} has had enough and leaves — forever.",
                SlapResponse.Rebel =>
                    $"{name} snaps and turns against you!",
                _ =>
                    $"{name} reacts to the slap."
            };

            string suffix = adjustedForce < 20f
                ? " (weak slap)"
                : adjustedForce > 75f
                    ? " (brutal!)"
                    : string.Empty;

            return prefix + core + suffix;
        }
    }
}

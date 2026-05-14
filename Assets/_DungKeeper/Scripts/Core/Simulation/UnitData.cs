using System;

namespace DungKeeper
{
    /// <summary>
    /// Pure C# data model representing a single dungeon unit.
    /// Contains no Unity dependencies so it can be instantiated and tested
    /// outside of a Play Mode context.
    /// </summary>
    [Serializable]
    public sealed class UnitData
    {
        // =====================================================================
        // Identity
        // =====================================================================

        /// <summary>Stable unique identifier (GUID string).</summary>
        public string Id { get; }

        /// <summary>Display name shown in the dungeon UI.</summary>
        public string DisplayName { get; set; }

        /// <summary>Functional role that determines which tasks this unit can perform.</summary>
        public UnitRole Role { get; }

        /// <summary>Personality archetype that shapes reactions to stimuli.</summary>
        public PersonalityTrait Personality { get; }

        // =====================================================================
        // Health
        // =====================================================================

        public float MaxHealth     { get; set; }
        public float CurrentHealth { get; set; }

        // =====================================================================
        // Psychological stats  (all 0-100 unless noted)
        // =====================================================================

        /// <summary>
        /// High fear accelerates work. Above <see cref="GameConstants.FearStressThreshold"/>
        /// the unit becomes stressed and may flee or rebel.
        /// </summary>
        public float Fear { get; set; }

        /// <summary>
        /// High loyalty increases slap tolerance and rebellion resistance.
        /// Low loyalty raises the risk of organised strikes.
        /// </summary>
        public float Loyalty { get; set; }

        /// <summary>
        /// Accumulated anger. Above <see cref="GameConstants.AngerRebellionThreshold"/>
        /// the unit will transition into a rebellion state.
        /// </summary>
        public float Anger { get; set; }

        /// <summary>
        /// General motivation. High morale grants a productivity bonus;
        /// low morale imposes a penalty.
        /// </summary>
        public float Morale { get; set; }

        // =====================================================================
        // Work / combat stats
        // =====================================================================

        /// <summary>Base work speed before situational modifiers are applied (0-100).</summary>
        public float Productivity { get; set; }

        /// <summary>Damage threshold before the unit flees or surrenders (0-100).</summary>
        public float PainTolerance { get; set; }

        /// <summary>
        /// Maximum accumulated slap force before reactions escalate.
        /// Derived from personality and role at construction; can be upgraded.
        /// </summary>
        public float SlapTolerance { get; set; }

        /// <summary>
        /// Running total of slap force applied since the last reset cycle.
        /// Compared against <see cref="SlapTolerance"/> to determine response severity.
        /// </summary>
        public float CurrentSlapAccumulation { get; set; }

        // =====================================================================
        // Biological needs
        // =====================================================================

        /// <summary>Rises over time. Above 70 the unit will seek food instead of working.</summary>
        public float Hunger { get; set; }

        /// <summary>Rises over time. Above 80 the unit must rest before resuming work.</summary>
        public float Fatigue { get; set; }

        // =====================================================================
        // Combat
        // =====================================================================

        /// <summary>Raw combat effectiveness used in fight resolution (0-100).</summary>
        public float CombatAbility { get; set; }

        // =====================================================================
        // State tracking
        // =====================================================================

        public UnitState CurrentState  { get; set; }
        public TaskType  CurrentTask   { get; set; }

        /// <summary>ID of the <see cref="RoomData"/> this unit is currently assigned to, or null.</summary>
        public string AssignedRoomId { get; set; }

        /// <summary>Game time (seconds) at which the unit was last slapped.</summary>
        public float LastSlappedTime { get; set; }

        /// <summary>Lifetime total number of slaps received.</summary>
        public int SlapCount   { get; set; }

        /// <summary>Lifetime total number of rebellion episodes initiated.</summary>
        public int RebelCount  { get; set; }

        public bool IsImprisoned { get; set; }
        public bool IsAlive      { get; private set; }

        // =====================================================================
        // Constructor
        // =====================================================================

        /// <summary>
        /// Creates a new unit with all stats initialised according to the
        /// specified <paramref name="role"/> and <paramref name="personality"/> combination.
        /// </summary>
        public UnitData(string name, UnitRole role, PersonalityTrait personality)
        {
            Id          = Guid.NewGuid().ToString();
            DisplayName = name ?? throw new ArgumentNullException(nameof(name));
            Role        = role;
            Personality = personality;
            IsAlive     = true;
            CurrentState = UnitState.Idle;
            CurrentTask  = TaskType.None;

            // -----------------------------------------------------------------
            // Role-based base stats
            // -----------------------------------------------------------------
            switch (role)
            {
                case UnitRole.Worker:
                    MaxHealth     = 60f;
                    Productivity  = 70f;
                    CombatAbility = 20f;
                    PainTolerance = 40f;
                    SlapTolerance = GameConstants.BaseSlapTolerance + 5f;
                    break;

                case UnitRole.Guard:
                    MaxHealth     = 100f;
                    Productivity  = 40f;
                    CombatAbility = 75f;
                    PainTolerance = 70f;
                    SlapTolerance = GameConstants.BaseSlapTolerance + 10f;
                    break;

                case UnitRole.Researcher:
                    MaxHealth     = 55f;
                    Productivity  = 80f;
                    CombatAbility = 15f;
                    PainTolerance = 30f;
                    SlapTolerance = GameConstants.BaseSlapTolerance;
                    break;

                case UnitRole.Torturer:
                    MaxHealth     = 75f;
                    Productivity  = 60f;
                    CombatAbility = 55f;
                    PainTolerance = 65f;
                    SlapTolerance = GameConstants.BaseSlapTolerance + 8f;
                    break;

                case UnitRole.Summoner:
                    MaxHealth     = 65f;
                    Productivity  = 65f;
                    CombatAbility = 35f;
                    PainTolerance = 35f;
                    SlapTolerance = GameConstants.BaseSlapTolerance + 3f;
                    break;

                default:
                    MaxHealth     = 60f;
                    Productivity  = 60f;
                    CombatAbility = 30f;
                    PainTolerance = 40f;
                    SlapTolerance = GameConstants.BaseSlapTolerance;
                    break;
            }

            CurrentHealth = MaxHealth;

            // -----------------------------------------------------------------
            // Personality modifiers applied on top of role base stats
            // -----------------------------------------------------------------
            ApplyPersonalityDefaults(personality);
        }

        // =====================================================================
        // Private initialisation helpers
        // =====================================================================

        private void ApplyPersonalityDefaults(PersonalityTrait personality)
        {
            // Shared starting point before personality tweaks
            Fear    = 30f;
            Loyalty = 50f;
            Anger   = 0f;
            Morale  = 60f;
            Hunger  = 0f;
            Fatigue = 0f;

            switch (personality)
            {
                case PersonalityTrait.Cowardly:
                    Fear          += 20f;   // starts more fearful
                    Loyalty       -= 10f;   // unreliable under pressure
                    PainTolerance -= 15f;   // flees earlier
                    CombatAbility -= 10f;
                    SlapTolerance -= 3f;    // breaks sooner
                    break;

                case PersonalityTrait.Aggressive:
                    Anger         += 15f;   // already simmering
                    CombatAbility += 20f;
                    PainTolerance += 10f;
                    Morale        += 10f;
                    SlapTolerance += 5f;    // dishes it out, can take it
                    break;

                case PersonalityTrait.Masochistic:
                    PainTolerance  += 30f;
                    SlapTolerance  += 15f;  // actually enjoys slaps
                    Fear           -= 10f;
                    Morale         += 5f;   // oddly content
                    break;

                case PersonalityTrait.Loyal:
                    Loyalty        += 25f;
                    SlapTolerance  += 8f;
                    Anger          -= 10f;  // slower to anger
                    Morale         += 10f;
                    break;

                case PersonalityTrait.Mercenary:
                    Loyalty        -= 20f;  // only cares about gold
                    Anger          += 5f;
                    Productivity   += 10f;  // motivated by greed
                    SlapTolerance  -= 5f;   // will walk if mistreated
                    break;

                case PersonalityTrait.Lazy:
                    Productivity   -= 20f;
                    FatigueRate_Override = GameConstants.FatigueRate * 1.5f;
                    Morale         -= 10f;
                    SlapTolerance  -= 2f;
                    break;
            }

            // Clamp everything into [0, 100]
            Fear          = Clamp01_100(Fear);
            Loyalty       = Clamp01_100(Loyalty);
            Anger         = Clamp01_100(Anger);
            Morale        = Clamp01_100(Morale);
            PainTolerance = Clamp01_100(PainTolerance);
            CombatAbility = Clamp01_100(CombatAbility);
            SlapTolerance = Math.Max(1f, Math.Min(GameConstants.MaxSlapTolerance, SlapTolerance));
        }

        /// <summary>
        /// Per-unit fatigue accumulation rate override; non-zero only for Lazy personality.
        /// The state machine and recovery logic should prefer this value when non-zero.
        /// </summary>
        public float FatigueRate_Override { get; private set; }

        /// <summary>Returns the effective fatigue rate for this unit.</summary>
        public float EffectiveFatigueRate =>
            FatigueRate_Override > 0f ? FatigueRate_Override : GameConstants.FatigueRate;

        // =====================================================================
        // Public methods
        // =====================================================================

        /// <summary>
        /// Computes the combined productivity multiplier from morale, fear, and fatigue.
        ///
        /// Formula:
        ///   base = Productivity / 100
        ///   moraleBonus  = (Morale - 50) / 100           → [-0.5, +0.5]
        ///   fearBonus    = Fear > FearStress ? -0.1 : Fear / 200  → small positive below stress
        ///   fatiguePenalty = Fatigue > 80 ? -0.3 : 0
        ///   result = clamp(base + moraleBonus + fearBonus + fatiguePenalty, 0.05, 2.0)
        /// </summary>
        public float GetProductivityMultiplier()
        {
            float baseMulti = Productivity / 100f;

            // Morale contribution: centred on 50; ±0.5 range
            float moraleContrib = (Morale - 50f) / 100f;

            // Fear contribution: small bonus below stress threshold, penalty above
            float fearContrib;
            if (Fear > GameConstants.FearStressThreshold)
                fearContrib = -0.15f;
            else
                fearContrib = Fear / 200f; // 0 → 0.4 (at threshold: 0.4)

            // Fatigue penalty
            float fatiguePenalty = Fatigue > GameConstants.FatigueDistressThreshold ? -0.30f : 0f;

            // Hunger penalty
            float hungerPenalty = Hunger > GameConstants.HungerDistressThreshold ? -0.20f : 0f;

            float total = baseMulti + moraleContrib + fearContrib + fatiguePenalty + hungerPenalty;
            return Math.Max(0.05f, Math.Min(2.0f, total));
        }

        /// <summary>
        /// Returns true when the unit is in enough distress that it will seek
        /// to fulfil a basic need rather than work.
        /// </summary>
        public bool IsInDistress()
        {
            return Hunger  > GameConstants.HungerDistressThreshold
                || Fatigue > GameConstants.FatigueDistressThreshold
                || Anger   > GameConstants.AngerRebellionThreshold - 5f; // early warning
        }

        /// <summary>
        /// Determines how this unit reacts to being slapped, based on personality,
        /// current psychological state, and how close to its tolerance limit it is.
        /// </summary>
        /// <param name="slapForce">Raw force of the slap (typically 1-10).</param>
        public SlapResponse EvaluateSlapResponse(float slapForce)
        {
            float accumulationRatio = SlapTolerance > 0f
                ? CurrentSlapAccumulation / SlapTolerance
                : 1f;

            // Masochistic units love it until they are pushed far past tolerance
            if (Personality == PersonalityTrait.Masochistic)
            {
                if (accumulationRatio < 1.5f) return SlapResponse.BecomeExcited;
                if (accumulationRatio < 2.0f) return SlapResponse.SpeedUp;
                return SlapResponse.Ignore; // too numb to care
            }

            // Cowardly units comply quickly but quit when pushed
            if (Personality == PersonalityTrait.Cowardly)
            {
                if (accumulationRatio < 0.5f) return SlapResponse.SpeedUp;
                if (accumulationRatio < 0.9f) return SlapResponse.BecomeAngry;
                return SlapResponse.Quit;
            }

            // Aggressive units escalate fast
            if (Personality == PersonalityTrait.Aggressive)
            {
                if (accumulationRatio < 0.4f) return SlapResponse.SpeedUp;
                if (accumulationRatio < 0.75f) return SlapResponse.BecomeAngry;
                return SlapResponse.Rebel;
            }

            // Loyal units absorb a great deal before breaking
            if (Personality == PersonalityTrait.Loyal)
            {
                if (accumulationRatio < 0.8f) return SlapResponse.SpeedUp;
                if (accumulationRatio < 1.2f) return SlapResponse.BecomeAngry;
                return SlapResponse.BecomeAngry; // still won't rebel easily
            }

            // Mercenary: walks out when mistreated, doesn't fight back
            if (Personality == PersonalityTrait.Mercenary)
            {
                if (accumulationRatio < 0.5f) return SlapResponse.SpeedUp;
                if (accumulationRatio < 0.85f) return SlapResponse.BecomeAngry;
                return SlapResponse.Quit;
            }

            // Lazy: ignores small slaps, rebels when pushed
            if (Personality == PersonalityTrait.Lazy)
            {
                if (accumulationRatio < 0.3f) return SlapResponse.Ignore;
                if (accumulationRatio < 0.7f) return SlapResponse.SpeedUp;
                if (accumulationRatio < 1.0f) return SlapResponse.BecomeAngry;
                return SlapResponse.Rebel;
            }

            // Generic fallback
            if (accumulationRatio < 0.5f) return SlapResponse.SpeedUp;
            if (accumulationRatio < 0.9f) return SlapResponse.BecomeAngry;
            return SlapResponse.Rebel;
        }

        /// <summary>
        /// Accumulates slap force against <see cref="CurrentSlapAccumulation"/>,
        /// increases Fear and Anger, and may trigger a rebellion if tolerance is breached.
        /// </summary>
        /// <param name="force">Raw slap force (positive value).</param>
        public void ApplySlapDamage(float force)
        {
            if (!IsAlive || IsImprisoned) return;

            CurrentSlapAccumulation += Math.Abs(force);
            SlapCount++;
            LastSlappedTime = 0f; // caller is responsible for providing game-time; reset here as a marker

            // Fear rises sharply with slaps
            Fear  = Clamp01_100(Fear  + force * 3f);

            // Anger rises — modulated by loyalty
            float angerGain = force * 2f * (1f - Loyalty / 200f); // loyalty dampens anger
            Anger = Clamp01_100(Anger + angerGain);

            // Masochistic units gain morale from slaps
            if (Personality == PersonalityTrait.Masochistic)
                Morale = Clamp01_100(Morale + force * 1.5f);
            else
                Morale = Clamp01_100(Morale - force * 0.5f);
        }

        /// <summary>
        /// Applies per-frame recovery and decay to psychological and biological stats.
        /// Called by the simulation tick; <paramref name="deltaTime"/> is elapsed seconds.
        /// </summary>
        /// <param name="deltaTime">Seconds since last tick.</param>
        /// <param name="settings">Balance settings to use for rates.</param>
        public void RecoverOverTime(float deltaTime, GameSettings settings)
        {
            if (!IsAlive) return;

            // -- Decay anger and fear ----------------------------------------
            Anger = Math.Max(0f, Anger - settings.AngerDecayRate * deltaTime);
            Fear  = Math.Max(0f, Fear  - settings.FearDecayRate  * deltaTime);

            // -- Recover morale only when not in acute distress ---------------
            if (!IsInDistress())
                Morale = Math.Min(100f, Morale + settings.MoraleRecoveryRate * deltaTime);

            // -- Biological needs accumulate passively ------------------------
            if (CurrentState != UnitState.Eating)
                Hunger = Math.Min(100f, Hunger + settings.HungerRate * deltaTime);

            if (CurrentState != UnitState.Resting)
            {
                float fatigueRate = FatigueRate_Override > 0f
                    ? FatigueRate_Override
                    : settings.FatigueRate;
                Fatigue = Math.Min(100f, Fatigue + fatigueRate * deltaTime);
            }

            // -- Slap accumulation decays over time ---------------------------
            CurrentSlapAccumulation = Math.Max(
                0f,
                CurrentSlapAccumulation - settings.SlapAccumulationDecay * deltaTime);
        }

        /// <summary>
        /// Marks this unit as dead. Once set, <see cref="IsAlive"/> cannot be restored.
        /// Callers should then publish a <see cref="UnitDiedEvent"/> via the event bus.
        /// </summary>
        public void Kill()
        {
            IsAlive       = false;
            CurrentHealth = 0f;
            CurrentState  = UnitState.Dead;
            CurrentTask   = TaskType.None;
        }

        /// <summary>
        /// Applies a health delta (negative = damage, positive = healing).
        /// Calls <see cref="Kill"/> automatically if health reaches zero.
        /// </summary>
        /// <returns>True if the unit died as a result.</returns>
        public bool ApplyHealthDelta(float delta)
        {
            if (!IsAlive) return false;

            CurrentHealth = Math.Max(0f, Math.Min(MaxHealth, CurrentHealth + delta));
            if (CurrentHealth <= 0f)
            {
                Kill();
                return true;
            }

            return false;
        }

        // =====================================================================
        // Utility
        // =====================================================================

        private static float Clamp01_100(float value) => Math.Max(0f, Math.Min(100f, value));

        public override string ToString() =>
            $"[Unit:{DisplayName} | Role:{Role} | Personality:{Personality} | " +
            $"State:{CurrentState} | HP:{CurrentHealth:F0}/{MaxHealth:F0} | " +
            $"Fear:{Fear:F0} Anger:{Anger:F0} Morale:{Morale:F0}]";
    }
}

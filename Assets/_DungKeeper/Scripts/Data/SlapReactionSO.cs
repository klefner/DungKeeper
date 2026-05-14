using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungKeeper
{
    // =========================================================================
    // Data struct
    // =========================================================================

    /// <summary>
    /// Defines the outcome of one tier of slap accumulation.
    /// Entries in <see cref="SlapReactionSO.Reactions"/> must be sorted by
    /// <see cref="SlapAccumulationThreshold"/> ascending so that the lookup
    /// returns the highest matching tier efficiently.
    /// </summary>
    [Serializable]
    public struct SlapReactionEntry
    {
        [Tooltip("Ratio of CurrentSlapAccumulation / SlapTolerance at which this entry applies (0 = any, 1 = at tolerance limit).")]
        [Range(0f, 2f)]
        public float SlapAccumulationThreshold;

        [Tooltip("The discrete behavioural response triggered when this tier is matched.")]
        public SlapResponse Response;

        [Tooltip("Short feedback message shown in the overlord's notification feed.")]
        [TextArea(1, 3)]
        public string FeedbackMessage;

        [Tooltip("How much the unit's Fear stat changes (positive = rise, negative = drop).")]
        public float FearChange;

        [Tooltip("How much the unit's Anger stat changes.")]
        public float AngerChange;

        [Tooltip("How much the unit's Loyalty stat changes.")]
        public float LoyaltyChange;

        [Tooltip("Signed delta applied to Productivity multiplier for the duration below.")]
        public float ProductivityModifier;

        [Tooltip("Seconds the productivity modifier is active before it expires.")]
        [Min(0f)]
        public float Duration;
    }

    // =========================================================================
    // ScriptableObject
    // =========================================================================

    /// <summary>
    /// ScriptableObject that describes how a specific unit archetype reacts to
    /// being slapped at escalating accumulation ratios.
    ///
    /// <para>
    ///   Attach one of these assets to a <see cref="UnitTypeSO"/> so that the
    ///   slap system can look up the correct tier and apply personality modifiers.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "DungKeeper/Slap Reaction Table", fileName = "SlapReactions_New")]
    public sealed class SlapReactionSO : ScriptableObject
    {
        // =====================================================================
        // Per-slap base rates
        // =====================================================================

        [Header("Per-Slap Base Rates")]
        [Tooltip("Anger added to the unit each time it is slapped (before personality scaling).")]
        [SerializeField, Min(0f)] private float _baseAngerGainPerSlap = 5f;

        [Tooltip("Fear added to the unit each time it is slapped (before personality scaling).")]
        [SerializeField, Min(0f)] private float _baseFearGainPerSlap = 8f;

        [Tooltip("Loyalty lost each time the unit is slapped (positive = loss; applied as negative delta).")]
        [SerializeField, Min(0f)] private float _baseLoyaltyLossPerSlap = 2f;

        // =====================================================================
        // Reaction tiers
        // =====================================================================

        [Header("Reaction Tiers (sorted ascending by threshold)")]
        [Tooltip("List of reaction entries; each entry covers the range from the previous threshold to this one. Must be sorted by SlapAccumulationThreshold ascending.")]
        [SerializeField] private List<SlapReactionEntry> _reactions = new List<SlapReactionEntry>();

        // =====================================================================
        // Public read-only properties
        // =====================================================================

        public float                      BaseAngerGainPerSlap   => _baseAngerGainPerSlap;
        public float                      BaseFearGainPerSlap    => _baseFearGainPerSlap;
        public float                      BaseLoyaltyLossPerSlap => _baseLoyaltyLossPerSlap;
        public IReadOnlyList<SlapReactionEntry> Reactions        => _reactions;

        // =====================================================================
        // Primary API
        // =====================================================================

        /// <summary>
        /// Returns the best-matching <see cref="SlapReactionEntry"/> for the given
        /// accumulation ratio and optionally adjusts its values based on
        /// <paramref name="personality"/>.
        /// </summary>
        /// <param name="accumulationRatio">
        ///   CurrentSlapAccumulation / SlapTolerance — typically 0–2+.
        /// </param>
        /// <param name="personality">
        ///   The unit's personality, used to scale fear/anger/loyalty deltas.
        /// </param>
        /// <returns>
        ///   A copy of the best-matching entry with personality modifiers baked in.
        ///   If no entries are configured a safe default "SpeedUp" entry is returned.
        /// </returns>
        public SlapReactionEntry GetReaction(float accumulationRatio, PersonalityTrait personality)
        {
            if (_reactions == null || _reactions.Count == 0)
                return BuildDefaultEntry(accumulationRatio, personality);

            // Walk the list (assumed to be sorted ascending) and take the last
            // entry whose threshold is <= accumulationRatio, giving us the highest
            // applicable tier.
            SlapReactionEntry best = _reactions[0]; // always start with the lowest tier
            for (int i = 0; i < _reactions.Count; i++)
            {
                if (_reactions[i].SlapAccumulationThreshold <= accumulationRatio)
                    best = _reactions[i];
                else
                    break; // sorted list — no need to continue
            }

            return ApplyPersonalityModifiers(best, personality, accumulationRatio);
        }

        // =====================================================================
        // Private helpers
        // =====================================================================

        /// <summary>
        /// Applies personality-specific scaling to the stat deltas of an entry.
        /// Returns a mutated copy; the original struct in the list is not changed.
        /// </summary>
        private static SlapReactionEntry ApplyPersonalityModifiers(
            SlapReactionEntry entry,
            PersonalityTrait  personality,
            float             accumulationRatio)
        {
            // We work on a mutable copy because SlapReactionEntry is a value type.
            switch (personality)
            {
                case PersonalityTrait.Cowardly:
                    // Fear spikes; anger dampened; slight boost in SpeedUp probability
                    entry.FearChange   *= 1.5f;
                    entry.AngerChange  *= 0.6f;
                    // Cowardly units speed up even at moderate accumulation
                    if (accumulationRatio < 0.5f && entry.Response == SlapResponse.BecomeAngry)
                        entry.Response = SlapResponse.SpeedUp;
                    break;

                case PersonalityTrait.Aggressive:
                    // Anger amplified; small fear reduction
                    entry.AngerChange  *= 1.5f;
                    entry.FearChange   *= 0.7f;
                    // Aggressive units escalate to Rebel at lower thresholds
                    if (accumulationRatio >= 0.75f && entry.Response == SlapResponse.BecomeAngry)
                        entry.Response = SlapResponse.Rebel;
                    break;

                case PersonalityTrait.Masochistic:
                    // Fear and anger suppressed; loyalty improved; flip to excitement
                    entry.FearChange    = -Mathf.Abs(entry.FearChange); // fear goes down
                    entry.AngerChange  *= 0.2f;
                    entry.LoyaltyChange = Mathf.Abs(entry.LoyaltyChange) * 0.5f; // loyalty rises
                    entry.ProductivityModifier = Mathf.Abs(entry.ProductivityModifier); // always positive
                    if (entry.Response != SlapResponse.Rebel && entry.Response != SlapResponse.Quit)
                        entry.Response = SlapResponse.BecomeExcited;
                    // Custom message hint
                    entry.FeedbackMessage = "[Masochistic] " + entry.FeedbackMessage;
                    break;

                case PersonalityTrait.Loyal:
                    // Anger heavily dampened; loyalty loss reduced
                    entry.AngerChange      *= 0.4f;
                    entry.LoyaltyChange    *= 0.3f; // loyalty loss is much smaller
                    entry.ProductivityModifier *= 1.1f;
                    // Loyal units resist escalation
                    if (entry.Response == SlapResponse.Rebel && accumulationRatio < 1.2f)
                        entry.Response = SlapResponse.BecomeAngry;
                    break;

                case PersonalityTrait.Mercenary:
                    // Loyalty tanks fast; anger moderate; may walk without combat
                    entry.LoyaltyChange    *= 1.8f;
                    entry.AngerChange      *= 1.2f;
                    if (accumulationRatio >= 0.85f && entry.Response != SlapResponse.Quit)
                        entry.Response = SlapResponse.Quit;
                    break;

                case PersonalityTrait.Lazy:
                    // Ignores mild slaps; anger slow to rise but rebellion follows
                    if (accumulationRatio < 0.3f)
                    {
                        entry.Response = SlapResponse.Ignore;
                        entry.FearChange  = 0f;
                        entry.AngerChange = 0f;
                    }
                    else if (accumulationRatio >= 1.0f)
                    {
                        entry.Response = SlapResponse.Rebel;
                    }

                    entry.ProductivityModifier *= 0.6f; // lazy units benefit less from slaps
                    break;
            }

            return entry;
        }

        /// <summary>Safe default when no entries are configured.</summary>
        private static SlapReactionEntry BuildDefaultEntry(float ratio, PersonalityTrait personality)
        {
            SlapResponse defaultResponse = ratio < 0.5f ? SlapResponse.SpeedUp
                                         : ratio < 0.9f ? SlapResponse.BecomeAngry
                                         : SlapResponse.Rebel;

            var entry = new SlapReactionEntry
            {
                SlapAccumulationThreshold = 0f,
                Response             = defaultResponse,
                FeedbackMessage      = "The unit reacts to the slap.",
                FearChange           = 8f,
                AngerChange          = 5f,
                LoyaltyChange        = -2f,
                ProductivityModifier = defaultResponse == SlapResponse.SpeedUp ? 0.25f : -0.20f,
                Duration             = 15f
            };

            return ApplyPersonalityModifiers(entry, personality, ratio);
        }

        // =====================================================================
        // Editor validation
        // =====================================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_reactions == null) return;

            // Warn the designer if the list is not sorted
            for (int i = 1; i < _reactions.Count; i++)
            {
                if (_reactions[i].SlapAccumulationThreshold < _reactions[i - 1].SlapAccumulationThreshold)
                {
                    Debug.LogWarning(
                        $"[SlapReactionSO] '{name}' — entry [{i}] threshold " +
                        $"({_reactions[i].SlapAccumulationThreshold:F2}) is less than entry [{i - 1}] " +
                        $"({_reactions[i - 1].SlapAccumulationThreshold:F2}). " +
                        "Reactions must be sorted ascending by SlapAccumulationThreshold.", this);
                }
            }
        }
#endif
    }
}

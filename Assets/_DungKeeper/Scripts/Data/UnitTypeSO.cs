using System;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// ScriptableObject that defines a unit archetype (e.g. "Groveling Peon", "Fury Grunt").
    /// Each instance lives in the project as an asset; at runtime a <see cref="UnitData"/>
    /// instance is produced via <see cref="CreateInstance"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "DungKeeper/Unit Type", fileName = "UnitType_New")]
    public sealed class UnitTypeSO : ScriptableObject
    {
        // =====================================================================
        // Identity & presentation
        // =====================================================================

        [Header("Identity")]
        [Tooltip("Human-readable name displayed in the dungeon UI.")]
        [SerializeField] private string _displayName = "Unnamed Unit";

        [Tooltip("The functional role this unit fulfils by default.")]
        [SerializeField] private UnitRole _defaultRole = UnitRole.Worker;

        [Tooltip("The personality archetype that shapes this unit's reactions to stimuli.")]
        [SerializeField] private PersonalityTrait _defaultPersonality = PersonalityTrait.Cowardly;

        [Tooltip("Portrait sprite shown in the unit panel and recruitment screen.")]
        [SerializeField] private Sprite _portrait;

        [Tooltip("Prefab spawned in the dungeon when this unit type is recruited.")]
        [SerializeField] private GameObject _prefab;

        // =====================================================================
        // Base stats
        // =====================================================================

        [Header("Base Stats")]
        [Tooltip("Maximum hit points at recruitment (before upgrades).")]
        [SerializeField, Min(1f)] private float _baseMaxHealth = 60f;

        [Tooltip("Base work-output multiplier (0-100 scale; 100 = maximum productivity).")]
        [SerializeField, Range(0f, 100f)] private float _baseProductivity = 60f;

        [Tooltip("Damage threshold before the unit flees or surrenders (0-100 scale).")]
        [SerializeField, Range(0f, 100f)] private float _basePainTolerance = 40f;

        [Tooltip("Accumulated-slap ceiling before reactions escalate (units, not ratio).")]
        [SerializeField, Range(1f, 50f)] private float _baseSlapTolerance = 10f;

        [Tooltip("Raw combat effectiveness used in fight resolution (0-100 scale).")]
        [SerializeField, Range(0f, 100f)] private float _baseCombatAbility = 20f;

        // =====================================================================
        // Psychological starting values
        // =====================================================================

        [Header("Starting Psychology (0 – 100)")]
        [Tooltip("Fear the unit starts with — high fear accelerates work until stress threshold.")]
        [SerializeField, Range(0f, 100f)] private float _baseFear = 30f;

        [Tooltip("Starting loyalty — higher loyalty dampens anger and slap reactivity.")]
        [SerializeField, Range(0f, 100f)] private float _baseLoyalty = 50f;

        [Tooltip("Starting anger — above AngerRebellionThreshold the unit enters rebellion.")]
        [SerializeField, Range(0f, 100f)] private float _baseAnger = 0f;

        [Tooltip("Starting morale — affects productivity multiplier and need recovery rates.")]
        [SerializeField, Range(0f, 100f)] private float _baseMorale = 60f;

        // =====================================================================
        // Recruitment cost
        // =====================================================================

        [Header("Recruitment Cost")]
        [Tooltip("Gold required to recruit one unit of this type.")]
        [SerializeField, Min(0f)] private float _recruitmentCostGold = 50f;

        [Tooltip("Essence required to recruit one unit of this type.")]
        [SerializeField, Min(0f)] private float _recruitmentCostEssence = 0f;

        // =====================================================================
        // Lore & reactions
        // =====================================================================

        [Header("Lore & Reactions")]
        [Tooltip("Flavour text displayed on the unit-type card in the recruitment screen.")]
        [TextArea(3, 6)]
        [SerializeField] private string _flavorText = string.Empty;

        [Tooltip("ScriptableObject that defines how this unit type reacts to slaps at different tolerance levels.")]
        [SerializeField] private SlapReactionSO _slapReactions;

        // =====================================================================
        // Public read-only properties
        // =====================================================================

        public string          DisplayName           => _displayName;
        public UnitRole        DefaultRole           => _defaultRole;
        public PersonalityTrait DefaultPersonality   => _defaultPersonality;
        public Sprite          Portrait              => _portrait;
        public GameObject      Prefab                => _prefab;
        public float           BaseMaxHealth         => _baseMaxHealth;
        public float           BaseProductivity      => _baseProductivity;
        public float           BasePainTolerance     => _basePainTolerance;
        public float           BaseSlapTolerance     => _baseSlapTolerance;
        public float           BaseCombatAbility     => _baseCombatAbility;
        public float           BaseFear              => _baseFear;
        public float           BaseLoyalty           => _baseLoyalty;
        public float           BaseAnger             => _baseAnger;
        public float           BaseMorale            => _baseMorale;
        public float           RecruitmentCostGold   => _recruitmentCostGold;
        public float           RecruitmentCostEssence => _recruitmentCostEssence;
        public string          FlavorText            => _flavorText;
        public SlapReactionSO  SlapReactions         => _slapReactions;

        // =====================================================================
        // Factory
        // =====================================================================

        /// <summary>
        /// Creates a new <see cref="UnitData"/> runtime instance initialised from
        /// this archetype's base values.  Personality-based modifiers are applied
        /// on top of the archetype stats so the final unit reflects both sources.
        /// </summary>
        /// <param name="customName">
        /// Optional display name override.  When <c>null</c> the archetype's
        /// <see cref="DisplayName"/> is used verbatim.
        /// </param>
        /// <returns>A freshly created, fully initialised <see cref="UnitData"/>.</returns>
        public UnitData CreateUnitData(string customName = null)
        {
            string resolvedName = string.IsNullOrWhiteSpace(customName) ? _displayName : customName;

            // UnitData.Create assigns a new GUID automatically.
            var unit = UnitData.Create(resolvedName, _defaultRole, _defaultPersonality);

            // Override the constructor defaults with archetype-specific values.
            // Personality deltas are applied by the UnitData constructor; here we
            // set the archetype baselines that the constructor would otherwise derive
            // purely from role.  We re-apply them as an additive override so that
            // personality modifiers layered on top in the constructor stay intact.
            unit.MaxHealth   = _baseMaxHealth;
            unit.Health      = _baseMaxHealth;

            // Productivity on UnitData is on the 0-100 scale (same as Inspector value).
            // GetProductivityMultiplier() normalises it internally.
            unit.Productivity = _baseProductivity;

            // Psychological seeds (clamped to [0, 100] on the UnitData side)
            unit.Fear    = Mathf.Clamp(_baseFear,    0f, 100f);
            unit.Loyalty = Mathf.Clamp(_baseLoyalty, 0f, 100f);
            unit.Anger   = Mathf.Clamp(_baseAnger,   0f, 100f);
            unit.Morale  = Mathf.Clamp(_baseMorale,  0f, 100f);

            // Attack (= CombatAbility) derived from the archetype combat stat.
            // The merged UnitData maps Attack → CombatAbility; Defense is not a
            // separate field in the current model.
            unit.Attack = _baseCombatAbility;

            return unit;
        }

        // =====================================================================
        // Editor validation
        // =====================================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_displayName))
                _displayName = name; // fall back to the asset filename
        }
#endif
    }
}

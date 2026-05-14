using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// ScriptableObject that defines a room archetype (e.g. "Production Chamber",
    /// "Barracks Pit").  One asset per room type; runtime <see cref="RoomData"/>
    /// instances are created via <see cref="RoomData.Create"/> using the values
    /// stored here.
    /// </summary>
    [CreateAssetMenu(menuName = "DungKeeper/Room Type", fileName = "RoomType_New")]
    public sealed class RoomTypeSO : ScriptableObject
    {
        // =====================================================================
        // Identity & presentation
        // =====================================================================

        [Header("Identity")]
        [Tooltip("Human-readable name shown in the build menu and room panel.")]
        [SerializeField] private string _displayName = "Unnamed Room";

        [Tooltip("Functional category that determines which tasks can be performed here.")]
        [SerializeField] private RoomType _roomType = RoomType.ProductionChamber;

        [Tooltip("Icon used in the build menu and minimap.")]
        [SerializeField] private Sprite _icon;

        [Tooltip("Prefab placed in the dungeon grid when the room is constructed.")]
        [SerializeField] private GameObject _prefab;

        // =====================================================================
        // Grid footprint
        // =====================================================================

        [Header("Grid Footprint")]
        [Tooltip("Horizontal cell span (1–3).")]
        [SerializeField, Range(1, 3)] private int _gridSizeX = 1;

        [Tooltip("Vertical cell span (1–3).")]
        [SerializeField, Range(1, 3)] private int _gridSizeY = 1;

        // =====================================================================
        // Capacity & production
        // =====================================================================

        [Header("Capacity & Production")]
        [Tooltip("Maximum number of units that can be assigned concurrently.")]
        [SerializeField, Min(1)] private int _maxCapacity = 4;

        [Tooltip("Resource generated per assigned worker per second at full efficiency.")]
        [SerializeField, Min(0f)] private float _baseProductionRate = 1f;

        [Tooltip("The resource type this room primarily produces.")]
        [SerializeField] private ResourceType _primaryResource = ResourceType.Gold;

        // =====================================================================
        // Build & demolish cost
        // =====================================================================

        [Header("Economy")]
        [Tooltip("Gold cost to construct this room.")]
        [SerializeField, Min(0f)] private float _buildCostGold = 100f;

        [Tooltip("Essence cost to construct this room.")]
        [SerializeField, Min(0f)] private float _buildCostEssence = 0f;

        [Tooltip("Fraction of build cost returned when the room is demolished (0 = none, 1 = full refund).")]
        [SerializeField, Range(0f, 1f)] private float _demolishRefundPercent = 0.5f;

        // =====================================================================
        // Structural health
        // =====================================================================

        [Header("Structural")]
        [Tooltip("Maximum hit points.  Reaches 0 when fully demolished by invaders or sabotage.")]
        [SerializeField, Min(1f)] private float _maxHealth = 200f;

        // =====================================================================
        // Task & availability
        // =====================================================================

        [Header("Task & Availability")]
        [Tooltip("The primary task type performed by units assigned to this room.")]
        [SerializeField] private TaskType _associatedTask = TaskType.MineGold;

        [Tooltip("Whether this room requires units assigned to it to produce resources.")]
        [SerializeField] private bool _requiresWorkers = true;

        [Tooltip("Whether this room is unlocked from the very start of a campaign (no research required).")]
        [SerializeField] private bool _availableFromStart = true;

        // =====================================================================
        // Lore
        // =====================================================================

        [Header("Lore")]
        [Tooltip("Short flavour text displayed on the room-type card in the build menu.")]
        [TextArea(3, 6)]
        [SerializeField] private string _flavorText = string.Empty;

        // =====================================================================
        // Public read-only properties
        // =====================================================================

        public string       DisplayName          => _displayName;
        public RoomType     RoomType             => _roomType;
        public Sprite       Icon                 => _icon;
        public GameObject   Prefab               => _prefab;
        public int          GridSizeX            => _gridSizeX;
        public int          GridSizeY            => _gridSizeY;
        public int          MaxCapacity          => _maxCapacity;
        public float        BaseProductionRate   => _baseProductionRate;
        public ResourceType PrimaryResource      => _primaryResource;
        public float        BuildCostGold        => _buildCostGold;
        public float        BuildCostEssence     => _buildCostEssence;
        public float        DemolishRefundPercent => _demolishRefundPercent;
        public float        MaxHealth            => _maxHealth;
        public string       FlavorText           => _flavorText;
        public TaskType     AssociatedTask       => _associatedTask;
        public bool         RequiresWorkers      => _requiresWorkers;
        public bool         AvailableFromStart   => _availableFromStart;

        // =====================================================================
        // Convenience helpers
        // =====================================================================

        /// <summary>
        /// Returns the Gold and Essence refund amounts when this room is demolished
        /// based on <see cref="DemolishRefundPercent"/>.
        /// </summary>
        public (float gold, float essence) GetDemolishRefund()
            => (_buildCostGold    * _demolishRefundPercent,
                _buildCostEssence * _demolishRefundPercent);

        /// <summary>
        /// Creates a new <see cref="RoomData"/> instance initialised from this archetype.
        /// </summary>
        /// <param name="customName">Optional display name override; falls back to <see cref="DisplayName"/>.</param>
        public RoomData CreateInstance(string customName = null)
        {
            string resolvedName = string.IsNullOrWhiteSpace(customName) ? _displayName : customName;
            return RoomData.Create(resolvedName, _roomType, _maxCapacity, _baseProductionRate, _maxHealth);
        }

        // =====================================================================
        // Editor validation
        // =====================================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(_displayName))
                _displayName = name;

            // Sanity-check: a room that requires workers but has zero capacity would
            // never produce anything — warn the designer.
            if (_requiresWorkers && _maxCapacity < 1)
                Debug.LogWarning($"[RoomTypeSO] '{_displayName}' requires workers but MaxCapacity is 0.", this);
        }
#endif
    }
}

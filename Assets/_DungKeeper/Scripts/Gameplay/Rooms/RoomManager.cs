using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Manages the dungeon's grid-based room placement system.
    /// Handles placement preview (ghost), cost validation via <see cref="ResourceSystem"/>,
    /// room instantiation, and demolition.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomManager : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Inspector fields
        // -------------------------------------------------------------------------

        [Header("Grid Configuration")]
        [SerializeField] private int   _gridWidth  = 20;
        [SerializeField] private int   _gridHeight = 20;
        [SerializeField] private float _cellSize   = 2f;

        [Header("Room Definitions")]
        [Tooltip("All available room type archetypes. Looked up by RoomType enum value.")]
        [SerializeField] private RoomTypeSO[] _roomDefinitions;

        [Header("Placement Preview")]
        [Tooltip("Generic ghost prefab used when a RoomTypeSO has no Prefab set.")]
        [SerializeField] private GameObject _ghostPrefab;

        // -------------------------------------------------------------------------
        // Runtime state
        // -------------------------------------------------------------------------

        private readonly Dictionary<Vector3Int, RoomController> _grid
            = new Dictionary<Vector3Int, RoomController>();

        private GameObject _ghostInstance;
        private Renderer[] _ghostRenderers;
        private RoomType   _selectedRoomType;
        private bool       _isPlacementMode;

        // Ghost material tint colors.
        private static readonly Color ValidPlacementColor   = new Color(0.2f, 1f,  0.3f, 0.45f);
        private static readonly Color InvalidPlacementColor = new Color(1f,  0.2f, 0.1f, 0.45f);

        // Layer mask for the ground plane raycast.
        private int _groundLayerMask;

        // -------------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            _groundLayerMask = LayerMask.GetMask("Ground");
            if (_groundLayerMask == 0)
                _groundLayerMask = ~0; // fall back to all layers if "Ground" is not configured
        }

        private void Update()
        {
            if (!_isPlacementMode) return;
            if (Camera.main == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _groundLayerMask)) return;

            Vector3Int gridPos  = WorldToGrid(hit.point);
            Vector3    worldPos = GridToWorld(gridPos);

            // Move ghost to snapped position.
            if (_ghostInstance != null)
                _ghostInstance.transform.position = worldPos;

            bool isValid = IsWithinGrid(gridPos) && !_grid.ContainsKey(gridPos);

            // Tint ghost renderers to signal validity.
            if (_ghostRenderers != null)
            {
                Color tint = isValid ? ValidPlacementColor : InvalidPlacementColor;
                foreach (Renderer r in _ghostRenderers)
                    if (r != null) r.material.color = tint;
            }

            // Left-click: confirm placement if cell is valid.
            if (Input.GetMouseButtonDown(0))
            {
                if (isValid)
                    TryPlaceRoom(gridPos, _selectedRoomType);
                // Invalid cell: do not exit — let the player reposition.
            }

            // Right-click or Escape: cancel.
            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
                ExitPlacementMode();
        }

        // -------------------------------------------------------------------------
        // Placement mode API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Enters placement mode for the given room type, showing a ghost preview.
        /// </summary>
        public void EnterPlacementMode(RoomType type)
        {
            ExitPlacementMode(); // destroy any prior ghost

            RoomTypeSO definition = FindDefinition(type);
            if (definition == null)
            {
                Debug.LogError($"[RoomManager] No RoomTypeSO found for RoomType.{type}.");
                return;
            }

            _selectedRoomType = type;
            _isPlacementMode  = true;

            // Use the definition's prefab if available, otherwise fall back to the generic ghost.
            GameObject prefabToUse = definition.Prefab != null ? definition.Prefab : _ghostPrefab;
            if (prefabToUse == null)
            {
                Debug.LogWarning("[RoomManager] No ghost prefab available. Placement preview invisible.");
                return;
            }

            _ghostInstance      = Instantiate(prefabToUse, Vector3.zero, Quaternion.identity);
            _ghostInstance.name = $"Ghost_{type}";

            // Disable colliders so the ghost does not interfere with raycasts.
            foreach (Collider col in _ghostInstance.GetComponentsInChildren<Collider>())
                col.enabled = false;

            _ghostRenderers = _ghostInstance.GetComponentsInChildren<Renderer>();
        }

        /// <summary>Exits placement mode and destroys the ghost preview.</summary>
        public void ExitPlacementMode()
        {
            _isPlacementMode = false;

            if (_ghostInstance != null)
            {
                Destroy(_ghostInstance);
                _ghostInstance  = null;
                _ghostRenderers = null;
            }
        }

        // -------------------------------------------------------------------------
        // Room placement & demolition
        // -------------------------------------------------------------------------

        /// <summary>
        /// Attempts to place a room of <paramref name="type"/> at <paramref name="gridPos"/>.
        /// Validates grid bounds, cell vacancy, and resource cost before instantiating.
        /// </summary>
        /// <returns>True if the room was placed successfully.</returns>
        public bool TryPlaceRoom(Vector3Int gridPos, RoomType type)
        {
            if (!IsWithinGrid(gridPos))
            {
                Debug.LogWarning($"[RoomManager] Grid position {gridPos} is out of bounds.");
                return false;
            }

            if (_grid.ContainsKey(gridPos))
            {
                Debug.LogWarning($"[RoomManager] Grid position {gridPos} is already occupied.");
                return false;
            }

            RoomTypeSO definition = FindDefinition(type);
            if (definition == null)
            {
                Debug.LogError($"[RoomManager] No definition found for RoomType.{type}.");
                return false;
            }

            // Deduct build cost via ResourceSystem (obtained from GameManager).
            ResourceSystem resources = GameManager.Instance != null
                ? GameManager.Instance.ResourceSystem
                : null;

            if (resources != null)
            {
                if (!resources.TrySpend(ResourceType.Gold, definition.BuildCostGold))
                {
                    Debug.Log($"[RoomManager] Insufficient gold to build {type} (need {definition.BuildCostGold}).");
                    return false;
                }

                if (definition.BuildCostEssence > 0f &&
                    !resources.TrySpend(ResourceType.Essence, definition.BuildCostEssence))
                {
                    // Refund gold already spent.
                    resources.Add(ResourceType.Gold, definition.BuildCostGold);
                    Debug.Log($"[RoomManager] Insufficient essence to build {type}.");
                    return false;
                }
            }

            // Instantiate room prefab.
            GameObject prefab = definition.Prefab != null ? definition.Prefab : _ghostPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[RoomManager] No prefab assigned for {type}. Refunding cost.");
                if (resources != null)
                {
                    resources.Add(ResourceType.Gold,    definition.BuildCostGold);
                    resources.Add(ResourceType.Essence, definition.BuildCostEssence);
                }
                return false;
            }

            Vector3        worldPos   = GridToWorld(gridPos);
            GameObject     roomGO     = Instantiate(prefab, worldPos, Quaternion.identity, transform);
            RoomController controller = roomGO.GetComponent<RoomController>();

            if (controller == null)
                controller = roomGO.AddComponent<RoomController>();

            // Create runtime RoomData from the ScriptableObject archetype.
            RoomData data = definition.CreateInstance();

            controller.Initialize(data, definition);
            _grid[gridPos] = controller;

            EventBus.Global.Publish(new RoomBuiltEvent(data));

            ExitPlacementMode();
            return true;
        }

        /// <summary>
        /// Demolishes the room at <paramref name="gridPos"/>, refunding a portion of its
        /// build cost, removing it from the grid, and unassigning all occupying units.
        /// </summary>
        /// <returns>True if a room existed at that position and was removed.</returns>
        public bool TryDemolishRoom(Vector3Int gridPos)
        {
            if (!_grid.TryGetValue(gridPos, out RoomController controller))
            {
                Debug.LogWarning($"[RoomManager] No room at grid position {gridPos} to demolish.");
                return false;
            }

            RoomData   data       = controller.Data;
            RoomTypeSO definition = controller.Definition;

            // Unassign all units.
            if (data != null && data.AssignedUnitIds.Count > 0)
            {
                foreach (string unitId in data.AssignedUnitIds)
                    Debug.Log($"[RoomManager] Unassigning unit {unitId} from demolished room {data.Name}.");
                data.AssignedUnitIds.Clear();
            }

            // Refund partial build cost.
            ResourceSystem resources = GameManager.Instance != null
                ? GameManager.Instance.ResourceSystem
                : null;

            if (resources != null && definition != null)
            {
                var (goldRefund, essenceRefund) = definition.GetDemolishRefund();
                if (goldRefund    > 0f) resources.Add(ResourceType.Gold,    goldRefund);
                if (essenceRefund > 0f) resources.Add(ResourceType.Essence, essenceRefund);
            }

            _grid.Remove(gridPos);
            Destroy(controller.gameObject);
            return true;
        }

        /// <summary>
        /// Called by <see cref="RoomController"/> when a room's health reaches zero.
        /// Removes the grid entry without a cost refund.
        /// </summary>
        public void NotifyRoomDestroyed(RoomController controller)
        {
            Vector3Int key   = default;
            bool       found = false;

            foreach (var kvp in _grid)
            {
                if (kvp.Value == controller)
                {
                    key   = kvp.Key;
                    found = true;
                    break;
                }
            }

            if (found)
            {
                _grid.Remove(key);
                if (controller != null && controller.gameObject != null)
                    Destroy(controller.gameObject);
            }
        }

        // -------------------------------------------------------------------------
        // Query API
        // -------------------------------------------------------------------------

        /// <summary>Returns the controller at <paramref name="pos"/>, or null if unoccupied.</summary>
        public RoomController GetRoomAt(Vector3Int pos)
        {
            _grid.TryGetValue(pos, out RoomController ctrl);
            return ctrl;
        }

        /// <summary>Returns a snapshot list of <see cref="RoomData"/> for every placed room.</summary>
        public List<RoomData> GetAllRoomData()
        {
            var result = new List<RoomData>(_grid.Count);
            foreach (RoomController ctrl in _grid.Values)
                if (ctrl != null && ctrl.Data != null)
                    result.Add(ctrl.Data);
            return result;
        }

        // -------------------------------------------------------------------------
        // Coordinate conversion
        // -------------------------------------------------------------------------

        /// <summary>Converts a grid cell coordinate to the world-space centre of that cell.</summary>
        public Vector3 GridToWorld(Vector3Int gridPos)
        {
            return new Vector3(
                gridPos.x * _cellSize + _cellSize * 0.5f,
                0f,
                gridPos.z * _cellSize + _cellSize * 0.5f);
        }

        /// <summary>Converts a world-space position to the enclosing grid cell coordinate.</summary>
        public Vector3Int WorldToGrid(Vector3 worldPos)
        {
            return new Vector3Int(
                Mathf.FloorToInt(worldPos.x / _cellSize),
                0,
                Mathf.FloorToInt(worldPos.z / _cellSize));
        }

        // -------------------------------------------------------------------------
        // Internal helpers
        // -------------------------------------------------------------------------

        private bool IsWithinGrid(Vector3Int pos)
            => pos.x >= 0 && pos.x < _gridWidth && pos.z >= 0 && pos.z < _gridHeight;

        private RoomTypeSO FindDefinition(RoomType type)
        {
            if (_roomDefinitions == null) return null;
            foreach (RoomTypeSO def in _roomDefinitions)
                if (def != null && def.RoomType == type) return def;
            return null;
        }
    }
}

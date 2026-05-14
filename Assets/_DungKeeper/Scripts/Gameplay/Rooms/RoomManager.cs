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
        [Tooltip("Array of all available room types. Index must match RoomType enum order or be looked up by RoomType field.")]
        [SerializeField] private RoomTypeSO[] _roomDefinitions;

        [Header("Placement Preview")]
        [SerializeField] private GameObject _ghostPrefab;

        [Header("Dependencies")]
        [Tooltip("Resource system used to validate and deduct construction costs.")]
        [SerializeField] private ResourceSystem _resourceSystem;

        // -------------------------------------------------------------------------
        // Runtime state
        // -------------------------------------------------------------------------

        private readonly Dictionary<Vector3Int, RoomController> _grid
            = new Dictionary<Vector3Int, RoomController>();

        private GameObject  _ghostInstance;
        private Renderer[]  _ghostRenderers;
        private RoomType    _selectedRoomType;
        private bool        _isPlacementMode;

        // Ghost material colors.
        private static readonly Color ValidPlacementColor   = new Color(0.2f, 1f, 0.3f, 0.45f);
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

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, _groundLayerMask)) return;

            Vector3Int gridPos = WorldToGrid(hit.point);
            Vector3    worldPos = GridToWorld(gridPos);

            // Move ghost to snapped position.
            if (_ghostInstance != null)
                _ghostInstance.transform.position = worldPos;

            bool isValid = IsWithinGrid(gridPos) && !_grid.ContainsKey(gridPos);

            // Tint ghost renderers.
            if (_ghostRenderers != null)
            {
                Color tint = isValid ? ValidPlacementColor : InvalidPlacementColor;
                foreach (Renderer r in _ghostRenderers)
                    if (r != null) r.material.color = tint;
            }

            // Left-click to confirm placement.
            if (Input.GetMouseButtonDown(0))
            {
                if (isValid)
                    TryPlaceRoom(gridPos, _selectedRoomType);
                // If invalid, do not exit placement mode — let the player reposition.
            }

            // Right-click or Escape to cancel.
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
            ExitPlacementMode(); // clean up any previous ghost

            RoomTypeSO definition = FindDefinition(type);
            if (definition == null)
            {
                Debug.LogError($"[RoomManager] No RoomTypeSO found for RoomType.{type}.");
                return;
            }

            _selectedRoomType = type;
            _isPlacementMode  = true;

            // Instantiate the ghost from either the room definition's prefab or the generic ghost prefab.
            GameObject prefabToUse = (definition.RoomPrefab != null) ? definition.RoomPrefab : _ghostPrefab;
            if (prefabToUse == null)
            {
                Debug.LogWarning("[RoomManager] No ghost prefab configured. Placement preview will be invisible.");
                return;
            }

            _ghostInstance  = Instantiate(prefabToUse, Vector3.zero, Quaternion.identity);
            _ghostInstance.name = $"Ghost_{type}";

            // Disable all colliders on the ghost so it does not interfere with raycasts.
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

            // Deduct build cost — ResourceSystem returns false if insufficient funds.
            if (_resourceSystem != null)
            {
                if (!_resourceSystem.TrySpend(ResourceType.Gold,    definition.GoldCost))
                {
                    Debug.Log($"[RoomManager] Insufficient gold to build {type}.");
                    return false;
                }
                if (definition.EssenceCost > 0f &&
                    !_resourceSystem.TrySpend(ResourceType.Essence, definition.EssenceCost))
                {
                    // Refund the gold we already spent.
                    _resourceSystem.Add(ResourceType.Gold, definition.GoldCost);
                    Debug.Log($"[RoomManager] Insufficient essence to build {type}.");
                    return false;
                }
            }

            // Instantiate the room prefab.
            GameObject prefab = definition.RoomPrefab != null ? definition.RoomPrefab : _ghostPrefab;
            if (prefab == null)
            {
                Debug.LogError($"[RoomManager] No prefab assigned for {type}.");
                return false;
            }

            Vector3          worldPos   = GridToWorld(gridPos);
            GameObject       roomGO     = Instantiate(prefab, worldPos, Quaternion.identity, transform);
            RoomController   controller = roomGO.GetComponent<RoomController>();

            if (controller == null)
                controller = roomGO.AddComponent<RoomController>();

            RoomData data = RoomData.Create(
                definition.DisplayName,
                type,
                definition.Capacity,
                definition.BaseProductionRate,
                definition.MaxHealth);

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

            RoomData      data       = controller.Data;
            RoomTypeSO    definition = controller.Definition;

            // Unassign all units from the room.
            if (data != null)
            {
                foreach (string unitId in data.AssignedUnitIds)
                    Debug.Log($"[RoomManager] Unassigning unit {unitId} from demolished room {data.Name}.");
                data.AssignedUnitIds.Clear();
            }

            // Refund partial cost.
            if (_resourceSystem != null && definition != null)
            {
                float goldRefund    = definition.GoldCost    * definition.DemolishRefundFraction;
                float essenceRefund = definition.EssenceCost * definition.DemolishRefundFraction;

                if (goldRefund > 0f)
                    _resourceSystem.Add(ResourceType.Gold, goldRefund);
                if (essenceRefund > 0f)
                    _resourceSystem.Add(ResourceType.Essence, essenceRefund);
            }

            _grid.Remove(gridPos);
            Destroy(controller.gameObject);
            return true;
        }

        /// <summary>
        /// Called by <see cref="RoomController"/> when a room's health drops to zero.
        /// Removes the entry from the grid without a cost refund.
        /// </summary>
        public void NotifyRoomDestroyed(RoomController controller)
        {
            Vector3Int key = default;
            bool found = false;

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

        /// <summary>Returns the controller at a grid position, or null.</summary>
        public RoomController GetRoomAt(Vector3Int pos)
        {
            _grid.TryGetValue(pos, out RoomController ctrl);
            return ctrl;
        }

        /// <summary>Aggregates <see cref="RoomData"/> from every placed room.</summary>
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

        /// <summary>Converts a grid cell coordinate to a world-space position (cell centre).</summary>
        public Vector3 GridToWorld(Vector3Int gridPos)
        {
            return new Vector3(
                gridPos.x * _cellSize + _cellSize * 0.5f,
                0f,
                gridPos.z * _cellSize + _cellSize * 0.5f);
        }

        /// <summary>Converts a world-space position to the nearest grid cell coordinate.</summary>
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

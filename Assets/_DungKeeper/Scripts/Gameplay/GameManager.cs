using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Central simulation orchestrator and scene-level singleton.
    ///
    /// Owns all pure-C# simulation systems, the master unit and room lists,
    /// and the accumulated game clock. Every other MonoBehaviour that needs
    /// to read or modify game state does so through this class.
    ///
    /// Architecture:
    ///   GameManager (MonoBehaviour / DontDestroyOnLoad singleton)
    ///     ├── SlapSystem      — evaluates slap force → SlapResponse
    ///     ├── MoraleSystem    — per-unit morale / fear / anger / needs tick
    ///     ├── TaskSystem      — task assignment and state transitions
    ///     ├── ResourceSystem  — Gold / Essence / Fear economy
    ///     ├── CombatSystem    — threat and invader resolution
    ///     └── StrikeSystem    — rebellion tracking and sabotage
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        // ==================================================================
        // Singleton
        // ==================================================================

        private static GameManager _instance;

        /// <summary>
        /// Global access point. Non-null after Awake completes on the first
        /// GameManager instance loaded; null before that point.
        /// </summary>
        public static GameManager Instance => _instance;

        // ==================================================================
        // Inspector
        // ==================================================================

        [SerializeField]
        [Tooltip("Balance settings. Leave null to use code defaults.")]
        private GameSettings _settings;

        [Header("Prefabs")]
        [SerializeField]
        [Tooltip("Prefab with a UnitController component — instantiated per spawned unit.")]
        private GameObject _unitPrefab;

        [SerializeField]
        [Tooltip("Room prefabs indexed by (int)RoomType. Gaps are allowed (null entries = no prefab).")]
        private GameObject[] _roomPrefabs;

        [SerializeField]
        [Tooltip("RoomTypeSO definitions indexed by (int)RoomType — must match _roomPrefabs order.")]
        private RoomTypeSO[] _roomDefinitions;

        [Header("Initial Spawn")]
        [SerializeField]
        [Tooltip("How many units to spawn when no save file is found.")]
        private int _initialUnitCount = 3;

        [SerializeField]
        [Tooltip("World positions used for the initial unit placement.")]
        private Transform[] _initialSpawnPoints;

        // ==================================================================
        // Simulation systems
        // ==================================================================

        private SlapSystem     _slapSystem;
        private MoraleSystem   _moraleSystem;
        private TaskSystem     _taskSystem;
        private ResourceSystem _resourceSystem;
        private CombatSystem   _combatSystem;
        private StrikeSystem   _strikeSystem;

        // ==================================================================
        // Runtime lists
        // ==================================================================

        private readonly List<UnitData> _allUnits = new List<UnitData>(32);
        private readonly List<RoomData> _allRooms = new List<RoomData>(16);

        // Visual controller lookups (data-model Id → scene controller)
        private readonly Dictionary<string, UnitController> _unitControllers
            = new Dictionary<string, UnitController>(StringComparer.Ordinal);
        private readonly Dictionary<string, RoomController> _roomControllers
            = new Dictionary<string, RoomController>(StringComparer.Ordinal);

        // ==================================================================
        // Game clock & state
        // ==================================================================

        private float _gameTime;
        private bool  _isPaused;

        // ==================================================================
        // Public read-only accessors
        // ==================================================================

        public bool              IsPaused        => _isPaused;
        public float             GameTime        => _gameTime;
        public GameSettings      Settings        => _settings;
        public ResourceSystem    ResourceSystem  => _resourceSystem;

        public IReadOnlyList<UnitData> AllUnits  => _allUnits;
        public IReadOnlyList<RoomData> AllRooms  => _allRooms;

        // Retained from pre-existing stub — lets InvaderController and similar
        // systems query the scene graph without expensive FindObjectOfType calls.
        public CombatSystem CombatSystem => _combatSystem;

        // ==================================================================
        // Unity lifecycle — Awake
        // ==================================================================

        private void Awake()
        {
            // Singleton enforcement with DDOL.
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[GameManager] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitialiseSystems();
            SubscribeToEventBus();
        }

        // ==================================================================
        // Unity lifecycle — Start
        // ==================================================================

        private void Start()
        {
            SaveData save = SaveSystem.Load();
            if (save != null)
                LoadFromSaveData(save);
            else
                StartNewGame();
        }

        // ==================================================================
        // Unity lifecycle — Update
        // ==================================================================

        private void Update()
        {
            if (_isPaused) return;

            float dt = Time.deltaTime;
            _gameTime += dt;

            // Tick all simulation systems in deterministic order.
            // Argument lists must match the actual system constructor / method signatures.
            _moraleSystem.Tick(_allUnits, dt, _settings);
            _taskSystem.AssignTasks(_allUnits, _allRooms, _settings);
            _resourceSystem.Tick(_allUnits, _allRooms, dt);
            _combatSystem.Tick(_allUnits, dt);
            _strikeSystem.Tick(_allUnits, _gameTime, dt, _resourceSystem, _allRooms);
            _slapSystem.Tick(dt);

            CheckAngerRebellionThresholds();
        }

        // ==================================================================
        // Unity lifecycle — OnDestroy
        // ==================================================================

        private void OnDestroy()
        {
            UnsubscribeFromEventBus();
            if (_instance == this)
                _instance = null;
        }

        // ==================================================================
        // Registry — used by the pre-existing UnitController / RoomController stubs
        // ==================================================================

        /// <summary>Called by <see cref="UnitController"/> in its OnEnable.</summary>
        public void RegisterUnit(UnitController controller)
        {
            if (controller == null) return;
            if (controller.Data != null)
                _unitControllers[controller.Data.Id] = controller;
        }

        /// <summary>Called by <see cref="UnitController"/> in its OnDisable / OnDestroy.</summary>
        public void UnregisterUnit(UnitController controller)
        {
            if (controller?.Data != null)
                _unitControllers.Remove(controller.Data.Id);
        }

        /// <summary>Called by <see cref="RoomController"/> when it initialises.</summary>
        public void RegisterRoom(RoomController controller)
        {
            if (controller == null) return;
            if (controller.Data != null)
                _roomControllers[controller.Data.Id] = controller;
        }

        /// <summary>Called by <see cref="RoomController"/> when it is destroyed.</summary>
        public void UnregisterRoom(RoomController controller)
        {
            if (controller?.Data != null)
                _roomControllers.Remove(controller.Data.Id);
        }

        // ==================================================================
        // Public API — units
        // ==================================================================

        /// <summary>
        /// Creates a new <see cref="UnitData"/>, instantiates its visual
        /// <see cref="UnitController"/> prefab, and registers both.
        /// </summary>
        /// <returns>The new unit's data model, or null if the unit cap is reached.</returns>
        public UnitData SpawnUnit(string unitName, UnitRole role, PersonalityTrait personality, Vector3 worldPosition)
        {
            if (_allUnits.Count >= _settings.MaxUnitCount)
            {
                Debug.LogWarning($"[GameManager] Unit cap ({_settings.MaxUnitCount}) reached — spawn skipped.");
                return null;
            }

            UnitData data = UnitData.Create(unitName, role, personality);
            _allUnits.Add(data);

            if (_unitPrefab != null)
            {
                GameObject go = Instantiate(_unitPrefab, worldPosition, Quaternion.identity);
                go.name = $"Unit_{data.Name}_{data.Id[..8]}";

                UnitController controller = go.GetComponent<UnitController>();
                if (controller != null)
                {
                    controller.Initialize(data);
                    _unitControllers[data.Id] = controller;
                }
                else
                {
                    Debug.LogError("[GameManager] _unitPrefab is missing a UnitController component.");
                }
            }
            else
            {
                Debug.LogWarning("[GameManager] _unitPrefab is not assigned — unit spawned without a visual.");
            }

            Debug.Log($"[GameManager] Spawned {data}");
            return data;
        }

        /// <summary>
        /// Removes <paramref name="unit"/> from the simulation and destroys its
        /// visual controller. Safe to call from EventBus handlers.
        /// </summary>
        public void DespawnUnit(UnitData unit)
        {
            if (unit == null) return;

            _allUnits.Remove(unit);

            if (_unitControllers.TryGetValue(unit.Id, out UnitController controller))
            {
                _unitControllers.Remove(unit.Id);
                if (controller != null)
                    controller.Die();
            }
        }

        /// <summary>Returns the <see cref="UnitController"/> for <paramref name="unit"/>, or null.</summary>
        public UnitController GetController(UnitData unit)
            => unit != null && _unitControllers.TryGetValue(unit.Id, out var ctrl) ? ctrl : null;

        /// <summary>Filters the unit list by the given <see cref="UnitState"/>.</summary>
        public IEnumerable<UnitData> GetUnitsInState(UnitState state)
            => _allUnits.Where(u => u.State == state);

        /// <summary>Returns the nearest alive <see cref="UnitController"/> in Fighting state, or null.</summary>
        public UnitController GetNearestFightingUnit(Vector3 from)
        {
            UnitController best     = null;
            float          bestDist = float.MaxValue;

            foreach (var kv in _unitControllers)
            {
                UnitController ctrl = kv.Value;
                if (ctrl == null || !ctrl.IsAlive) continue;
                if (ctrl.Data.State != UnitState.Fighting) continue;

                float dist = (ctrl.transform.position - from).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = ctrl;
                }
            }

            return best;
        }

        /// <summary>Returns a read-only snapshot of all registered <see cref="UnitController"/> instances.</summary>
        public IReadOnlyList<UnitController> GetAllUnits()
        {
            var list = new List<UnitController>(_unitControllers.Count);
            foreach (var v in _unitControllers.Values)
                if (v != null) list.Add(v);
            return list;
        }

        // ==================================================================
        // Public API — rooms
        // ==================================================================

        /// <summary>
        /// Creates a new <see cref="RoomData"/>, instantiates its prefab, and registers both.
        /// </summary>
        /// <returns>The new room's data model, or null if the room cap is reached.</returns>
        public RoomData BuildRoom(RoomType type, Vector3Int gridPosition)
        {
            if (_allRooms.Count >= _settings.MaxRoomCount)
            {
                Debug.LogWarning($"[GameManager] Room cap ({_settings.MaxRoomCount}) reached — build skipped.");
                return null;
            }

            RoomData data = RoomData.Create(
                name:           type.ToString(),
                type:           type,
                capacity:       4,
                productionRate: 1f
            );

            _allRooms.Add(data);

            int        roomIndex = (int)type;
            GameObject prefab    = (_roomPrefabs != null && roomIndex < _roomPrefabs.Length)
                                   ? _roomPrefabs[roomIndex]
                                   : null;

            if (prefab != null)
            {
                Vector3    worldPos = new Vector3(gridPosition.x, gridPosition.y, gridPosition.z);
                GameObject go       = Instantiate(prefab, worldPos, Quaternion.identity);
                go.name             = $"Room_{type}_{data.Id[..8]}";

                RoomController controller = go.GetComponent<RoomController>();
                if (controller != null)
                {
                    RoomTypeSO definition = (_roomDefinitions != null && roomIndex < _roomDefinitions.Length)
                                           ? _roomDefinitions[roomIndex]
                                           : null;
                    controller.Initialize(data, definition);
                    _roomControllers[data.Id] = controller;
                }
                else
                    Debug.LogError("[GameManager] Room prefab is missing a RoomController component.");
            }
            else
            {
                Debug.LogWarning($"[GameManager] No prefab for RoomType {type} (index {roomIndex}).");
            }

            EventBus.Global.Publish(new RoomBuiltEvent(data));
            Debug.Log($"[GameManager] Built {data}");
            return data;
        }

        /// <summary>Returns the nearest operational <see cref="RoomController"/>, or null.</summary>
        public RoomController GetNearestRoom(Vector3 from)
        {
            RoomController best     = null;
            float          bestDist = float.MaxValue;

            foreach (var kv in _roomControllers)
            {
                RoomController ctrl = kv.Value;
                if (ctrl == null) continue;

                // Operational check via the data model.
                if (ctrl.Data != null && !ctrl.Data.IsOperational) continue;

                float dist = (ctrl.transform.position - from).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best     = ctrl;
                }
            }

            return best;
        }

        /// <summary>Returns a read-only snapshot of all registered <see cref="RoomController"/> instances.</summary>
        public IReadOnlyList<RoomController> GetAllRooms()
        {
            var list = new List<RoomController>(_roomControllers.Count);
            foreach (var v in _roomControllers.Values)
                if (v != null) list.Add(v);
            return list;
        }

        // ==================================================================
        // Public API — slap
        // ==================================================================

        /// <summary>
        /// Evaluates a slap of the given <paramref name="force"/> on <paramref name="unit"/>,
        /// applies stat changes, handles state transitions, fires the global event,
        /// and triggers the visual controller reaction.
        ///
        /// <paramref name="force"/> is in the 0–10 range produced by <see cref="SlapController"/>.
        /// </summary>
        public void SlapUnit(UnitData unit, float force)
        {
            if (unit == null || !unit.IsAlive) return;

            SlapResult result = _slapSystem.ProcessSlap(unit, force, _settings);

            // Apply temporary productivity modifier (clamped to reasonable range).
            unit.Productivity = Mathf.Clamp(unit.Productivity + result.ProductivityModifier, 0.1f, 3f);

            // Handle hard-state transitions produced by the slap.
            switch (result.Response)
            {
                case SlapResponse.Rebel:
                    if (unit.State != UnitState.Rebelling)
                        _strikeSystem.StartStrike(unit, StrikeType.FullRebellion, _gameTime);
                    break;

                case SlapResponse.Quit:
                    // Unit quits immediately — remove from simulation.
                    unit.State = UnitState.Dead;
                    DespawnUnit(unit);
                    return; // controller destroyed; do not forward to visual
            }

            // Publish to global bus for UI, audio, achievements, etc.
            EventBus.Global.Publish(new UnitSlappedEvent(unit, force, result.Response));

            // Forward to visual controller for animation / FX.
            if (_unitControllers.TryGetValue(unit.Id, out UnitController controller))
                controller.PlaySlapReaction(result.Response, force);
        }

        // ==================================================================
        // Public API — pause
        // ==================================================================

        /// <summary>
        /// Toggles the simulation pause state. Adjusts <see cref="Time.timeScale"/>
        /// so Unity physics and animations also freeze.
        /// </summary>
        public void TogglePause()
        {
            _isPaused      = !_isPaused;
            Time.timeScale = _isPaused ? 0f : 1f;
            Debug.Log($"[GameManager] {(_isPaused ? "Paused" : "Resumed")}.");
        }

        // ==================================================================
        // Public API — save / load
        // ==================================================================

        /// <summary>Serialises current game state and writes it to disk.</summary>
        public void SaveGame(string slot = "default")
        {
            SaveData data = GetSaveData();
            data.SaveSlot = slot;
            SaveSystem.Save(data, slot);
        }

        /// <summary>Builds a complete <see cref="SaveData"/> snapshot of the current state.</summary>
        public SaveData GetSaveData()
        {
            var data = new SaveData
            {
                GameTime     = _gameTime,
                Gold         = _resourceSystem.Get(ResourceType.Gold),
                Essence      = _resourceSystem.Get(ResourceType.Essence),
                FearResource = _resourceSystem.Get(ResourceType.Fear),
            };

            foreach (UnitData unit in _allUnits)
            {
                Vector3 pos = Vector3.zero;
                if (_unitControllers.TryGetValue(unit.Id, out UnitController ctrl) && ctrl != null)
                    pos = ctrl.transform.position;

                data.Units.Add(new UnitSaveData
                {
                    Id             = unit.Id,
                    Name           = unit.Name,
                    Role           = unit.Role,
                    Personality    = unit.Personality,
                    State          = unit.State,
                    AssignedRoomId = unit.AssignedRoomId,
                    CurrentTask    = unit.CurrentTask,
                    Health         = unit.Health,
                    MaxHealth      = unit.MaxHealth,
                    Hunger         = unit.Hunger,
                    Fatigue        = unit.Fatigue,
                    Morale         = unit.Morale,
                    Fear           = unit.Fear,
                    Anger          = unit.Anger,
                    Loyalty        = unit.Loyalty,
                    Productivity   = unit.Productivity,
                    Attack         = unit.Attack,
                    // Defense omitted — field not present in current UnitData; restore when added.
                    PosX           = pos.x,
                    PosY           = pos.y,
                    PosZ           = pos.z,
                });
            }

            foreach (RoomData room in _allRooms)
            {
                Vector3 pos = Vector3.zero;
                if (_roomControllers.TryGetValue(room.Id, out RoomController rctrl) && rctrl != null)
                    pos = rctrl.transform.position;

                data.Rooms.Add(new RoomSaveData
                {
                    Id                 = room.Id,
                    Name               = room.Name,
                    Type               = room.Type,
                    Capacity           = room.Capacity,
                    ProductionRate     = room.ProductionRate,
                    BaseProductionRate = room.BaseProductionRate,
                    Health             = room.Health,
                    MaxHealth          = room.MaxHealth,
                    AssignedUnitIds    = new List<string>(room.AssignedUnitIds),
                    GridX              = Mathf.RoundToInt(pos.x),
                    GridY              = Mathf.RoundToInt(pos.y),
                    GridZ              = Mathf.RoundToInt(pos.z),
                });
            }

            return data;
        }

        /// <summary>
        /// Restores game state from a <see cref="SaveData"/> snapshot.
        /// Destroys all current visual controllers before restoring.
        /// </summary>
        public void LoadFromSaveData(SaveData saveData)
        {
            if (saveData == null) throw new ArgumentNullException(nameof(saveData));

            // Destroy existing scene objects.
            foreach (var ctrl in _unitControllers.Values)
                if (ctrl != null) Destroy(ctrl.gameObject);
            foreach (var ctrl in _roomControllers.Values)
                if (ctrl != null) Destroy(ctrl.gameObject);

            _allUnits.Clear();
            _allRooms.Clear();
            _unitControllers.Clear();
            _roomControllers.Clear();

            _gameTime = saveData.GameTime;

            // Restore units. Note: the current UnitData constructor generates a new GUID —
            // ID restoration requires an id-preserving factory to be added to UnitData.
            // AssignedRoomId cross-references are re-established by Id after all units are loaded.
            foreach (UnitSaveData usd in saveData.Units)
            {
                var unit = new UnitData(usd.Name, usd.Role, usd.Personality)
                {
                    State          = usd.State,
                    AssignedRoomId = usd.AssignedRoomId,
                    CurrentTask    = usd.CurrentTask,
                    Health         = usd.Health,
                    MaxHealth      = usd.MaxHealth,
                    Hunger         = usd.Hunger,
                    Fatigue        = usd.Fatigue,
                    Morale         = usd.Morale,
                    Fear           = usd.Fear,
                    Anger          = usd.Anger,
                    Loyalty        = usd.Loyalty,
                    Productivity   = usd.Productivity,
                    Attack         = usd.Attack,
                    // Defense is not present in the current UnitData model;
                    // restore when the field is added.
                };

                _allUnits.Add(unit);

                if (_unitPrefab != null)
                {
                    Vector3    pos = new Vector3(usd.PosX, usd.PosY, usd.PosZ);
                    GameObject go  = Instantiate(_unitPrefab, pos, Quaternion.identity);
                    go.name        = $"Unit_{unit.Name}_{unit.Id[..8]}";

                    UnitController controller = go.GetComponent<UnitController>();
                    if (controller != null)
                    {
                        controller.Initialize(unit);
                        _unitControllers[unit.Id] = controller;
                    }
                }
            }

            // Restore rooms.
            foreach (RoomSaveData rsd in saveData.Rooms)
            {
                var room = new RoomData(
                    rsd.Id, rsd.Name, rsd.Type, rsd.Capacity, rsd.ProductionRate, rsd.MaxHealth)
                {
                    Health = rsd.Health,
                };

                foreach (string uid in rsd.AssignedUnitIds)
                    room.AssignedUnitIds.Add(uid);

                _allRooms.Add(room);

                int        roomIndex = (int)rsd.Type;
                GameObject prefab    = (_roomPrefabs != null && roomIndex < _roomPrefabs.Length)
                                       ? _roomPrefabs[roomIndex]
                                       : null;

                if (prefab != null)
                {
                    Vector3    pos = new Vector3(rsd.GridX, rsd.GridY, rsd.GridZ);
                    GameObject go  = Instantiate(prefab, pos, Quaternion.identity);
                    go.name        = $"Room_{rsd.Type}_{rsd.Id[..8]}";

                    RoomController controller = go.GetComponent<RoomController>();
                    if (controller != null)
                        _roomControllers[room.Id] = controller;
                }
            }

            // Restore economy totals.
            _resourceSystem.Add(ResourceType.Gold,    saveData.Gold);
            _resourceSystem.Add(ResourceType.Essence, saveData.Essence);
            _resourceSystem.Add(ResourceType.Fear,    saveData.FearResource);

            Debug.Log($"[GameManager] Loaded save. GameTime={_gameTime:F1}s, " +
                      $"Units={_allUnits.Count}, Rooms={_allRooms.Count}");
        }

        // ==================================================================
        // EventBus handlers
        // ==================================================================

        private void OnThreatSpawned(ThreatSpawnedEvent evt)
        {
            Debug.Log($"[GameManager] Threat inbound: {evt.Level}, {evt.InvaderCount} invaders.");
            _combatSystem.BeginThreat(evt.Level, evt.InvaderCount);
        }

        private void OnThreatResolved(ThreatResolvedEvent evt)
        {
            Debug.Log($"[GameManager] Threat resolved. Player won: {evt.PlayerWon}");

            // Return all fighting units to idle so they resume work.
            foreach (UnitData unit in _allUnits)
            {
                if (unit.State == UnitState.Fighting)
                    unit.State = UnitState.Idle;
            }
        }

        private void OnUnitDied(UnitDiedEvent evt)
        {
            Debug.Log($"[GameManager] {evt.Unit.Name} has died.");
            DespawnUnit(evt.Unit);
        }

        // ==================================================================
        // Private helpers
        // ==================================================================

        private void InitialiseSystems()
        {
            if (_settings == null)
            {
                Debug.LogWarning("[GameManager] GameSettings not assigned in Inspector — using defaults.");
                _settings = GameSettings.Default();
            }

            // Constructor signatures must match the actual system implementations.
            // SlapSystem and MoraleSystem take an EventBus for internal event dispatch.
            _slapSystem     = new SlapSystem(EventBus.Global);
            _moraleSystem   = new MoraleSystem(EventBus.Global);
            _taskSystem     = new TaskSystem(_settings);
            _resourceSystem = new ResourceSystem();
            _combatSystem   = new CombatSystem(_settings);
            _strikeSystem   = new StrikeSystem(_settings);
        }

        private void SubscribeToEventBus()
        {
            EventBus.Global.Subscribe<ThreatSpawnedEvent>(OnThreatSpawned);
            EventBus.Global.Subscribe<ThreatResolvedEvent>(OnThreatResolved);
            EventBus.Global.Subscribe<UnitDiedEvent>(OnUnitDied);
        }

        private void UnsubscribeFromEventBus()
        {
            EventBus.Global.Unsubscribe<ThreatSpawnedEvent>(OnThreatSpawned);
            EventBus.Global.Unsubscribe<ThreatResolvedEvent>(OnThreatResolved);
            EventBus.Global.Unsubscribe<UnitDiedEvent>(OnUnitDied);
        }

        private void StartNewGame()
        {
            SpawnInitialUnits();
            BuildRoom(RoomType.ProductionChamber, Vector3Int.zero);
        }

        private void SpawnInitialUnits()
        {
            string[]         names = { "Grunt", "Digger", "Thug", "Lurker", "Wretch" };
            PersonalityTrait[] personalities = (PersonalityTrait[])Enum.GetValues(typeof(PersonalityTrait));

            for (int i = 0; i < _initialUnitCount; i++)
            {
                string           unitName    = i < names.Length ? names[i] : $"Minion_{i}";
                PersonalityTrait personality = personalities[i % personalities.Length];
                Vector3          pos         = (_initialSpawnPoints != null && i < _initialSpawnPoints.Length)
                                               ? _initialSpawnPoints[i].position
                                               : new Vector3(i * 2f, 0f, 0f);

                SpawnUnit(unitName, UnitRole.Worker, personality, pos);
            }
        }

        /// <summary>
        /// Checks all living units for anger at or above the rebellion threshold
        /// and triggers a rebellion if the StrikeSystem hasn't already done so.
        /// </summary>
        private void CheckAngerRebellionThresholds()
        {
            foreach (UnitData unit in _allUnits)
            {
                if (!unit.IsAlive) continue;
                if (unit.State == UnitState.Rebelling || unit.State == UnitState.Dead) continue;

                if (unit.Anger >= _settings.AngerRebellionThreshold)
                    _strikeSystem.StartStrike(unit, StrikeType.FullRebellion, _gameTime);
            }
        }

        // ==================================================================
        // Editor / debug helpers
        // ==================================================================

        [ContextMenu("Spawn Test Threat")]
        private void SpawnTestThreat()
        {
            Debug.Log("[GameManager] Spawning test threat: Skirmish / 3 invaders.");
            EventBus.Global.Publish(new ThreatSpawnedEvent(ThreatLevel.Skirmish, 3));
        }

        [ContextMenu("Save Game (default slot)")]
        private void DebugSave() => SaveGame();

        [ContextMenu("Toggle Pause")]
        private void DebugTogglePause() => TogglePause();

        [ContextMenu("Log Unit States")]
        private void DebugLogUnitStates()
        {
            foreach (UnitData u in _allUnits)
                Debug.Log(u.ToString());
        }

        [ContextMenu("Log Resource Summary")]
        private void DebugLogResources()
            => Debug.Log(_resourceSystem?.GetResourceSummary() ?? "ResourceSystem not initialised.");
    }
}

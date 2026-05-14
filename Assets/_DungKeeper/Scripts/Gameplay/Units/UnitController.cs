using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace DungKeeper
{
    /// <summary>
    /// Visual Unity representation of a single dungeon unit.
    ///
    /// Responsibilities:
    ///   • Sync animator booleans with <see cref="UnitData.State"/> every frame.
    ///   • Drive <see cref="NavMeshAgent"/> movement based on the current state.
    ///   • Update the floating status-bar HUD (fear, morale, anger).
    ///   • React to <see cref="UnitSlappedEvent"/> with animation, particles,
    ///     material flash, positional shake, and a camera shake request.
    ///   • Expose <see cref="Die"/> for ordered destruction with animation delay.
    ///   • Register / unregister with <see cref="GameManager"/> on Enable / Disable.
    ///
    /// Design contract:
    ///   This class never mutates <see cref="UnitData"/> directly.
    ///   All simulation changes go through the pure-C# systems
    ///   (SlapSystem, MoraleSystem, TaskSystem, …) owned by GameManager.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class UnitController : MonoBehaviour
    {
        // ==================================================================
        // Inspector — animation
        // ==================================================================

        [Header("Animation")]
        [SerializeField] private Animator _animator;

        // Pre-hashed animator parameter IDs — cheaper than string lookups per frame.
        private static readonly int HashIsWorking   = Animator.StringToHash("IsWorking");
        private static readonly int HashIsFleeing   = Animator.StringToHash("IsFleeing");
        private static readonly int HashIsRebelling = Animator.StringToHash("IsRebelling");
        private static readonly int HashIsFighting  = Animator.StringToHash("IsFighting");
        private static readonly int HashDead        = Animator.StringToHash("Dead");
        private static readonly int HashSlap        = Animator.StringToHash("SlapReact");
        private static readonly int HashExcited     = Animator.StringToHash("Excited");
        private static readonly int HashAngry       = Animator.StringToHash("Angry");

        // ==================================================================
        // Inspector — rendering
        // ==================================================================

        [Header("Rendering")]
        [SerializeField] private SkinnedMeshRenderer _meshRenderer;

        [SerializeField]
        [Tooltip("Body flash colour on a light slap or SpeedUp response.")]
        private Color _lightSlapFlashColor = new Color(1f, 0.8f, 0.2f, 1f);   // warm gold

        [SerializeField]
        [Tooltip("Body flash colour on a heavy slap, Rebel, or Quit response.")]
        private Color _heavySlapFlashColor = new Color(1f, 0.1f, 0.1f, 1f);   // angry red

        [SerializeField]
        [Tooltip("Seconds the body colour stays at flash colour before restoring.")]
        private float _flashDuration = 0.15f;

        [Header("State Colours (fallback body tint)")]
        [SerializeField] private Color _workingColor  = Color.green;
        [SerializeField] private Color _fightingColor = Color.red;
        [SerializeField] private Color _fleeingColor  = Color.yellow;
        [SerializeField] private Color _deadColor     = Color.grey;

        // ==================================================================
        // Inspector — effects
        // ==================================================================

        [Header("Effects")]
        [SerializeField]
        [Tooltip("Particle prefab spawned at impact on each slap.")]
        private GameObject _slapHitEffect;

        // ==================================================================
        // Inspector — status HUD
        // ==================================================================

        [Header("Status HUD")]
        [SerializeField] private Canvas _statusCanvas;
        [SerializeField] private Slider _fearBar;
        [SerializeField] private Slider _moraleBar;
        [SerializeField] private Slider _angerBar;

        [SerializeField]
        [Tooltip("When true the status canvas is always visible. " +
                 "When false it is only shown while the cursor hovers this unit.")]
        private bool _alwaysShowStatus;

        // ==================================================================
        // Inspector — slap shake
        // ==================================================================

        [Header("Slap Shake")]
        [SerializeField] private float _slapShakeIntensity = 0.12f;
        [SerializeField] private float _slapShakeDuration  = 0.25f;

        // ==================================================================
        // Inspector — movement
        // ==================================================================

        [Header("Movement")]
        [SerializeField]
        [Tooltip("Target point used when this unit enters the Fleeing state. " +
                 "If null, the unit flees along its forward axis.")]
        private Transform _fleePoint;

        [SerializeField]
        [Tooltip("Radius (world units) within which a rebelling unit chooses patrol waypoints.")]
        private float _rebellionPatrolRadius = 8f;

        // ==================================================================
        // Runtime data
        // ==================================================================

        /// <summary>Simulation data model. Assigned once via <see cref="Initialize"/>.</summary>
        public UnitData Data { get; private set; }

        /// <summary>True while the unit's data reports it as alive.</summary>
        public bool IsAlive => Data != null && Data.IsAlive;

        private NavMeshAgent _agent;
        private Vector3      _originalLocalPosition;
        private UnitState    _lastKnownState = UnitState.Idle;

        // Active coroutine handles — stored so they can be cancelled on overlap.
        private Coroutine _shakeCoroutine;
        private Coroutine _flashCoroutine;

        // Rebellion patrol state.
        private float   _patrolTimer;

        // Material property block — avoids per-frame material instantiation.
        private MaterialPropertyBlock _propBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private const float DeathDestroyDelaySec = 2.5f;

        // ==================================================================
        // Unity lifecycle — Awake
        // ==================================================================

        private void Awake()
        {
            _agent                 = GetComponent<NavMeshAgent>();
            _originalLocalPosition = transform.localPosition;
            _propBlock             = new MaterialPropertyBlock();

            if (_statusCanvas != null)
                _statusCanvas.gameObject.SetActive(_alwaysShowStatus);
        }

        // ==================================================================
        // Unity lifecycle — OnEnable / OnDisable
        // ==================================================================

        private void OnEnable()
        {
            EventBus.Global.Subscribe<UnitSlappedEvent>(OnUnitSlapped);

            if (GameManager.Instance != null)
                GameManager.Instance.RegisterUnit(this);
        }

        private void OnDisable()
        {
            EventBus.Global.Unsubscribe<UnitSlappedEvent>(OnUnitSlapped);

            if (GameManager.Instance != null)
                GameManager.Instance.UnregisterUnit(this);
        }

        // ==================================================================
        // Unity lifecycle — Update
        // ==================================================================

        private void Update()
        {
            if (Data == null || !Data.IsAlive) return;

            SyncAnimatorState();
            UpdateStatusBars();
            DriveMovement();
        }

        // ==================================================================
        // Initialisation
        // ==================================================================

        /// <summary>
        /// Binds this controller to its data model and names the GameObject.
        /// Called by <see cref="GameManager"/> immediately after instantiation.
        /// </summary>
        public void Initialize(UnitData data)
        {
            Data = data ?? throw new System.ArgumentNullException(nameof(data));
            name = $"Unit_{data.Name}_{data.Id[..8]}";
        }

        // ==================================================================
        // Public API — slap reaction
        // ==================================================================

        /// <summary>
        /// Plays the full visual reaction to a slap: animator trigger, impact particles,
        /// material colour flash, positional shake, and a camera shake request.
        /// May be called directly by <see cref="GameManager.SlapUnit"/> or via
        /// the <see cref="UnitSlappedEvent"/> subscription.
        /// </summary>
        /// <param name="response">Behavioural outcome computed by SlapSystem.</param>
        /// <param name="force">Raw force (0–10) from SlapController.</param>
        public void PlaySlapReaction(SlapResponse response, float force)
        {
            // --- Animator trigger ---
            if (_animator != null)
            {
                switch (response)
                {
                    case SlapResponse.BecomeExcited:
                        _animator.SetTrigger(HashExcited);
                        break;
                    case SlapResponse.BecomeAngry:
                    case SlapResponse.Rebel:
                        _animator.SetTrigger(HashAngry);
                        break;
                    default:
                        _animator.SetTrigger(HashSlap);
                        break;
                }
            }

            // --- Spawn impact particle at chest height ---
            if (_slapHitEffect != null)
            {
                GameObject fx = Instantiate(
                    _slapHitEffect,
                    transform.position + Vector3.up * 1.2f,
                    Quaternion.identity);
                Destroy(fx, 3f);
            }

            // --- Material flash (heavy vs light) ---
            bool   heavySlap  = response == SlapResponse.Rebel
                              || response == SlapResponse.Quit
                              || force >= 7f;
            Color  flashColor = heavySlap ? _heavySlapFlashColor : _lightSlapFlashColor;

            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(MaterialFlashRoutine(flashColor, _flashDuration));

            // --- Positional shake scaled by force ---
            float normForce      = Mathf.Clamp01(force / 10f);
            float shakeIntensity = _slapShakeIntensity * normForce;

            if (_shakeCoroutine != null) StopCoroutine(_shakeCoroutine);
            _shakeCoroutine = StartCoroutine(SlapShakeRoutine(shakeIntensity, _slapShakeDuration));

            // --- Camera shake — intensity scales with normalised force ---
            CameraShake.Shake(Mathf.Clamp01(force / 10f));
        }

        // ==================================================================
        // Public API — movement
        // ==================================================================

        /// <summary>Commands the <see cref="NavMeshAgent"/> to path toward <paramref name="target"/>.</summary>
        public void SetMoveTarget(Vector3 target)
        {
            if (_agent != null && _agent.isOnNavMesh)
                _agent.SetDestination(target);
        }

        // ==================================================================
        // Public API — death
        // ==================================================================

        /// <summary>
        /// Plays the death animation, disables the agent and colliders,
        /// then destroys the GameObject after a short delay.
        /// Called by <see cref="GameManager.DespawnUnit"/>.
        /// </summary>
        public void Die()
        {
            if (_animator != null)
                _animator.SetTrigger(HashDead);

            if (_agent != null)
                _agent.enabled = false;

            foreach (Collider col in GetComponentsInChildren<Collider>())
                col.enabled = false;

            Destroy(gameObject, DeathDestroyDelaySec);
        }

        // ==================================================================
        // Public API — combat (for InvaderController / CombatSystem)
        // ==================================================================

        /// <summary>
        /// Applies damage to the unit's health and fires <see cref="UnitDiedEvent"/>
        /// when health reaches zero. Refreshes body colour.
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (Data == null || !Data.IsAlive) return;

            Data.Health = Mathf.Max(0f, Data.Health - Mathf.Abs(amount));

            RefreshBodyColor();

            if (Data.Health <= 0f)
            {
                Data.State = UnitState.Dead;
                EventBus.Global.Publish(new UnitDiedEvent(Data));
            }
        }

        // ==================================================================
        // Public API — status canvas visibility
        // ==================================================================

        /// <summary>
        /// Shows or hides the floating status HUD.
        /// Called from <see cref="SlapController"/> on hover enter / exit.
        /// </summary>
        public void SetStatusVisibility(bool visible)
        {
            if (_statusCanvas != null)
                _statusCanvas.gameObject.SetActive(_alwaysShowStatus || visible);
        }

        // ==================================================================
        // Coroutines
        // ==================================================================

        /// <summary>
        /// Shakes the unit's local position with an exponentially decaying envelope
        /// using random offsets.
        /// </summary>
        private IEnumerator SlapShakeRoutine(float intensity, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float envelope = 1f - Mathf.Clamp01(elapsed / duration);

                Vector3 offset = Random.insideUnitSphere * (intensity * envelope);
                offset.z = 0f; // preserve z-depth in 2.5D setups
                transform.localPosition = _originalLocalPosition + offset;

                yield return null;
            }

            transform.localPosition = _originalLocalPosition;
            _shakeCoroutine = null;
        }

        /// <summary>
        /// Briefly sets the body mesh colour to <paramref name="flashColor"/>,
        /// then restores the state-driven colour after <paramref name="flashDuration"/> seconds.
        /// Uses a <see cref="MaterialPropertyBlock"/> to avoid material instantiation.
        /// </summary>
        private IEnumerator MaterialFlashRoutine(Color flashColor, float flashDuration)
        {
            if (_meshRenderer == null) yield break;

            _meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, flashColor);
            _meshRenderer.SetPropertyBlock(_propBlock);

            yield return new WaitForSeconds(flashDuration);

            // Restore the state-driven body tint.
            RefreshBodyColor();
            _flashCoroutine = null;
        }

        // ==================================================================
        // Private — animator sync
        // ==================================================================

        private void SyncAnimatorState()
        {
            if (_animator == null) return;

            UnitState state = Data.State;
            if (state == _lastKnownState) return;

            _animator.SetBool(HashIsWorking,   state == UnitState.Working);
            _animator.SetBool(HashIsFleeing,   state == UnitState.Fleeing);
            _animator.SetBool(HashIsRebelling, state == UnitState.Rebelling);
            _animator.SetBool(HashIsFighting,  state == UnitState.Fighting);
            _animator.SetBool(HashDead,        state == UnitState.Dead);

            _lastKnownState = state;
        }

        // ==================================================================
        // Private — HUD
        // ==================================================================

        private void UpdateStatusBars()
        {
            if (_fearBar   != null) _fearBar.value   = Data.Fear   / 100f;
            if (_moraleBar != null) _moraleBar.value = Data.Morale / 100f;
            if (_angerBar  != null) _angerBar.value  = Data.Anger  / 100f;
        }

        // ==================================================================
        // Private — NavMeshAgent movement
        // ==================================================================

        private void DriveMovement()
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            switch (Data.State)
            {
                case UnitState.Working:
                    NavigateToAssignedRoom();
                    break;

                case UnitState.Fleeing:
                    NavigateToFleePoint();
                    break;

                case UnitState.Rebelling:
                    PatrolForRebellion();
                    break;

                default:
                    // Idle, resting, eating — stop pathing.
                    if (!_agent.isStopped)
                        _agent.ResetPath();
                    break;
            }
        }

        private void NavigateToAssignedRoom()
        {
            if (string.IsNullOrEmpty(Data.AssignedRoomId)) return;

            foreach (RoomController rc in GameManager.Instance.GetAllRooms())
            {
                if (rc != null && rc.Data != null && rc.Data.Id == Data.AssignedRoomId)
                {
                    SetMoveTarget(rc.transform.position);
                    return;
                }
            }
        }

        private void NavigateToFleePoint()
        {
            Vector3 target = _fleePoint != null
                ? _fleePoint.position
                : transform.position + transform.forward * 25f;

            SetMoveTarget(target);
        }

        private void PatrolForRebellion()
        {
            _patrolTimer -= Time.deltaTime;
            if (_patrolTimer > 0f) return;

            // Pick a random nearby wander point.
            Vector2 circle = Random.insideUnitCircle * _rebellionPatrolRadius;
            Vector3 target = transform.position + new Vector3(circle.x, 0f, circle.y);
            SetMoveTarget(target);
            _patrolTimer = Random.Range(2f, 5f);
        }

        // ==================================================================
        // Private — body colour
        // ==================================================================

        private void RefreshBodyColor()
        {
            if (_meshRenderer == null || Data == null) return;

            Color c = Data.State switch
            {
                UnitState.Fighting  => _fightingColor,
                UnitState.Fleeing   => _fleeingColor,
                UnitState.Dead      => _deadColor,
                _                   => _workingColor,
            };

            _meshRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId, c);
            _meshRenderer.SetPropertyBlock(_propBlock);
        }

        // ==================================================================
        // EventBus — filtered slap reaction
        // ==================================================================

        private void OnUnitSlapped(UnitSlappedEvent evt)
        {
            // Only react to events for this specific unit.
            if (Data == null || evt.Unit.Id != Data.Id) return;
            PlaySlapReaction(evt.Response, evt.Force);
        }
    }
}

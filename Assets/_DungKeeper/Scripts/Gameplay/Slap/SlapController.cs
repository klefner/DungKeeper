using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DungKeeper
{
    /// <summary>
    /// Manages the player's slap hand — the primary interaction mechanic of DungKeeper.
    ///
    /// Features:
    ///   • Raycasts from the mouse to find hovered <see cref="UnitController"/> instances.
    ///   • Highlights the hovered unit (status canvas shown on hover).
    ///   • Hold-to-charge mechanic: longer hold = higher force = stronger reaction.
    ///   • On release: launches the slap coroutine, plays audio and animation,
    ///     calls <see cref="GameManager.SlapUnit"/>, and triggers camera shake.
    ///   • Miss swing: when the mouse is pressed somewhere other than a unit,
    ///     a whoosh animation and sound play with no game-state effect.
    ///   • The hand visual follows the cursor with a configurable lag (Lerp).
    ///   • A charge indicator near the cursor shows current charge (0–100%).
    ///   • The hand is always rendered; semi-transparent when not hovering a unit.
    ///
    /// Design note:
    ///   SlapController is a first-class gameplay system, not cosmetic.
    ///   It is the sole path through which the player delivers slaps.
    ///   SlapController → GameManager.SlapUnit → SlapSystem → UnitData → EventBus.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlapController : MonoBehaviour
    {
        // ==================================================================
        // Inspector — camera & scene references
        // ==================================================================

        [Header("Scene References")]
        [SerializeField]
        [Tooltip("Main camera used for mouse-to-world raycasts. Auto-resolved if null.")]
        private Camera _mainCamera;

        // ==================================================================
        // Inspector — hand visual
        // ==================================================================

        [Header("Hand Visual")]
        [SerializeField]
        [Tooltip("3D hand mesh root. Follows the cursor with lerp lag.")]
        private GameObject _handVisual;

        [SerializeField]
        [Tooltip("Animator on the hand mesh (plays Idle, Charge, Slap, Miss triggers).")]
        private Animator _handAnimator;

        [SerializeField]
        [Tooltip("Distance from the camera at which the hand sits in world space.")]
        private float _handDepth = 8f;

        [SerializeField]
        [Tooltip("Position follow speed (lower = more lag, higher = snappier). 0 = instant.")]
        [Range(0f, 30f)]
        private float _handFollowSpeed = 12f;

        [SerializeField]
        [Tooltip("Normal alpha while hovering or after hovering (0-1).")]
        [Range(0f, 1f)]
        private float _handActiveAlpha = 1f;

        [SerializeField]
        [Tooltip("Alpha when the hand is idle and not hovering any unit (0-1).")]
        [Range(0f, 1f)]
        private float _handIdleAlpha = 0.55f;

        // Pre-hashed animator parameter IDs.
        private static readonly int HashCharge    = Animator.StringToHash("Charge");
        private static readonly int HashSlapTrig  = Animator.StringToHash("Slap");
        private static readonly int HashMissTrig  = Animator.StringToHash("Miss");

        // ==================================================================
        // Inspector — audio
        // ==================================================================

        [Header("Audio")]
        [SerializeField] private AudioSource _slapAudioSource;

        [SerializeField]
        [Tooltip("Sound bank for successful slap impacts. One is chosen at random per slap.")]
        private AudioClip[] _slapSounds;

        [SerializeField]
        [Tooltip("Sound bank for miss swings (whoosh). One is chosen at random.")]
        private AudioClip[] _missSwingSounds;

        [SerializeField]
        [Tooltip("Pitch at minimum force.")]
        private float _minPitch = 0.85f;

        [SerializeField]
        [Tooltip("Pitch at maximum force.")]
        private float _maxPitch = 1.25f;

        [SerializeField]
        [Tooltip("Additional pitch for Rebel / Quit responses (rage factor).")]
        private float _ragePitchBonus = 0.2f;

        // ==================================================================
        // Inspector — slap mechanics
        // ==================================================================

        [Header("Slap Mechanics")]
        [SerializeField]
        [Tooltip("Maximum force value passed to GameManager.SlapUnit.")]
        private float _maxSlapForce = 10f;

        [SerializeField]
        [Tooltip("Minimum force value — even an instant tap delivers this much.")]
        private float _minSlapForce = 2f;

        [SerializeField]
        [Tooltip("How fast charge accumulates per second while the button is held (0–1 / sec).")]
        private float _slapChargeRate = 0.8f;

        // ==================================================================
        // Inspector — physics
        // ==================================================================

        [Header("Raycast")]
        [SerializeField]
        [Tooltip("LayerMask for unit colliders. The raycast only hits these layers.")]
        private LayerMask _unitLayerMask = ~0;

        [SerializeField]
        [Tooltip("Maximum raycast distance in world units.")]
        private float _raycastMaxDistance = 100f;

        // ==================================================================
        // Inspector — charge UI
        // ==================================================================

        [Header("Charge Indicator")]
        [SerializeField]
        [Tooltip("Slider shown near the cursor to indicate current charge (0-1). " +
                 "Parent to a Canvas set to Screen Space – Overlay and position with code.")]
        private Slider _chargeIndicatorSlider;

        [SerializeField]
        [Tooltip("Offset from the cursor in screen pixels where the indicator appears.")]
        private Vector2 _chargeIndicatorOffset = new Vector2(30f, -30f);

        [SerializeField]
        [Tooltip("GameObject wrapping the charge indicator; hidden when charge is 0.")]
        private GameObject _chargeIndicatorRoot;

        // ==================================================================
        // Runtime state
        // ==================================================================

        private float          _currentCharge;    // 0–1
        private bool           _isCharging;
        private bool           _isSwinging;

        private UnitController _hoveredUnit;
        private UnitController _targetUnit;

        // Renderer / material for hand alpha control.
        private Renderer    _handRenderer;
        private Material    _handMaterial;
        private static readonly int AlphaProperty = Shader.PropertyToID("_BaseColor");

        // Coroutine handle for the active slap swing.
        private Coroutine _slapCoroutine;

        // World-space position the hand is lerping toward.
        private Vector3 _handTargetPosition;

        // ==================================================================
        // Unity lifecycle — Awake
        // ==================================================================

        private void Awake()
        {
            if (_mainCamera == null)
                _mainCamera = Camera.main;

            if (_handVisual != null)
            {
                _handRenderer = _handVisual.GetComponentInChildren<Renderer>();
                if (_handRenderer != null)
                    _handMaterial = _handRenderer.material;
            }

            SetHandAlpha(_handIdleAlpha);
            HideChargeIndicator();
        }

        // ==================================================================
        // Unity lifecycle — Update
        // ==================================================================

        private void Update()
        {
            UpdateHandPosition();
            UpdateHoveredUnit();
            HandleInput();
            UpdateChargeIndicator();
        }

        // ==================================================================
        // Unity lifecycle — OnDisable
        // ==================================================================

        private void OnDisable()
        {
            // Clear hover state so the unit's status canvas is hidden.
            SetHoveredUnit(null);
        }

        // ==================================================================
        // Private — hand positioning
        // ==================================================================

        private void UpdateHandPosition()
        {
            if (_mainCamera == null || _handVisual == null) return;

            // Convert mouse position to a world-space point at _handDepth in front of the camera.
            Vector3 screenPos = Input.mousePosition;
            screenPos.z = _handDepth;
            _handTargetPosition = _mainCamera.ScreenToWorldPoint(screenPos);

            // Smoothly lerp the hand toward the target — produces a satisfying lag effect.
            if (_handFollowSpeed > 0f)
                _handVisual.transform.position = Vector3.Lerp(
                    _handVisual.transform.position,
                    _handTargetPosition,
                    Time.deltaTime * _handFollowSpeed);
            else
                _handVisual.transform.position = _handTargetPosition;
        }

        // ==================================================================
        // Private — hover detection
        // ==================================================================

        private void UpdateHoveredUnit()
        {
            if (_mainCamera == null) return;

            Ray        ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            UnitController newHovered = null;
            if (Physics.Raycast(ray, out hit, _raycastMaxDistance, _unitLayerMask))
                newHovered = hit.collider.GetComponentInParent<UnitController>();

            if (newHovered != _hoveredUnit)
                SetHoveredUnit(newHovered);
        }

        private void SetHoveredUnit(UnitController unit)
        {
            // Deactivate previous hover.
            if (_hoveredUnit != null)
            {
                _hoveredUnit.SetStatusVisibility(false);
                SetHandAlpha(_handIdleAlpha);
            }

            _hoveredUnit = unit;

            // Activate new hover.
            if (_hoveredUnit != null)
            {
                _hoveredUnit.SetStatusVisibility(true);
                SetHandAlpha(_handActiveAlpha);
            }
        }

        // ==================================================================
        // Private — input handling
        // ==================================================================

        private void HandleInput()
        {
            if (_isSwinging) return;  // prevent input during active swing

            if (Input.GetMouseButtonDown(0))
                BeginPressOrCharge();

            if (Input.GetMouseButton(0) && _isCharging)
                ContinueCharge();

            if (Input.GetMouseButtonUp(0))
                ReleaseInput();
        }

        private void BeginPressOrCharge()
        {
            if (_hoveredUnit != null && _hoveredUnit.IsAlive)
            {
                // Begin charging a slap on this unit.
                _targetUnit     = _hoveredUnit;
                _isCharging     = true;
                _currentCharge  = 0f;

                if (_handAnimator != null)
                    _handAnimator.SetBool(HashCharge, true);
            }
            else
            {
                // Miss swing — clicked on empty space.
                TriggerMissSwing();
            }
        }

        private void ContinueCharge()
        {
            _currentCharge = Mathf.Clamp01(_currentCharge + _slapChargeRate * Time.deltaTime);
        }

        private void ReleaseInput()
        {
            if (_handAnimator != null)
                _handAnimator.SetBool(HashCharge, false);

            if (_isCharging && _targetUnit != null && _targetUnit.IsAlive)
            {
                float force = Mathf.Lerp(_minSlapForce, _maxSlapForce, _currentCharge);
                LaunchSlap(_targetUnit, force);
            }

            _isCharging    = false;
            _currentCharge = 0f;
            _targetUnit    = null;
            HideChargeIndicator();
        }

        // ==================================================================
        // Private — slap launch
        // ==================================================================

        private void LaunchSlap(UnitController target, float force)
        {
            if (_slapCoroutine != null)
                StopCoroutine(_slapCoroutine);

            _slapCoroutine = StartCoroutine(PerformSlap(target, force));
        }

        // ==================================================================
        // Coroutine — slap execution
        // ==================================================================

        /// <summary>
        /// Performs the full slap sequence:
        ///   1. Trigger the swing animation.
        ///   2. Lerp the hand toward the target.
        ///   3. At impact: call GameManager, play sound, trigger camera shake.
        ///   4. Retract the hand back to the cursor.
        /// </summary>
        private IEnumerator PerformSlap(UnitController target, float force)
        {
            _isSwinging = true;

            // --- Trigger swing animation ---
            if (_handAnimator != null)
                _handAnimator.SetTrigger(HashSlapTrig);

            // --- Lerp hand toward the target unit ---
            Vector3 startPos = _handVisual != null ? _handVisual.transform.position : Vector3.zero;
            Vector3 targetPos = target != null ? target.transform.position + Vector3.up * 1f : startPos;

            const float swingDuration = 0.12f;
            float elapsed = 0f;

            while (elapsed < swingDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / swingDuration);

                if (_handVisual != null)
                    _handVisual.transform.position = Vector3.Lerp(startPos, targetPos, t);

                // Early-exit if the target disappears mid-swing.
                if (target == null || !target.IsAlive)
                    break;

                yield return null;
            }

            // --- Impact ---
            if (target != null && target.IsAlive)
            {
                // Notify the simulation.
                if (GameManager.Instance != null)
                    GameManager.Instance.SlapUnit(target.Data, force);

                // Play slap audio with force-scaled pitch.
                PlaySlapAudio(force, response: null);

                // Camera shake scaled to force — redundant with UnitController.PlaySlapReaction
                // but harmless (CameraShake.Shake is additive).
                CameraShake.Shake(Mathf.Clamp01(force / _maxSlapForce));
            }

            // --- Retract hand back toward cursor ---
            const float retractDuration = 0.1f;
            elapsed = 0f;
            Vector3 impactPos = _handVisual != null ? _handVisual.transform.position : Vector3.zero;

            while (elapsed < retractDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / retractDuration);

                if (_handVisual != null)
                    _handVisual.transform.position = Vector3.Lerp(impactPos, _handTargetPosition, t);

                yield return null;
            }

            _isSwinging    = false;
            _slapCoroutine = null;
        }

        // ==================================================================
        // Private — miss swing
        // ==================================================================

        private void TriggerMissSwing()
        {
            if (_handAnimator != null)
                _handAnimator.SetTrigger(HashMissTrig);

            PlayMissAudio();
        }

        // ==================================================================
        // Private — audio
        // ==================================================================

        private void PlaySlapAudio(float force, SlapResponse? response)
        {
            if (_slapAudioSource == null) return;

            AudioClip clip = PickRandom(_slapSounds);
            if (clip == null) return;

            float normForce = Mathf.Clamp01(force / _maxSlapForce);
            float pitch     = Mathf.Lerp(_minPitch, _maxPitch, normForce);

            bool isRageResponse = response.HasValue
                && (response.Value == SlapResponse.Rebel || response.Value == SlapResponse.Quit);

            if (isRageResponse)
                pitch += _ragePitchBonus;

            _slapAudioSource.pitch = pitch;
            _slapAudioSource.PlayOneShot(clip);
        }

        private void PlayMissAudio()
        {
            if (_slapAudioSource == null) return;

            AudioClip clip = PickRandom(_missSwingSounds);
            if (clip == null) return;

            _slapAudioSource.pitch = 1f;
            _slapAudioSource.PlayOneShot(clip);
        }

        private static AudioClip PickRandom(AudioClip[] bank)
        {
            if (bank == null || bank.Length == 0) return null;
            return bank[Random.Range(0, bank.Length)];
        }

        // ==================================================================
        // Private — charge indicator UI
        // ==================================================================

        private void UpdateChargeIndicator()
        {
            if (_chargeIndicatorRoot == null) return;

            bool showIndicator = _isCharging && _currentCharge > 0f;
            _chargeIndicatorRoot.SetActive(showIndicator);

            if (!showIndicator) return;

            // Position the indicator in screen space near the cursor.
            if (_chargeIndicatorRoot.TryGetComponent<RectTransform>(out var rt))
            {
                rt.anchoredPosition = (Vector2)Input.mousePosition + _chargeIndicatorOffset;
            }

            if (_chargeIndicatorSlider != null)
                _chargeIndicatorSlider.value = _currentCharge;
        }

        private void HideChargeIndicator()
        {
            if (_chargeIndicatorRoot != null)
                _chargeIndicatorRoot.SetActive(false);
        }

        // ==================================================================
        // Private — hand transparency
        // ==================================================================

        private void SetHandAlpha(float alpha)
        {
            if (_handMaterial == null) return;

            // Works for Standard shader (Alpha/Transparent mode) and URP Lit.
            Color c = _handMaterial.HasProperty(AlphaProperty)
                ? _handMaterial.GetColor(AlphaProperty)
                : _handMaterial.color;

            c.a = alpha;

            if (_handMaterial.HasProperty(AlphaProperty))
                _handMaterial.SetColor(AlphaProperty, c);
            else
                _handMaterial.color = c;
        }
    }
}

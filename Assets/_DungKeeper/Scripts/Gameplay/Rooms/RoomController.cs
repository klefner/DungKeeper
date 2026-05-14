using System.Collections;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// MonoBehaviour attached to each placed room GameObject.
    /// Owns the visual representation — health bars, particles, lighting — and
    /// delegates economy logic to <see cref="RoomManager"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomController : MonoBehaviour
    {
        // -------------------------------------------------------------------------
        // Data & definition (set by RoomManager.TryPlaceRoom)
        // -------------------------------------------------------------------------

        /// <summary>Mutable runtime state for this room. Set once via Initialize.</summary>
        public RoomData Data { get; private set; }

        /// <summary>Static ScriptableObject definition for this room type.</summary>
        public RoomTypeSO Definition { get; private set; }

        /// <summary>
        /// True when the room has positive health and is toggled on.
        /// Used by <see cref="InvaderController"/> and <see cref="GameManager"/> to
        /// filter out destroyed or deactivated rooms from target selection.
        /// </summary>
        public bool IsOperational => Data != null && Data.Health > 0f && Data.IsActive;

        // -------------------------------------------------------------------------
        // Inspector-assigned visual components
        // -------------------------------------------------------------------------

        [Header("Renderers")]
        [SerializeField] private Renderer[] _roomRenderers;

        [Header("Particles")]
        [SerializeField] private ParticleSystem _productionEffect;
        [SerializeField] private ParticleSystem _damageEffect;

        [Header("Lighting")]
        [SerializeField] private Light _ambientLight;

        [Header("Visual State Colors")]
        [SerializeField] private Color _activeColor   = new Color(0.2f, 0.8f, 0.3f);
        [SerializeField] private Color _inactiveColor = new Color(0.4f, 0.4f, 0.4f);
        [SerializeField] private Color _damagedColor  = new Color(0.9f, 0.3f, 0.1f);

        // -------------------------------------------------------------------------
        // Runtime state
        // -------------------------------------------------------------------------

        private float _currentHealth;
        private float _maxHealth;
        private bool  _isDamaged;

        private MaterialPropertyBlock _propBlock;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");

        // Track last-applied state to skip redundant visual updates.
        private bool _lastKnownIsActive = true;
        private bool _lastKnownIsDamaged;

        private Coroutine _damageFlashCoroutine;

        // -------------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------------

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
        }

        // -------------------------------------------------------------------------
        // Initialisation
        // -------------------------------------------------------------------------

        /// <summary>
        /// Called by <see cref="RoomManager"/> immediately after instantiation.
        /// Must be called before any other method.
        /// </summary>
        public void Initialize(RoomData data, RoomTypeSO definition)
        {
            Data       = data ?? throw new System.ArgumentNullException(nameof(data));
            Definition = definition; // may be null — visual features degrade gracefully

            _maxHealth     = definition != null ? definition.MaxHealth : 100f;
            _currentHealth = _maxHealth;
            _isDamaged     = false;

            // Push initial values into RoomData health fields (kept in sync).
            Data.Health    = _currentHealth;
            Data.MaxHealth = _maxHealth;
            Data.IsActive  = true;

            ApplyVisualState(forceRefresh: true);
        }

        // -------------------------------------------------------------------------
        // Unity lifecycle
        // -------------------------------------------------------------------------

        private void Update()
        {
            if (Data == null) return;

            bool activeChanged  = Data.IsActive != _lastKnownIsActive;
            bool damagedChanged = _isDamaged    != _lastKnownIsDamaged;

            if (activeChanged || damagedChanged)
                ApplyVisualState(forceRefresh: false);
        }

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Alias for <see cref="TakeDamage"/> used by <see cref="InvaderController"/>.
        /// </summary>
        public void ApplyDamage(float amount) => TakeDamage(amount);

        /// <summary>Apply an amount of structural damage to the room.</summary>
        public void TakeDamage(float amount)
        {
            if (amount <= 0f) return;

            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            Data.Health    = _currentHealth;

            // Flash the damage tint.
            if (_damageFlashCoroutine != null)
                StopCoroutine(_damageFlashCoroutine);
            _damageFlashCoroutine = StartCoroutine(FlashDamage());

            // Enter / exit damaged state at 50 % health threshold.
            bool shouldBeDamaged = _currentHealth < _maxHealth * 0.5f;
            if (shouldBeDamaged != _isDamaged)
            {
                _isDamaged = shouldBeDamaged;
                if (_isDamaged && _damageEffect != null && !_damageEffect.isPlaying)
                    _damageEffect.Play();
                else if (!_isDamaged && _damageEffect != null && _damageEffect.isPlaying)
                    _damageEffect.Stop();
            }

            if (_currentHealth <= 0f)
            {
                // Room is structurally destroyed — notify manager for grid cleanup.
                RoomManager manager = Object.FindFirstObjectByType<RoomManager>();
                if (manager != null)
                    manager.NotifyRoomDestroyed(this);
                else
                    Destroy(gameObject);
            }
        }

        /// <summary>Restore structural health, clamped to maximum.</summary>
        public void Repair(float amount)
        {
            if (amount <= 0f) return;

            _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);
            Data.Health    = _currentHealth;

            // Exit damaged state once above the 50 % threshold again.
            if (_isDamaged && _currentHealth >= _maxHealth * 0.5f)
            {
                _isDamaged = false;
                if (_damageEffect != null && _damageEffect.isPlaying)
                    _damageEffect.Stop();
            }
        }

        /// <summary>Toggle whether the room is producing resources.</summary>
        public void SetActive(bool active)
        {
            if (Data == null) return;
            Data.IsActive = active;
            // Visual refresh is handled in Update via dirty-flag comparison.
        }

        /// <summary>
        /// Called when a unit enters this room.
        /// Displays a presence indicator; hook into a pooled prefab system when available.
        /// </summary>
        public void OnUnitEntered(UnitData unit)
        {
            if (unit == null || Data == null) return;
            Debug.Log($"[RoomController] {unit.Name} entered {Data.Name ?? Definition?.DisplayName}.");
        }

        /// <summary>Called when a unit's assignment to this room is cleared.</summary>
        public void OnUnitExited(UnitData unit)
        {
            if (unit == null || Data == null) return;
            Debug.Log($"[RoomController] {unit.Name} exited {Data.Name ?? Definition?.DisplayName}.");
        }

        // -------------------------------------------------------------------------
        // Visual helpers
        // -------------------------------------------------------------------------

        private void ApplyVisualState(bool forceRefresh)
        {
            if (Data == null) return;

            bool isActive  = Data.IsActive;
            bool isDamaged = _isDamaged;

            Color targetColor = !isActive ? _inactiveColor
                              : isDamaged  ? _damagedColor
                              : _activeColor;

            if (_roomRenderers != null)
            {
                foreach (Renderer r in _roomRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(ColorId, targetColor);
                    r.SetPropertyBlock(_propBlock);
                }
            }

            if (_ambientLight != null)
                _ambientLight.color = targetColor;

            if (_productionEffect != null)
            {
                bool shouldPlay = isActive && !isDamaged;
                if (shouldPlay && !_productionEffect.isPlaying)
                    _productionEffect.Play();
                else if (!shouldPlay && _productionEffect.isPlaying)
                    _productionEffect.Stop();
            }

            _lastKnownIsActive  = isActive;
            _lastKnownIsDamaged = isDamaged;
        }

        private IEnumerator FlashDamage()
        {
            // Apply a brief damage tint then revert to the logical color.
            if (_roomRenderers != null)
            {
                foreach (Renderer r in _roomRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(ColorId, _damagedColor);
                    r.SetPropertyBlock(_propBlock);
                }
            }

            if (_ambientLight != null)
                _ambientLight.color = _damagedColor;

            yield return new WaitForSeconds(0.15f);

            // Force the correct state back.
            ApplyVisualState(forceRefresh: true);
            _damageFlashCoroutine = null;
        }

        // -------------------------------------------------------------------------
        // Cleanup
        // -------------------------------------------------------------------------

        private void OnDestroy()
        {
            if (_productionEffect != null && _productionEffect.isPlaying) _productionEffect.Stop();
            if (_damageEffect     != null && _damageEffect.isPlaying)     _damageEffect.Stop();
        }
    }
}

using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Provides procedural trauma-based camera shake.
    /// Attach to the Main Camera; call <see cref="Shake"/> from any system
    /// (slap feedback, explosions, quake events) to add trauma.
    ///
    /// <para>
    ///   The shake uses a max-intensity clamp so multiple simultaneous triggers
    ///   cannot push the camera beyond a designer-controlled bound.  Intensity
    ///   decays exponentially each frame via <see cref="_shakeDecayRate"/>.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraShake : MonoBehaviour
    {
        // =====================================================================
        // Singleton
        // =====================================================================

        /// <summary>
        /// Process-wide singleton.  Populated in <see cref="Awake"/>; destroyed
        /// when the camera is unloaded.  Only one <see cref="CameraShake"/>
        /// instance should exist per scene.
        /// </summary>
        public static CameraShake Instance { get; private set; }

        // =====================================================================
        // Serialised fields
        // =====================================================================

        [Header("Shake Settings")]
        [Tooltip("Absolute maximum offset in world units that shake can produce.")]
        [SerializeField, Min(0f)] private float _maxShakeIntensity = 0.3f;

        [Tooltip("Rate at which current shake intensity decays per second (higher = snappier return).")]
        [SerializeField, Min(0.1f)] private float _shakeDecayRate = 5f;

        // =====================================================================
        // Runtime state
        // =====================================================================

        private float   _currentShakeIntensity;
        private Vector3 _originalPosition;

        // =====================================================================
        // Unity lifecycle
        // =====================================================================

        private void Awake()
        {
            // Singleton pattern with scene-lifetime scope.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning(
                    "[CameraShake] Duplicate instance detected — destroying the new one.", this);
                Destroy(this);
                return;
            }

            Instance = this;
            _originalPosition = transform.localPosition;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>
        /// LateUpdate runs after all Update calls, ensuring the final camera
        /// position is applied after any camera-following logic.
        /// </summary>
        private void LateUpdate()
        {
            const float kDeadZone = 0.001f;

            if (_currentShakeIntensity <= kDeadZone)
            {
                // Snap back cleanly and zero out to avoid floating-point drift
                transform.localPosition = _originalPosition;
                _currentShakeIntensity  = 0f;
                return;
            }

            // Apply a random offset within the current intensity envelope
            Vector3 offset = new Vector3(
                Random.Range(-1f, 1f) * _currentShakeIntensity,
                Random.Range(-1f, 1f) * _currentShakeIntensity,
                0f
            );
            transform.localPosition = _originalPosition + offset;

            // Exponential decay: intensity * (1 - decayRate * dt) approaches zero
            _currentShakeIntensity = Mathf.Max(
                0f,
                _currentShakeIntensity - _shakeDecayRate * Time.deltaTime);
        }

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>
        /// Adds trauma to the camera shake, clamped to <see cref="_maxShakeIntensity"/>.
        /// Safe to call from any system; has no effect if no instance exists in the scene.
        /// </summary>
        /// <param name="intensity">
        ///   Desired shake strength in world units.  Values above
        ///   <c>_maxShakeIntensity</c> are clamped.
        /// </param>
        public static void Shake(float intensity)
        {
            if (Instance == null) return;
            Instance._currentShakeIntensity =
                Mathf.Clamp(intensity, 0f, Instance._maxShakeIntensity);
        }

        /// <summary>
        /// Adds to the existing shake intensity rather than replacing it,
        /// useful for additive trauma sources (e.g. multiple nearby explosions).
        /// Result is clamped to <see cref="_maxShakeIntensity"/>.
        /// </summary>
        /// <param name="intensity">Additive trauma amount.</param>
        public static void AddShake(float intensity)
        {
            if (Instance == null) return;
            Instance._currentShakeIntensity =
                Mathf.Clamp(Instance._currentShakeIntensity + intensity,
                            0f,
                            Instance._maxShakeIntensity);
        }

        /// <summary>
        /// Immediately resets both the shake intensity and the camera position.
        /// Useful on scene transitions or cutscenes that cannot tolerate residual shake.
        /// </summary>
        public static void StopShake()
        {
            if (Instance == null) return;
            Instance._currentShakeIntensity  = 0f;
            Instance.transform.localPosition = Instance._originalPosition;
        }

        // =====================================================================
        // Editor helpers
        // =====================================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_maxShakeIntensity < 0f) _maxShakeIntensity = 0f;
            if (_shakeDecayRate    < 0.1f) _shakeDecayRate  = 0.1f;
        }
#endif
    }
}

using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// MonoBehaviour that binds a <see cref="UnitData"/> data model to a dungeon
    /// unit GameObject.  Handles visual representation, registration with
    /// <see cref="GameManager"/>, and provides a surface for combat interactions.
    ///
    /// <para>
    ///   All gameplay logic that mutates <see cref="UnitData"/> must go through the
    ///   pure-C# systems (<see cref="SlapSystem"/>, <see cref="CombatSystem"/>, etc.).
    ///   This class is the thin scene-side façade only.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitController : MonoBehaviour
    {
        // =====================================================================
        // Runtime data
        // =====================================================================

        /// <summary>Mutable runtime data for this unit.  Populated via <see cref="Initialize"/>.</summary>
        public UnitData Data { get; private set; }

        // =====================================================================
        // Convenience accessors
        // =====================================================================

        /// <summary>True when the unit's data model reports it as alive.</summary>
        public bool IsAlive => Data != null && Data.IsAlive;

        // =====================================================================
        // Serialised visual components
        // =====================================================================

        [Header("Visuals")]
        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer _bodyRenderer;

        [Header("State Colors")]
        [SerializeField] private Color _workingColor  = Color.green;
        [SerializeField] private Color _fightingColor = Color.red;
        [SerializeField] private Color _fleeingColor  = Color.yellow;
        [SerializeField] private Color _deadColor     = Color.grey;

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void OnEnable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.RegisterUnit(this);
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
                GameManager.Instance.UnregisterUnit(this);
        }

        // =====================================================================
        // Initialisation
        // =====================================================================

        /// <summary>
        /// Binds this controller to a <see cref="UnitData"/> model.
        /// Must be called once immediately after instantiation.
        /// </summary>
        public void Initialize(UnitData data)
        {
            Data = data ?? throw new System.ArgumentNullException(nameof(data));
            RefreshVisuals();
        }

        // =====================================================================
        // Public combat API
        // =====================================================================

        /// <summary>
        /// Applies damage to the unit's data model and refreshes visuals.
        /// Fires a <see cref="UnitDiedEvent"/> via the global bus when health reaches zero.
        /// </summary>
        public void TakeDamage(float amount)
        {
            if (Data == null || !Data.IsAlive) return;

            bool died = Data.ApplyHealthDelta(-Mathf.Abs(amount));

            RefreshVisuals();

            if (died)
                EventBus.Global.Publish(new UnitDiedEvent(Data));
        }

        // =====================================================================
        // Death
        // =====================================================================

        /// <summary>
        /// Plays the death visual sequence and destroys this GameObject.
        /// Called by <see cref="GameManager.DespawnUnit"/> when a unit leaves the simulation.
        /// </summary>
        public void Die()
        {
            if (Data != null)
                Data.CurrentState = UnitState.Dead;

            RefreshVisuals();

            if (_animator != null)
                _animator.SetTrigger("Die");

            // Destroy after a short delay to allow death animation to complete.
            Destroy(gameObject, 2f);
        }

        // =====================================================================
        // Slap reaction visual
        // =====================================================================

        private static readonly int s_HashSlapReact = Animator.StringToHash("SlapReact");

        /// <summary>
        /// Triggers the appropriate animation / visual reaction for a slap response.
        /// Called by <see cref="GameManager.SlapUnit"/> after the SlapSystem resolves.
        /// </summary>
        public void PlaySlapReaction(SlapResponse response, float force)
        {
            if (_animator != null)
                _animator.SetTrigger(s_HashSlapReact);

            // Camera shake scales with force and severity
            float shakeIntensity = response switch
            {
                SlapResponse.SpeedUp       => 0.05f * force / 10f,
                SlapResponse.BecomeExcited => 0.08f * force / 10f,
                SlapResponse.BecomeAngry   => 0.12f * force / 10f,
                SlapResponse.Rebel         => 0.20f * force / 10f,
                SlapResponse.Quit          => 0.25f,
                _                          => 0.03f * force / 10f
            };

            CameraShake.AddShake(shakeIntensity);
            RefreshVisuals();
        }

        // =====================================================================
        // Visual
        // =====================================================================

        private void RefreshVisuals()
        {
            if (_bodyRenderer == null || Data == null) return;

            Color c = Data.CurrentState switch
            {
                UnitState.Fighting  => _fightingColor,
                UnitState.Fleeing   => _fleeingColor,
                UnitState.Dead      => _deadColor,
                UnitState.Working   => _workingColor,
                _                   => _workingColor
            };

            _bodyRenderer.material.color = c;
        }
    }
}

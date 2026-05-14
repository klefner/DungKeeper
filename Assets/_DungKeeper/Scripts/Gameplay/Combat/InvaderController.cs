using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace DungKeeper
{
    // =========================================================================
    // Supporting runtime data type
    // =========================================================================

    /// <summary>
    /// Serialisable runtime data snapshot for a single invader.
    /// Kept as a plain class (no MonoBehaviour) so it can be passed between
    /// systems without a scene reference.
    /// </summary>
    [Serializable]
    public sealed class InvaderData
    {
        public string      Id           { get; } = Guid.NewGuid().ToString();
        public string      DisplayName  { get; set; } = "Unknown Invader";
        public ThreatLevel ThreatLevel  { get; set; } = ThreatLevel.Skirmish;

        public float MaxHealth      { get; set; } = 100f;
        public float Health         { get; set; } = 100f;
        public float AttackDamage   { get; set; } = 10f;
        public float MoveSpeed      { get; set; } = 3.5f;
        public float AttackRange    { get; set; } = 1.5f;
        public float AttackCooldown { get; set; } = 1.5f;

        public bool IsAlive => Health > 0f;

        public InvaderData(string displayName, ThreatLevel level)
        {
            DisplayName = displayName ?? "Invader";
            ThreatLevel = level;
        }

        /// <summary>Applies damage, clamped to [0, MaxHealth].</summary>
        public void TakeDamage(float amount)
        {
            Health = Mathf.Max(0f, Health - Mathf.Abs(amount));
        }
    }

    // =========================================================================
    // Events
    // =========================================================================

    /// <summary>Dispatched by <see cref="CombatSystem"/> when an invader dies.</summary>
    public sealed class InvaderDiedEvent
    {
        public InvaderController Invader { get; }
        public InvaderDiedEvent(InvaderController invader)
            => Invader = invader ?? throw new ArgumentNullException(nameof(invader));
    }

    // =========================================================================
    // InvaderController
    // =========================================================================

    /// <summary>
    /// MonoBehaviour that drives a single dungeon invader (hero or other threat).
    ///
    /// <para>Behaviour loop (evaluated every Update):</para>
    /// <list type="number">
    ///   <item>If dead → do nothing (already cleaned up).</item>
    ///   <item>If no unit target → poll <see cref="GameManager"/> for nearest fighting unit.</item>
    ///   <item>If unit target alive → move toward it; attack when within range and off cooldown.</item>
    ///   <item>If no unit targets remain → switch to nearest room and attack it.</item>
    /// </list>
    ///
    /// Attach this to an invader prefab alongside a <see cref="NavMeshAgent"/> and
    /// an <see cref="Animator"/> that honours the trigger/bool parameters listed below.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class InvaderController : MonoBehaviour
    {
        // =====================================================================
        // Serialised fields
        // =====================================================================

        [SerializeField] private Animator     _animator;
        [SerializeField] private NavMeshAgent _agent;

        [Header("Combat Tuning")]
        [SerializeField, Min(0.1f)] private float _attackRange    = 1.5f;
        [SerializeField, Min(0.1f)] private float _attackCooldown = 1.5f;

        // =====================================================================
        // Runtime state
        // =====================================================================

        private float          _lastAttackTime;
        private UnitController _currentTarget;
        private RoomController _targetRoom;
        private bool           _isDead;

        // Animator parameter hashes (computed once for efficiency)
        private static readonly int s_HashAttack    = Animator.StringToHash("Attack");
        private static readonly int s_HashDie       = Animator.StringToHash("Die");
        private static readonly int s_HashMoveSpeed = Animator.StringToHash("MoveSpeed");

        // =====================================================================
        // Public data
        // =====================================================================

        /// <summary>Runtime data snapshot for this invader.  Initialised by <see cref="Initialize"/>.</summary>
        public InvaderData Data { get; private set; }

        // =====================================================================
        // Lifecycle
        // =====================================================================

        private void Awake()
        {
            // Allow the agent to be assigned in the Inspector or discovered at
            // Awake time if forgotten.
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (_isDead || Data == null) return;

            // ----------------------------------------------------------------
            // Death check
            // ----------------------------------------------------------------
            if (!Data.IsAlive)
            {
                Die();
                return;
            }

            // ----------------------------------------------------------------
            // Target acquisition — unit priority over rooms
            // ----------------------------------------------------------------
            if (_currentTarget == null || !_currentTarget.IsAlive)
            {
                _currentTarget = null;
                _currentTarget = GameManager.Instance != null
                    ? GameManager.Instance.GetNearestFightingUnit(transform.position)
                    : null;
            }

            if (_currentTarget != null && _currentTarget.IsAlive)
            {
                // Move toward the unit target
                SetDestination(_currentTarget.transform.position);

                float distToUnit = Vector3.Distance(transform.position, _currentTarget.transform.position);
                if (distToUnit <= _attackRange && CanAttack())
                    Attack(_currentTarget);
            }
            else
            {
                // No unit targets — fall back to attacking a room
                if (_targetRoom == null || !_targetRoom.IsOperational)
                    _targetRoom = GameManager.Instance != null
                        ? GameManager.Instance.GetNearestRoom(transform.position)
                        : null;

                if (_targetRoom != null && _targetRoom.IsOperational)
                {
                    SetDestination(_targetRoom.transform.position);

                    float distToRoom = Vector3.Distance(transform.position, _targetRoom.transform.position);
                    if (distToRoom <= _attackRange && CanAttack())
                        AttackRoom(_targetRoom);
                }
            }

            // Update locomotion blend parameter
            if (_animator != null && _agent != null)
                _animator.SetFloat(s_HashMoveSpeed, _agent.velocity.magnitude);
        }

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>
        /// Populates runtime <see cref="Data"/> and synchronises <see cref="NavMeshAgent"/>
        /// settings.  Call this immediately after instantiating the prefab.
        /// </summary>
        public void Initialize(InvaderData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));

            // Mirror data values to the serialised tuning fields so the
            // combat loop uses data-driven values when available.
            _attackRange    = data.AttackRange;
            _attackCooldown = data.AttackCooldown;

            if (_agent != null)
                _agent.speed = data.MoveSpeed;
        }

        // =====================================================================
        // Combat
        // =====================================================================

        /// <summary>Deals damage to a <see cref="UnitController"/> and triggers the attack animation.</summary>
        public void Attack(UnitController target)
        {
            if (target == null || !target.IsAlive) return;

            _lastAttackTime = Time.time;

            // Trigger attack animation
            if (_animator != null)
                _animator.SetTrigger(s_HashAttack);

            // Deal damage through the target's data model
            target.TakeDamage(Data.AttackDamage);
        }

        /// <summary>Deals damage to a <see cref="RoomController"/> and triggers the attack animation.</summary>
        public void AttackRoom(RoomController room)
        {
            if (room == null || !room.IsOperational) return;

            _lastAttackTime = Time.time;

            if (_animator != null)
                _animator.SetTrigger(s_HashAttack);

            room.ApplyDamage(Data.AttackDamage);
        }

        /// <summary>
        /// Plays the death animation, fires an <see cref="InvaderDiedEvent"/> via
        /// <see cref="CombatSystem"/>, and destroys the GameObject after a brief delay.
        /// </summary>
        public void Die()
        {
            if (_isDead) return;
            _isDead = true;

            // Stop navigation
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            // Trigger death animation
            if (_animator != null)
                _animator.SetTrigger(s_HashDie);

            // Notify combat system
            CombatSystem.Instance?.OnInvaderDied(this);

            // Also publish on the global event bus
            EventBus.Global.Publish(new InvaderDiedEvent(this));

            // Delay destruction to allow the death animation to finish
            StartCoroutine(DestroyAfterDelay(2f));
        }

        // =====================================================================
        // Private helpers
        // =====================================================================

        private bool CanAttack()
            => Time.time - _lastAttackTime >= _attackCooldown;

        private void SetDestination(Vector3 target)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            // Only update when the destination has meaningfully changed to
            // avoid NavMeshAgent rebuilding the path every frame.
            if (!_agent.hasPath || Vector3.SqrMagnitude(_agent.destination - target) > 0.25f)
                _agent.SetDestination(target);
        }

        private IEnumerator DestroyAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            Destroy(gameObject);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_attackRange    <= 0f) _attackRange    = 1.5f;
            if (_attackCooldown <= 0f) _attackCooldown = 1.5f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _attackRange);
        }
#endif
    }
}

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace DungKeeper
{
    // =========================================================================
    // Event
    // =========================================================================

    /// <summary>
    /// Published on <see cref="EventBus.Global"/> when an invader's GameObject is
    /// destroyed so that UI, audio, and achievement systems can react.
    /// </summary>
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
    /// Attach to an invader prefab alongside a <see cref="NavMeshAgent"/> and an
    /// <see cref="Animator"/> that honours the "Attack", "Die", and "MoveSpeed" parameters.
    /// The <see cref="InvaderData"/> that drives this controller is the pure-C# record
    /// defined in <see cref="CombatSystem"/>.
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

        [Header("Combat Tuning — overridden by InvaderData at runtime")]
        [SerializeField, Min(0.1f)] private float _attackRange    = 1.5f;
        [SerializeField, Min(0.1f)] private float _attackCooldown = 1.5f;
        [SerializeField, Min(0.1f)] private float _moveSpeed      = 3.5f;

        // =====================================================================
        // Runtime state
        // =====================================================================

        private float          _lastAttackTime;
        private UnitController _currentTarget;
        private RoomController _targetRoom;
        private bool           _isDead;

        // Animator parameter hashes (computed once at class load time for GC efficiency)
        private static readonly int s_HashAttack    = Animator.StringToHash("Attack");
        private static readonly int s_HashDie       = Animator.StringToHash("Die");
        private static readonly int s_HashMoveSpeed = Animator.StringToHash("MoveSpeed");

        // =====================================================================
        // Public data
        // =====================================================================

        /// <summary>
        /// Runtime data for this invader.  Populated by <see cref="Initialize"/>;
        /// the type is the pure-C# <see cref="DungKeeper.InvaderData"/> record
        /// defined and managed by <see cref="CombatSystem"/>.
        /// </summary>
        public InvaderData Data { get; private set; }

        // =====================================================================
        // Unity lifecycle
        // =====================================================================

        private void Awake()
        {
            if (_agent == null)
                _agent = GetComponent<NavMeshAgent>();

            if (_animator == null)
                _animator = GetComponentInChildren<Animator>();
        }

        private void Update()
        {
            if (_isDead || Data == null) return;

            // ----------------------------------------------------------------
            // Death check — data model is authoritative
            // ----------------------------------------------------------------
            if (!Data.IsAlive)
            {
                Die();
                return;
            }

            // ----------------------------------------------------------------
            // Target acquisition — unit targets take priority over rooms
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
                SetDestination(_currentTarget.transform.position);

                float distToUnit = Vector3.Distance(transform.position, _currentTarget.transform.position);
                if (distToUnit <= _attackRange && CanAttack())
                    Attack(_currentTarget);
            }
            else
            {
                // No fighting units left — attack the nearest operational room
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

            // Sync locomotion blend parameter with current agent speed
            if (_animator != null && _agent != null)
                _animator.SetFloat(s_HashMoveSpeed, _agent.velocity.magnitude);
        }

        // =====================================================================
        // Public API
        // =====================================================================

        /// <summary>
        /// Binds a <see cref="InvaderData"/> record to this controller and applies
        /// its movement speed to the <see cref="NavMeshAgent"/>.
        /// Call immediately after instantiating the prefab.
        /// </summary>
        public void Initialize(InvaderData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));

            // InvaderData.Attack is the combat damage stat; there is no dedicated
            // move-speed / range / cooldown field — use the Inspector defaults unless
            // the designer extends InvaderData in the future.
            if (_agent != null)
                _agent.speed = _moveSpeed;
        }

        // =====================================================================
        // Combat methods
        // =====================================================================

        /// <summary>
        /// Deals damage to <paramref name="target"/> and triggers the attack animation.
        /// </summary>
        public void Attack(UnitController target)
        {
            if (target == null || !target.IsAlive) return;

            _lastAttackTime = Time.time;

            if (_animator != null)
                _animator.SetTrigger(s_HashAttack);

            target.TakeDamage(Data.Attack);
        }

        /// <summary>
        /// Deals damage to <paramref name="room"/> and triggers the attack animation.
        /// </summary>
        public void AttackRoom(RoomController room)
        {
            if (room == null || !room.IsOperational) return;

            _lastAttackTime = Time.time;

            if (_animator != null)
                _animator.SetTrigger(s_HashAttack);

            room.ApplyDamage(Data.Attack);
        }

        /// <summary>
        /// Plays the death animation, notifies <see cref="CombatSystem.Instance"/>
        /// (which resolves the threat if all invaders are down), publishes
        /// <see cref="InvaderDiedEvent"/> on the global bus, then destroys
        /// the GameObject after a brief delay.
        /// </summary>
        public void Die()
        {
            if (_isDead) return;
            _isDead = true;

            // Halt navigation so the corpse stops sliding
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            if (_animator != null)
                _animator.SetTrigger(s_HashDie);

            // Notify the pure-C# combat system
            CombatSystem.Instance?.OnInvaderDied(this);

            // Notify scene-side listeners (UI, audio, achievements)
            EventBus.Global.Publish(new InvaderDiedEvent(this));

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

            // Only re-path when the destination has moved enough to matter,
            // avoiding unnecessary NavMesh path rebuilds every frame.
            if (!_agent.hasPath || Vector3.SqrMagnitude(_agent.destination - target) > 0.25f)
                _agent.SetDestination(target);
        }

        private IEnumerator DestroyAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            Destroy(gameObject);
        }

        // =====================================================================
        // Editor helpers
        // =====================================================================

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_attackRange    <= 0f) _attackRange    = 1.5f;
            if (_attackCooldown <= 0f) _attackCooldown = 1.5f;
            if (_moveSpeed      <= 0f) _moveSpeed      = 3.5f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _attackRange);
        }
#endif
    }
}

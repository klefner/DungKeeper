using System.Collections.Generic;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Handles combat resolution when an enemy incursion is active.
    /// Guard units engage invaders; other units may flee or be injured.
    ///
    /// Pure C# — no MonoBehaviour dependency.
    /// </summary>
    public sealed class CombatSystem
    {
        // -------------------------------------------------------------------------
        // Singleton — set by GameManager at startup; null outside of a dungeon scene.
        // -------------------------------------------------------------------------

        /// <summary>
        /// The active scene-level instance.  Set by <see cref="GameManager"/> when it
        /// constructs the system; <c>null</c> outside of a dungeon scene.
        /// </summary>
        public static CombatSystem Instance { get; set; }

        // -------------------------------------------------------------------------
        // Fields
        // -------------------------------------------------------------------------

        private readonly GameSettings _settings;

        // Active invader count for the current threat.
        private int   _invaderCount;
        private float _invaderHealth;
        private bool  _threatActive;

        public bool ThreatActive => _threatActive;

        public CombatSystem(GameSettings settings)
        {
            _settings = settings;
            // Register this as the active scene instance.
            Instance  = this;
        }

        // -------------------------------------------------------------------------
        // Invader death notification
        // -------------------------------------------------------------------------

        /// <summary>
        /// Called by <see cref="InvaderController.Die"/> when a scene-side invader
        /// GameObject is destroyed.  Decrements the active invader count and checks
        /// for threat resolution.
        /// </summary>
        public void OnInvaderDied(InvaderController invader)
        {
            if (invader == null || !_threatActive) return;

            _invaderCount  = UnityEngine.Mathf.Max(0, _invaderCount - 1);

            // Reduce shared health pool by a proportional share
            float healthShare = invader.Data != null ? invader.Data.MaxHealth : 50f;
            _invaderHealth = UnityEngine.Mathf.Max(0f, _invaderHealth - healthShare);

            if (_invaderCount <= 0 || _invaderHealth <= 0f)
            {
                _threatActive = false;
                UnityEngine.Debug.Log("[CombatSystem] All invaders defeated — threat resolved.");
                EventBus.Global.Publish(new ThreatResolvedEvent(playerWon: true));
            }
        }

        /// <summary>Registers a new threat incursion.</summary>
        public void BeginThreat(ThreatLevel level, int invaderCount)
        {
            _invaderCount  = invaderCount;
            _invaderHealth = invaderCount * 50f * (1f + (int)level * 0.5f);
            _threatActive  = true;
            Debug.Log($"[CombatSystem] Threat started: {level}, {invaderCount} invaders, {_invaderHealth:F0} HP total.");
        }

        /// <summary>
        /// Advances combat by <paramref name="deltaTime"/> seconds.
        /// Returns true when the threat is resolved (all invaders defeated or player overrun).
        /// Fires <see cref="ThreatResolvedEvent"/> via <see cref="EventBus.Global"/> when done.
        /// </summary>
        public void Tick(IList<UnitData> units, float deltaTime)
        {
            if (!_threatActive) return;

            float guardDps    = 0f;
            float invaderDps  = _settings.BaseCombatDamagePerSecond * _invaderCount;

            // Sum guard DPS
            for (int i = 0; i < units.Count; i++)
            {
                UnitData u = units[i];
                if (!u.IsAlive || u.Role != UnitRole.Guard) continue;
                u.State = UnitState.Fighting;
                guardDps += u.Attack * 0.5f;
            }

            // Guards damage invaders
            _invaderHealth -= guardDps * deltaTime;

            // Non-guard units take ambient damage and may flee
            for (int i = 0; i < units.Count; i++)
            {
                UnitData u = units[i];
                if (!u.IsAlive || u.Role == UnitRole.Guard) continue;

                // Ambient combat damage to non-guards
                float damage = invaderDps * 0.1f * deltaTime;
                u.Health = Mathf.Max(0f, u.Health - damage);
                u.Fear   = Mathf.Min(100f, u.Fear + 5f * deltaTime);

                // Low-health flee
                if (u.Health / u.MaxHealth * 100f < _settings.LowHealthFleeThreshold)
                    u.State = UnitState.Fleeing;

                if (u.Health <= 0f)
                {
                    u.State = UnitState.Dead;
                    EventBus.Global.Publish(new UnitDiedEvent(u));
                }
            }

            // Check resolution
            if (_invaderHealth <= 0f)
            {
                _threatActive = false;
                Debug.Log("[CombatSystem] Threat resolved — player won.");
                EventBus.Global.Publish(new ThreatResolvedEvent(playerWon: true));
            }
            else if (!HasLivingGuards(units))
            {
                // No guards left — dungeon overrun
                _threatActive = false;
                Debug.Log("[CombatSystem] Threat resolved — dungeon overrun.");
                EventBus.Global.Publish(new ThreatResolvedEvent(playerWon: false));
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static bool HasLivingGuards(IList<UnitData> units)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].IsAlive && units[i].Role == UnitRole.Guard)
                    return true;
            return false;
        }
    }
}

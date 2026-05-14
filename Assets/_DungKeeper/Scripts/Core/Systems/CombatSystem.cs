using System;
using System.Collections.Generic;
using System.Linq;

namespace DungKeeper
{
    // =========================================================================
    // InvaderData
    // =========================================================================

    /// <summary>
    /// Mutable runtime state for a single invading enemy unit.
    /// Created and managed exclusively by <see cref="CombatSystem"/>.
    /// Scene-side <c>InvaderController</c> MonoBehaviours hold a reference to this
    /// record via their <c>Data</c> property.
    /// </summary>
    public sealed class InvaderData
    {
        /// <summary>Stable GUID, assigned at spawn time.</summary>
        public string      Id        { get; }

        public string      Name      { get; }
        public float       Health    { get; set; }
        public float       MaxHealth { get; }

        /// <summary>Raw damage output per second in melee combat.</summary>
        public float Attack  { get; }

        /// <summary>
        /// Damage mitigation.  Effective incoming DPS = max(MinDPS, raw - Defense).
        /// </summary>
        public float Defense { get; }

        public ThreatLevel Threat  { get; }
        public bool        IsAlive => Health > 0f;

        public InvaderData(string id, string name, float health, float attack, float defense, ThreatLevel threat)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Invader id must not be null or empty.", nameof(id));

            Id        = id;
            Name      = name;
            MaxHealth = health;
            Health    = health;
            Attack    = attack;
            Defense   = defense;
            Threat    = threat;
        }

        public override string ToString()
            => $"[Invader {Name} | HP:{Health:F0}/{MaxHealth:F0} | ATK:{Attack:F1} DEF:{Defense:F1}]";
    }

    // =========================================================================
    // CombatReport
    // =========================================================================

    /// <summary>A point-in-time snapshot of the current combat state.</summary>
    public sealed class CombatReport
    {
        /// <summary>Number of invaders still alive.</summary>
        public int InvadersRemaining { get; }

        /// <summary>Number of dungeon units currently in the Fighting state.</summary>
        public int FightingUnits { get; }

        /// <summary>
        /// Positive = defenders have the edge; negative = invaders winning.
        /// Calculated as (defender effective power) − (invader effective power),
        /// where power = CurrentHealth + Attack × 3.
        /// </summary>
        public float EstimatedOutcome { get; }

        public CombatReport(int invadersRemaining, int fightingUnits, float estimatedOutcome)
        {
            InvadersRemaining = invadersRemaining;
            FightingUnits     = fightingUnits;
            EstimatedOutcome  = estimatedOutcome;
        }
    }

    // =========================================================================
    // CombatSystem
    // =========================================================================

    /// <summary>
    /// Simulates combat between dungeon defenders and invading threats.
    ///
    /// <b>Combat formula</b> (damage per second):
    ///   dps = max(1, attacker.Attack × (1 + angerBonus) − defender.Defense × (1 − fearPenalty))
    ///   • angerBonus  = +0.20 when unit.Anger ≥ 70
    ///   • fearPenalty = +0.20 when unit.Morale &lt; 30 (lowers effective defense)
    ///
    /// <b>Flee condition</b>:
    ///   Health &lt; 20% → flee unless Loyal or Aggressive personality.
    ///
    /// <b>Threat escalation curve</b> (by elapsed game time):
    ///   0–119 s → Skirmish | 120–239 s → Raid | 240–359 s → Assault | 360+ s → Siege
    ///
    /// <para>Pure C# — no Unity or MonoBehaviour dependency in this class.</para>
    /// </summary>
    public sealed class CombatSystem
    {
        // -------------------------------------------------------------------------
        // Static instance (set by GameManager; used by scene-side controllers)
        // -------------------------------------------------------------------------

        /// <summary>
        /// The active scene-level instance.
        /// Set by <c>GameManager</c> when it constructs this system; null outside a dungeon scene.
        /// </summary>
        public static CombatSystem Instance { get; set; }

        // -------------------------------------------------------------------------
        // Tuning
        // -------------------------------------------------------------------------

        private const float AngerBonusThreshold     = 70f;
        private const float AngerBonusMultiplier    = 0.20f;
        private const float MoralePenaltyThreshold  = 30f;
        private const float MoralePenaltyMultiplier = 0.20f;
        private const float LowHealthFleePercent    = 0.20f;
        private const float MinDamagePerSecond      = 1f;
        private const float EscalationIntervalSecs  = 120f;

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        /// <summary>All invaders spawned for the current incursion (alive or recently dead).</summary>
        public List<InvaderData> ActiveInvaders { get; } = new List<InvaderData>();

        /// <summary>True while at least one invader is alive.</summary>
        public bool IsUnderAttack => ActiveInvaders.Any(i => i.IsAlive);

        // Legacy flag consumed by BeginThreat / OnInvaderDied path
        private bool  _threatActive;
        public  bool  ThreatActive => _threatActive || IsUnderAttack;

        // Battle accounting
        private float _combatStartTime;
        private float _elapsedGameTime;
        private int   _invadersKilledThisBattle;
        private int   _defendersLostThisBattle;

        private readonly GameSettings _settings;
        private readonly Random       _rng;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        /// <param name="settings">Active game-balance settings.</param>
        /// <param name="seed">RNG seed. 0 = random.</param>
        public CombatSystem(GameSettings settings, int seed = 0)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _rng      = seed == 0 ? new Random() : new Random(seed);
            Instance  = this;
        }

        // -------------------------------------------------------------------------
        // SpawnThreat
        // -------------------------------------------------------------------------

        /// <summary>
        /// Creates a new wave of typed <see cref="InvaderData"/> scaled to
        /// <paramref name="level"/> and publishes <see cref="ThreatSpawnedEvent"/>.
        ///
        /// Wave sizes: Skirmish=2, Raid=5, Assault=10, Siege=20.
        /// </summary>
        public void SpawnThreat(ThreatLevel level)
        {
            int count = level switch
            {
                ThreatLevel.Skirmish => 2,
                ThreatLevel.Raid     => 5,
                ThreatLevel.Assault  => 10,
                ThreatLevel.Siege    => 20,
                _                    => throw new ArgumentException($"Cannot spawn threat for {level}.", nameof(level))
            };

            (float hpScale, float atkScale, float defScale) = level switch
            {
                ThreatLevel.Skirmish => (0.60f, 0.60f, 0.50f),
                ThreatLevel.Raid     => (1.00f, 1.00f, 1.00f),
                ThreatLevel.Assault  => (1.50f, 1.40f, 1.20f),
                ThreatLevel.Siege    => (2.50f, 2.00f, 1.80f),
                _                    => (1.00f, 1.00f, 1.00f)
            };

            string[] namePool = InvaderNamesFor(level);

            for (int i = 0; i < count; i++)
            {
                float hp  = MathF.Round(60f * hpScale  + (float)_rng.NextDouble() * 20f * hpScale,  1);
                float atk = MathF.Round(8f  * atkScale + (float)_rng.NextDouble() * 4f  * atkScale, 1);
                float def = MathF.Round(3f  * defScale + (float)_rng.NextDouble() * 3f  * defScale, 1);

                string suffix = count > namePool.Length ? $" {i + 1}" : string.Empty;
                ActiveInvaders.Add(
                    new InvaderData(Guid.NewGuid().ToString(),
                                    namePool[i % namePool.Length] + suffix,
                                    hp, atk, def, level));
            }

            _threatActive             = true;
            _combatStartTime          = _elapsedGameTime;
            _invadersKilledThisBattle = 0;
            _defendersLostThisBattle  = 0;

            EventBus.Global.Publish(new ThreatSpawnedEvent(level, count));
        }

        // -------------------------------------------------------------------------
        // Legacy BeginThreat — called by scene/GameManager when spawning via prefabs
        // -------------------------------------------------------------------------

        /// <summary>
        /// Registers an externally-managed threat incursion (e.g., when Unity spawns
        /// invader prefabs independently of <see cref="SpawnThreat"/>).
        /// </summary>
        public void BeginThreat(ThreatLevel level, int invaderCount)
        {
            _threatActive             = true;
            _combatStartTime          = _elapsedGameTime;
            _invadersKilledThisBattle = 0;
            _defendersLostThisBattle  = 0;

            EventBus.Global.Publish(new ThreatSpawnedEvent(level, invaderCount));
        }

        // -------------------------------------------------------------------------
        // OnInvaderDied — called by scene-side InvaderController
        // -------------------------------------------------------------------------

        /// <summary>
        /// Notifies the system that a scene-side invader has died.
        /// Also marks the matching <see cref="InvaderData"/> record as dead if found.
        /// </summary>
        public void OnInvaderDied(string invaderId)
        {
            if (!_threatActive && !IsUnderAttack) return;

            // Find and mark the corresponding InvaderData record.
            var record = ActiveInvaders.Find(inv => inv.Id == invaderId);
            if (record != null)
            {
                record.Health = 0f;
                _invadersKilledThisBattle++;
            }

            ActiveInvaders.RemoveAll(i => !i.IsAlive);

            if (!IsUnderAttack)
            {
                _threatActive = false;
                EventBus.Global.Publish(new ThreatResolvedEvent(playerWon: true));
            }
        }

        // -------------------------------------------------------------------------
        // Tick — primary simulation entry point
        // -------------------------------------------------------------------------

        /// <summary>
        /// Advances combat by <paramref name="deltaTime"/> seconds.
        ///
        /// Each tick:
        /// <list type="number">
        ///   <item>Guard-role units and any unit already Fighting become eligible defenders.</item>
        ///   <item>Defenders with Anger ≥ 70 deal +20% damage.</item>
        ///   <item>Each invader attacks a random defender; Morale &lt; 30 → +20% incoming damage.</item>
        ///   <item>Morale drops and Fear rises under sustained attack.</item>
        ///   <item>Defenders below 20% HP flee unless Loyal or Aggressive.</item>
        ///   <item>Dead invaders are pruned; resolution is checked at the end of the tick.</item>
        /// </list>
        /// If all invaders die → <see cref="ThreatResolvedEvent"/>(<c>playerWon: true</c>).
        /// If all combat units die/flee → <see cref="ThreatResolvedEvent"/>(<c>playerWon: false</c>).
        /// </summary>
        public void Tick(IEnumerable<UnitData> units, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            _elapsedGameTime += deltaTime;

            if (!IsUnderAttack && !_threatActive) return;

            var unitList = units as IList<UnitData> ?? units.ToList();

            // Collect eligible defenders
            var defenders = new List<UnitData>(16);
            for (int i = 0; i < unitList.Count; i++)
            {
                var u = unitList[i];
                if (!u.IsAlive) continue;
                if (u.CurrentState == UnitState.Fighting || u.Role == UnitRole.Guard)
                {
                    u.CurrentState = UnitState.Fighting;
                    u.CurrentTask  = TaskType.Fight;
                    defenders.Add(u);
                }
            }

            var liveInvaders = ActiveInvaders.FindAll(i => i.IsAlive);

            if (defenders.Count == 0 || liveInvaders.Count == 0)
            {
                CheckResolution(defenders, liveInvaders);
                return;
            }

            // ------------------------------------------------------------------
            // Defenders → invaders
            // ------------------------------------------------------------------

            foreach (var defender in defenders)
            {
                if (liveInvaders.Count == 0) break;

                var   target     = liveInvaders[_rng.Next(liveInvaders.Count)];
                float angerBonus = defender.Anger >= AngerBonusThreshold ? AngerBonusMultiplier : 0f;
                float rawDps     = defender.Attack * (1f + angerBonus) - target.Defense;
                float damage     = Math.Max(MinDamagePerSecond, rawDps) * deltaTime;

                target.Health -= damage;

                if (!target.IsAlive)
                {
                    liveInvaders.Remove(target);
                    _invadersKilledThisBattle++;
                }
            }

            // ------------------------------------------------------------------
            // Invaders → defenders
            // ------------------------------------------------------------------

            foreach (var invader in liveInvaders)
            {
                if (defenders.Count == 0) break;

                var   target      = defenders[_rng.Next(defenders.Count)];
                float fearPenalty = target.Morale < MoralePenaltyThreshold ? MoralePenaltyMultiplier : 0f;
                // Derive defender defense from PainTolerance (0-100 → 0-15 effective range).
                float defenderDef = target.PainTolerance / 100f * 15f * (1f - fearPenalty);
                float rawDps      = invader.Attack - defenderDef;
                float damage      = Math.Max(MinDamagePerSecond, rawDps) * deltaTime;

                bool died = target.ApplyHealthDelta(-damage);

                // Sustained attack erodes morale and amplifies fear
                target.Morale = Math.Max(0f, target.Morale - 2f * deltaTime);
                target.Fear   = Math.Min(100f, target.Fear  + 3f * deltaTime);

                if (died)
                {
                    EventBus.Global.Publish(new UnitDiedEvent(target));
                    defenders.Remove(target);
                    _defendersLostThisBattle++;
                }
            }

            // ------------------------------------------------------------------
            // Low-health flee check (iterate a copy; mutate original)
            // ------------------------------------------------------------------

            foreach (var defender in defenders.ToList())
            {
                if (!defender.IsAlive) continue;
                if (defender.CurrentHealth / defender.MaxHealth >= LowHealthFleePercent) continue;

                bool holdsGround = defender.Personality == PersonalityTrait.Loyal ||
                                   defender.Personality == PersonalityTrait.Aggressive;
                if (holdsGround) continue;

                defender.CurrentState = UnitState.Fleeing;
                defender.CurrentTask  = TaskType.None;
                defenders.Remove(defender);
                _defendersLostThisBattle++;
            }

            // ------------------------------------------------------------------
            // Prune dead invaders; check battle resolution
            // ------------------------------------------------------------------

            ActiveInvaders.RemoveAll(i => !i.IsAlive);
            CheckResolution(defenders, ActiveInvaders);
        }

        // -------------------------------------------------------------------------
        // Public queries
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns a combat snapshot without defender data.
        /// Use <see cref="GetCombatReport(IEnumerable{UnitData})"/> for a full picture.
        /// </summary>
        public CombatReport GetCombatReport()
        {
            float invaderPower = 0f;
            foreach (var inv in ActiveInvaders)
                if (inv.IsAlive) invaderPower += inv.Health + inv.Attack * 3f;

            return new CombatReport(
                ActiveInvaders.Count(i => i.IsAlive),
                fightingUnits:    0,
                estimatedOutcome: -invaderPower);
        }

        /// <summary>
        /// Returns a full combat snapshot including current defender strength.
        /// </summary>
        public CombatReport GetCombatReport(IEnumerable<UnitData> units)
        {
            float defenderPower = 0f;
            int   fightingCount = 0;
            foreach (var u in units)
            {
                if (!u.IsAlive || u.CurrentState != UnitState.Fighting) continue;
                defenderPower += u.CurrentHealth + u.Attack * 3f;
                fightingCount++;
            }

            float invaderPower = 0f;
            foreach (var inv in ActiveInvaders)
                if (inv.IsAlive) invaderPower += inv.Health + inv.Attack * 3f;

            return new CombatReport(
                ActiveInvaders.Count(i => i.IsAlive),
                fightingCount,
                defenderPower - invaderPower);
        }

        /// <summary>
        /// Returns the next threat level based on elapsed game time.
        /// Thresholds: 0–119 s → Skirmish | 120–239 s → Raid | 240–359 s → Assault | 360+ s → Siege.
        /// </summary>
        public ThreatLevel GetNextThreatLevel()
        {
            if (_elapsedGameTime < EscalationIntervalSecs)       return ThreatLevel.Skirmish;
            if (_elapsedGameTime < EscalationIntervalSecs * 2f)  return ThreatLevel.Raid;
            if (_elapsedGameTime < EscalationIntervalSecs * 3f)  return ThreatLevel.Assault;
            return ThreatLevel.Siege;
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private void CheckResolution(List<UnitData> defenders, List<InvaderData> liveInvaders)
        {
            bool allInvadersDead  = liveInvaders.Count == 0;
            bool allDefendersGone = defenders.Count == 0;

            if (!allInvadersDead && !allDefendersGone) return;

            // Victory: invaders all dead.
            // Defeat: no defenders remain — remaining invaders damage rooms (handled by coordinator).
            bool playerWon = allInvadersDead;
            _threatActive  = false;

            EventBus.Global.Publish(new ThreatResolvedEvent(playerWon));
        }

        private static string[] InvaderNamesFor(ThreatLevel level) => level switch
        {
            ThreatLevel.Skirmish => new[] { "Scout", "Rogue" },
            ThreatLevel.Raid     => new[] { "Ranger", "Knight", "Paladin", "Mage", "Cleric" },
            ThreatLevel.Assault  => new[] { "Crusader",      "Templar",    "Inquisitor",    "Battle Mage", "War Priest",
                                            "Champion",      "Warden",     "Captain",       "Berserker",   "Archer" },
            ThreatLevel.Siege    => new[] { "Siege Breaker", "Grand Paladin",  "Arch Mage",      "Lord Inquisitor",
                                            "War Marshal",   "Dragon Slayer",  "Holy Avenger",   "Battle Bishop",
                                            "Iron Juggernaut","Storm Knight",  "Void Hunter",    "Lich Bane",
                                            "Light Bringer", "Titan Guard",    "Doom Slayer",    "Myth Breaker",
                                            "Saint Warrior", "Sky Lancer",     "Peak Conqueror", "Last Hope" },
            _                    => new[] { "Invader" }
        };
    }
}

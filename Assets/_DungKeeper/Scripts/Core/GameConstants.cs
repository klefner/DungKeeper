using System;

namespace DungKeeper
{
    // -------------------------------------------------------------------------
    // Enumerations
    // -------------------------------------------------------------------------

    /// <summary>Functional role a unit fills inside the dungeon.</summary>
    public enum UnitRole
    {
        Worker,
        Guard,
        Researcher,
        Torturer,
        Summoner
    }

    /// <summary>Current behavioural state of a unit.</summary>
    public enum UnitState
    {
        Idle,
        Working,
        Resting,
        Eating,
        Fighting,
        Fleeing,
        Rebelling,
        Imprisoned,
        Impressed,
        Dead
    }

    /// <summary>Personality archetype that modifies unit reactions to stimuli.</summary>
    public enum PersonalityTrait
    {
        Cowardly,
        Aggressive,
        Masochistic,
        Loyal,
        Mercenary,
        Lazy
    }

    /// <summary>Types of rooms that can be constructed in the dungeon.</summary>
    public enum RoomType
    {
        ProductionChamber,
        BarracksPit,
        ResearchVault,
        TortureDen,
        SummoningCircle,
        FeastingHall,
        TreasureVault
    }

    /// <summary>Discrete tasks a unit can perform.</summary>
    public enum TaskType
    {
        None,
        MineGold,
        ProcessEssence,
        Guard,
        Research,
        Torture,
        Feast,
        Rest,
        Fight,
        Patrol
    }

    /// <summary>Escalating severity of an external incursion.</summary>
    public enum ThreatLevel
    {
        None,
        Skirmish,
        Raid,
        Assault,
        Siege
    }

    /// <summary>Possible outcomes when a unit is slapped by the overlord.</summary>
    public enum SlapResponse
    {
        SpeedUp,
        BecomeExcited,
        BecomeAngry,
        Ignore,
        Quit,
        Rebel
    }

    /// <summary>Economy resources tracked at the dungeon level.</summary>
    public enum ResourceType
    {
        Gold,
        Essence,
        Fear
    }

    /// <summary>Forms of collective action taken by rebelling units.</summary>
    public enum StrikeType
    {
        SlowDown,
        Walkout,
        Sabotage,
        FightEachOther,
        FullRebellion
    }

    // -------------------------------------------------------------------------
    // Static constants (compile-time defaults)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Global read-only constants used as fallback defaults and hard limits.
    /// Prefer <see cref="GameSettings"/> for runtime-tunable values.
    /// </summary>
    public static class GameConstants
    {
        // Slap mechanics
        public const float BaseSlapTolerance       = 10f;
        public const float MaxSlapTolerance        = 50f;
        public const float SlapCooldownSeconds     = 2f;
        public const float SlapAccumulationDecay   = 5f;   // units per second

        // Atmosphere / economy decay
        public const float FearDecayRate           = 0.5f;  // per second
        public const float MoraleRecoveryRate      = 1f;    // per second
        public const float AngerDecayRate          = 0.3f;  // per second

        // Biological needs
        public const float HungerRate              = 0.1f;  // per second
        public const float FatigueRate             = 0.08f; // per second

        // Thresholds
        public const float HungerDistressThreshold  = 70f;
        public const float FatigueDistressThreshold = 80f;
        public const float AngerRebellionThreshold  = 90f;
        public const float FearStressThreshold      = 80f;
        public const float LowHealthFleeThreshold   = 25f;  // % of MaxHealth

        // Population limits
        public const int MaxUnitCount              = 50;
        public const int MaxRoomCount              = 20;

        // Combat
        public const float BaseCombatDamagePerSecond = 5f;
        public const float FleeSpeedMultiplier       = 1.5f;

        // Rebellion
        public const float RebellionBaseDuration    = 30f;  // seconds
        public const float RoomSabotageDamagePerSecond = 2f;
        public const float ConversionRadius          = 10f;  // world units

        // Impressed state
        public const float ImpressedDuration         = 15f;  // seconds
        public const float ImpressedProductivityBonus = 25f; // flat bonus

        // Rest / eat
        public const float RestRecoveryRate          = 5f;   // fatigue per second
        public const float EatRecoveryRate           = 4f;   // hunger per second
    }

    // -------------------------------------------------------------------------
    // Runtime-tunable settings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Balance configuration that can be serialized to JSON / ScriptableObject
    /// and tweaked at runtime or in the Unity Inspector without a recompile.
    /// All fields default to the matching <see cref="GameConstants"/> value.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        // -- Slap mechanics ---------------------------------------------------
        public float BaseSlapTolerance       = GameConstants.BaseSlapTolerance;
        public float MaxSlapTolerance        = GameConstants.MaxSlapTolerance;
        public float SlapCooldownSeconds     = GameConstants.SlapCooldownSeconds;
        public float SlapAccumulationDecay   = GameConstants.SlapAccumulationDecay;

        // -- Atmosphere -------------------------------------------------------
        public float FearDecayRate           = GameConstants.FearDecayRate;
        public float MoraleRecoveryRate      = GameConstants.MoraleRecoveryRate;
        public float AngerDecayRate          = GameConstants.AngerDecayRate;

        // -- Biological needs -------------------------------------------------
        public float HungerRate              = GameConstants.HungerRate;
        public float FatigueRate             = GameConstants.FatigueRate;

        // -- Distress thresholds ----------------------------------------------
        public float HungerDistressThreshold  = GameConstants.HungerDistressThreshold;
        public float FatigueDistressThreshold = GameConstants.FatigueDistressThreshold;
        public float AngerRebellionThreshold  = GameConstants.AngerRebellionThreshold;
        public float FearStressThreshold      = GameConstants.FearStressThreshold;
        public float LowHealthFleeThreshold   = GameConstants.LowHealthFleeThreshold;

        // -- Population -------------------------------------------------------
        public int MaxUnitCount              = GameConstants.MaxUnitCount;
        public int MaxRoomCount              = GameConstants.MaxRoomCount;

        // -- Combat -----------------------------------------------------------
        public float BaseCombatDamagePerSecond = GameConstants.BaseCombatDamagePerSecond;
        public float FleeSpeedMultiplier       = GameConstants.FleeSpeedMultiplier;

        // -- Rebellion --------------------------------------------------------
        public float RebellionBaseDuration     = GameConstants.RebellionBaseDuration;
        public float RoomSabotageDamagePerSecond = GameConstants.RoomSabotageDamagePerSecond;
        public float ConversionRadius          = GameConstants.ConversionRadius;

        // -- Impressed --------------------------------------------------------
        public float ImpressedDuration         = GameConstants.ImpressedDuration;
        public float ImpressedProductivityBonus = GameConstants.ImpressedProductivityBonus;

        // -- Rest / eat -------------------------------------------------------
        public float RestRecoveryRate          = GameConstants.RestRecoveryRate;
        public float EatRecoveryRate           = GameConstants.EatRecoveryRate;

        /// <summary>Returns a settings instance pre-populated with all defaults.</summary>
        public static GameSettings Default() => new GameSettings();
    }
}

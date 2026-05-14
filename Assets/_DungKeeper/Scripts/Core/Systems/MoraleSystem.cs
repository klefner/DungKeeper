using System.Collections.Generic;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Ticks per-unit morale, fear, and anger decay each simulation frame.
    /// Also checks need-distress thresholds (hunger, fatigue) and applies
    /// morale penalties when units are starving or exhausted.
    ///
    /// Pure C# — no MonoBehaviour dependency.
    /// </summary>
    public sealed class MoraleSystem
    {
        private readonly GameSettings _settings;

        public MoraleSystem(GameSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Advances morale simulation by <paramref name="deltaTime"/> seconds for all living units.
        /// </summary>
        public void Tick(IList<UnitData> units, float deltaTime)
        {
            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (!unit.IsAlive) continue;

                // Natural decay / recovery
                unit.Fear   = Mathf.Max(0f, unit.Fear   - _settings.FearDecayRate        * deltaTime);
                unit.Morale = Mathf.Min(100f, unit.Morale + _settings.MoraleRecoveryRate  * deltaTime);
                unit.Anger  = Mathf.Max(0f, unit.Anger  - _settings.AngerDecayRate        * deltaTime);

                // Hunger accumulation
                unit.Hunger  = Mathf.Clamp(unit.Hunger  + _settings.HungerRate  * deltaTime, 0f, 100f);
                unit.Fatigue = Mathf.Clamp(unit.Fatigue + _settings.FatigueRate * deltaTime, 0f, 100f);

                // Distress penalties
                if (unit.Hunger > _settings.HungerDistressThreshold)
                {
                    float severity = (unit.Hunger - _settings.HungerDistressThreshold) / (100f - _settings.HungerDistressThreshold);
                    unit.Morale = Mathf.Max(0f, unit.Morale - severity * 5f * deltaTime);
                    unit.Anger  = Mathf.Min(100f, unit.Anger + severity * 3f * deltaTime);
                }

                if (unit.Fatigue > _settings.FatigueDistressThreshold)
                {
                    float severity = (unit.Fatigue - _settings.FatigueDistressThreshold) / (100f - _settings.FatigueDistressThreshold);
                    unit.Morale     = Mathf.Max(0f, unit.Morale - severity * 4f * deltaTime);
                    unit.Productivity = Mathf.Max(0.1f, unit.Productivity - severity * 0.5f * deltaTime);
                }

                // Rebellion anger threshold check — state transition is handled by GameManager.
                // MoraleSystem only flags; it does not mutate CurrentState directly.
            }
        }
    }
}

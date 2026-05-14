using System.Collections.Generic;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Tracks active unit rebellions and advances their timers.
    /// Selects a <see cref="StrikeType"/> based on unit personality and anger level,
    /// and fires <see cref="UnitRebellionStartedEvent"/> for newly rebelling units.
    ///
    /// Pure C# — no MonoBehaviour dependency.
    /// </summary>
    public sealed class StrikeSystem
    {
        private readonly GameSettings _settings;

        // Per-unit rebellion timer keyed by unit ID.
        private readonly Dictionary<string, float> _rebellionTimers
            = new Dictionary<string, float>();

        public StrikeSystem(GameSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Begins a rebellion for <paramref name="unit"/> if not already rebelling.
        /// Chooses the appropriate <see cref="StrikeType"/> and fires the event.
        /// </summary>
        public void StartRebellion(UnitData unit)
        {
            if (unit == null || !unit.IsAlive) return;
            if (_rebellionTimers.ContainsKey(unit.Id)) return; // already tracked

            StrikeType strikeType = ChooseStrikeType(unit);
            _rebellionTimers[unit.Id] = _settings.RebellionBaseDuration;
            unit.State = UnitState.Rebelling;

            Debug.Log($"[StrikeSystem] {unit.Name} begins rebellion: {strikeType}");
            EventBus.Global.Publish(new UnitRebellionStartedEvent(unit, strikeType));
        }

        /// <summary>
        /// Ticks all active rebellions. Units whose timer expires return to Idle.
        /// Rebelling units may sabotage nearby rooms (damage applied externally by GameManager).
        /// </summary>
        public void Tick(IList<UnitData> units, IList<RoomData> rooms, float deltaTime)
        {
            var expired = new List<string>();

            foreach (var kv in _rebellionTimers)
            {
                string unitId = kv.Key;
                float  timer  = kv.Value - deltaTime;

                UnitData unit = FindUnit(units, unitId);
                if (unit == null || !unit.IsAlive)
                {
                    expired.Add(unitId);
                    continue;
                }

                if (timer <= 0f)
                {
                    // Rebellion over — unit calms down (partially)
                    unit.State  = UnitState.Idle;
                    unit.Anger  = Mathf.Max(0f, unit.Anger - 30f);
                    unit.Morale = Mathf.Max(0f, unit.Morale - 10f);
                    expired.Add(unitId);
                }
                else
                {
                    _rebellionTimers[unitId] = timer;

                    // Sabotage: damage assigned room
                    if (!string.IsNullOrEmpty(unit.AssignedRoomId))
                    {
                        RoomData room = FindRoom(rooms, unit.AssignedRoomId);
                        room?.ApplyDamage(_settings.RoomSabotageDamagePerSecond * deltaTime);
                    }
                }
            }

            foreach (string id in expired)
                _rebellionTimers.Remove(id);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static StrikeType ChooseStrikeType(UnitData unit)
        {
            // Fully furious + aggressive → full rebellion
            if (unit.Anger >= 95f && unit.Personality == PersonalityTrait.Aggressive)
                return StrikeType.FullRebellion;

            // Aggressive personality → fight each other or sabotage
            if (unit.Personality == PersonalityTrait.Aggressive)
                return unit.Anger >= 80f ? StrikeType.FightEachOther : StrikeType.Sabotage;

            // Lazy → walkout
            if (unit.Personality == PersonalityTrait.Lazy)
                return StrikeType.Walkout;

            // Mercenary → walkout (leaves for better pay)
            if (unit.Personality == PersonalityTrait.Mercenary)
                return StrikeType.Walkout;

            // Cowardly → slow down (too scared to actually rebel hard)
            if (unit.Personality == PersonalityTrait.Cowardly)
                return StrikeType.SlowDown;

            // Default
            return unit.Anger >= 90f ? StrikeType.Sabotage : StrikeType.SlowDown;
        }

        private static UnitData FindUnit(IList<UnitData> units, string id)
        {
            for (int i = 0; i < units.Count; i++)
                if (units[i].Id == id) return units[i];
            return null;
        }

        private static RoomData FindRoom(IList<RoomData> rooms, string id)
        {
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].Id == id) return rooms[i];
            return null;
        }
    }
}

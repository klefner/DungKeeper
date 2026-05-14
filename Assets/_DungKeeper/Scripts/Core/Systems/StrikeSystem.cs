using System;
using System.Collections.Generic;
using System.Text;

namespace DungKeeper
{
    // =========================================================================
    // StrikeReport — point-in-time snapshot for UI / AI
    // =========================================================================

    /// <summary>A point-in-time snapshot of all active strike activity.</summary>
    public sealed class StrikeReport
    {
        /// <summary>Total number of units currently in any strike action.</summary>
        public int ActiveStrikers { get; }

        /// <summary>Number of units actively sabotaging their assigned room.</summary>
        public int Sabotaging { get; }

        /// <summary>True if at least one FullRebellion strike is active.</summary>
        public bool FullRebellionActive { get; }

        /// <summary>
        /// Estimated fraction of production lost due to all active strikes (0–1).
        /// Calculated as: (SlowDown × 0.6 + Sabotage × 0.4 + Walkout × 1 + FullRebellion × 1) / totalActiveWorkers.
        /// </summary>
        public float ProductionLoss { get; }

        public StrikeReport(int activeStrikers, int sabotaging, bool fullRebellionActive, float productionLoss)
        {
            ActiveStrikers       = activeStrikers;
            Sabotaging           = sabotaging;
            FullRebellionActive  = fullRebellionActive;
            ProductionLoss       = productionLoss;
        }

        public override string ToString()
            => $"[Strike Report | Strikers:{ActiveStrikers} Saboteurs:{Sabotaging} " +
               $"FullRebellion:{FullRebellionActive} ProdLoss:{ProductionLoss * 100f:F1}%]";
    }

    // =========================================================================
    // StrikeSystem
    // =========================================================================

    /// <summary>
    /// Tracks and advances active unit strikes and rebellions.
    ///
    /// <b>Strike types and their effects each tick:</b>
    /// <list type="bullet">
    ///   <item><see cref="StrikeType.SlowDown"/>       — unit Productivity multiplied by 0.4; no room damage.</item>
    ///   <item><see cref="StrikeType.Walkout"/>         — unit transitions to Fleeing; eventually dies/leaves.</item>
    ///   <item><see cref="StrikeType.Sabotage"/>        — periodically reduces assigned room ProductionRate by 10%;
    ///                                                    may also drain a small amount of Gold.</item>
    ///   <item><see cref="StrikeType.FightEachOther"/>  — two rebelling units deal health damage to each other each tick.</item>
    ///   <item><see cref="StrikeType.FullRebellion"/>   — unit is Rebelling; spreads rebellion to adjacent low-loyalty
    ///                                                    units (Loyalty &lt; 35); damages rooms; drains resources.</item>
    /// </list>
    ///
    /// <b>Strike ends when any of:</b>
    /// <list type="bullet">
    ///   <item>Duration elapses.</item>
    ///   <item>Unit Fear spikes above 85 (slap suppression).</item>
    ///   <item>Unit dies.</item>
    ///   <item>Unit is imprisoned.</item>
    /// </list>
    ///
    /// <para>Pure C# — no Unity or MonoBehaviour dependency.</para>
    /// </summary>
    public sealed class StrikeSystem
    {
        // -------------------------------------------------------------------------
        // Tuning constants
        // -------------------------------------------------------------------------

        private const float SlowDownProductivityMultiplier = 0.40f;
        private const float SabotageProductionRatePenalty  = 0.10f;  // −10% per sabotage pulse
        private const float SabotageGoldDrain              = 5f;      // Gold drained per sabotage pulse
        private const float SabotageIntervalSeconds        = 5f;      // How often the pulse fires
        private const float FightEachOtherDamagePerSecond  = 3f;      // Health lost per second during FightEachOther
        private const float FullRebellionRoomDamagePerSec  = GameConstants.RoomSabotageDamagePerSecond;
        private const float FullRebellionGoldDrainPerSec   = 1f;
        private const float RebellionContagionLoyaltyThres = 35f;     // Units below this can be converted
        private const float FearSuppressionThreshold       = 85f;     // Fear above this ends the strike
        private const float WalkoutHealthDrainPerSec       = 2f;      // Health cost while walking out
        private const float WalkoutLeaveDuration           = 10f;     // Seconds before the unit IsAlive = false

        // -------------------------------------------------------------------------
        // Strike record
        // -------------------------------------------------------------------------

        private sealed class StrikeRecord
        {
            public UnitData   Unit;
            public StrikeType StrikeType;
            public float      StartTime;
            public float      Duration;

            // SlowDown: original productivity to restore on end
            public float OriginalProductivity;

            // Sabotage: time of the last damage pulse
            public float LastSabotagePulse;

            // Walkout: time the unit transitioned to Fleeing
            public float WalkoutStartTime;
            public bool  WalkoutStarted;

            // FightEachOther: partner (may be null if partner left)
            public UnitData FightPartner;
        }

        // -------------------------------------------------------------------------
        // State
        // -------------------------------------------------------------------------

        // Keyed by unit ID; one record per striking unit.
        private readonly Dictionary<string, StrikeRecord> _strikes
            = new Dictionary<string, StrikeRecord>(StringComparer.Ordinal);

        /// <summary>
        /// Exposed read-only view for external inspection (e.g. GameManager, tests).
        /// Each element: (unit, strikeType, startTime, duration).
        /// </summary>
        public IEnumerable<(UnitData unit, StrikeType strikeType, float startTime, float duration)> ActiveStrikes
        {
            get
            {
                foreach (var kv in _strikes)
                    yield return (kv.Value.Unit, kv.Value.StrikeType, kv.Value.StartTime, kv.Value.Duration);
            }
        }

        private readonly GameSettings _settings;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public StrikeSystem(GameSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        // -------------------------------------------------------------------------
        // StartStrike
        // -------------------------------------------------------------------------

        /// <summary>
        /// Begins a strike for <paramref name="unit"/> of the given <paramref name="type"/>
        /// at <paramref name="currentTime"/>. If the unit is already striking, the existing
        /// strike is replaced (escalation).
        /// Publishes <see cref="UnitRebellionStartedEvent"/> via <see cref="EventBus.Global"/>.
        /// </summary>
        public void StartStrike(UnitData unit, StrikeType type, float currentTime)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (!unit.IsAlive || unit.IsImprisoned) return;

            float duration = _settings.RebellionBaseDuration;

            // Apply immediate state transition
            switch (type)
            {
                case StrikeType.SlowDown:
                    unit.CurrentState = UnitState.Working;   // still at station but slow
                    break;

                case StrikeType.Walkout:
                case StrikeType.FullRebellion:
                    unit.CurrentState = UnitState.Rebelling;
                    break;

                case StrikeType.Sabotage:
                    unit.CurrentState = UnitState.Rebelling;
                    break;

                case StrikeType.FightEachOther:
                    unit.CurrentState = UnitState.Rebelling;
                    break;
            }

            float originalProductivity = unit.Productivity;

            if (_strikes.TryGetValue(unit.Id, out var existing))
            {
                // Restore previous SlowDown if we're replacing it
                if (existing.StrikeType == StrikeType.SlowDown)
                    unit.Productivity = existing.OriginalProductivity;
            }

            var record = new StrikeRecord
            {
                Unit                 = unit,
                StrikeType           = type,
                StartTime            = currentTime,
                Duration             = duration,
                OriginalProductivity = originalProductivity,
                LastSabotagePulse    = currentTime,
                WalkoutStartTime     = 0f,
                WalkoutStarted       = false,
                FightPartner         = null
            };

            _strikes[unit.Id] = record;
            unit.RebelCount++;

            EventBus.Global.Publish(new UnitRebellionStartedEvent(unit, type));
        }

        // -------------------------------------------------------------------------
        // SuppressStrike
        // -------------------------------------------------------------------------

        /// <summary>
        /// Called when the player slaps a rebelling unit hard enough.
        /// Raises the unit's Fear past the suppression threshold and ends the strike.
        /// </summary>
        public void SuppressStrike(UnitData unit)
        {
            if (unit == null) return;
            if (!_strikes.ContainsKey(unit.Id)) return;

            // Spike fear past suppression threshold to trigger natural end
            unit.Fear = Math.Min(100f, FearSuppressionThreshold + 5f);
            EndStrike(unit.Id, calmDown: true);
        }

        // -------------------------------------------------------------------------
        // ImprisonUnit
        // -------------------------------------------------------------------------

        /// <summary>
        /// Moves <paramref name="unit"/> to the <see cref="UnitState.Imprisoned"/> state
        /// and removes it from all active strikes.
        /// </summary>
        public void ImprisonUnit(UnitData unit)
        {
            if (unit == null) return;

            unit.CurrentState = UnitState.Imprisoned;
            unit.IsImprisoned = true;
            unit.CurrentTask  = TaskType.None;

            // Restore productivity in case it was a SlowDown
            if (_strikes.TryGetValue(unit.Id, out var record) &&
                record.StrikeType == StrikeType.SlowDown)
            {
                unit.Productivity = record.OriginalProductivity;
            }

            _strikes.Remove(unit.Id);

            // Also remove this unit as a FightEachOther partner in other strikes
            foreach (var kv in _strikes)
            {
                if (kv.Value.FightPartner == unit)
                    kv.Value.FightPartner = null;
            }
        }

        // -------------------------------------------------------------------------
        // Tick — primary simulation entry point
        // -------------------------------------------------------------------------

        /// <summary>
        /// Advances all active strikes by <paramref name="deltaTime"/> seconds.
        /// </summary>
        /// <param name="units">All dungeon units (read for contagion checks).</param>
        /// <param name="currentTime">Monotonic game time in seconds.</param>
        /// <param name="deltaTime">Elapsed seconds since last tick.</param>
        /// <param name="resources">Resource system for Gold drain and sabotage.</param>
        /// <param name="rooms">All dungeon rooms (read/write for sabotage damage).</param>
        public void Tick(
            IEnumerable<UnitData> units,
            float currentTime,
            float deltaTime,
            ResourceSystem resources,
            IEnumerable<RoomData> rooms)
        {
            if (deltaTime <= 0f) return;

            // Materialise so we can iterate multiple times
            var unitList = units as IList<UnitData> ?? new List<UnitData>(units);
            var roomList = rooms as IList<RoomData> ?? new List<RoomData>(rooms);

            var toEnd = new List<string>(4);

            foreach (var kv in _strikes)
            {
                string     id     = kv.Key;
                StrikeRecord rec  = kv.Value;
                UnitData   unit   = rec.Unit;

                // ---- Natural termination checks ---------------------------------

                if (!unit.IsAlive || unit.IsImprisoned)
                {
                    toEnd.Add(id);
                    continue;
                }

                float elapsed = currentTime - rec.StartTime;

                if (elapsed >= rec.Duration)
                {
                    toEnd.Add(id);
                    continue;
                }

                if (unit.Fear >= FearSuppressionThreshold)
                {
                    toEnd.Add(id);
                    EndStrikeImmediate(unit, rec, calmDown: true);
                    continue;
                }

                // ---- Per-type effects -------------------------------------------

                switch (rec.StrikeType)
                {
                    // ---- SlowDown -----------------------------------------------
                    case StrikeType.SlowDown:
                        // Clamp productivity each tick (other systems may have changed it)
                        unit.Productivity = Math.Min(unit.Productivity, rec.OriginalProductivity * SlowDownProductivityMultiplier);
                        break;

                    // ---- Walkout ------------------------------------------------
                    case StrikeType.Walkout:
                        if (!rec.WalkoutStarted)
                        {
                            unit.CurrentState    = UnitState.Fleeing;
                            rec.WalkoutStartTime = currentTime;
                            rec.WalkoutStarted   = true;
                        }

                        // Gradual health drain as the unit "leaves the dungeon"
                        unit.ApplyHealthDelta(-WalkoutHealthDrainPerSec * deltaTime);

                        if (!unit.IsAlive)
                        {
                            EventBus.Global.Publish(new UnitDiedEvent(unit));
                            toEnd.Add(id);
                            break;
                        }

                        // After WalkoutLeaveDuration, the unit is considered gone
                        if (currentTime - rec.WalkoutStartTime >= WalkoutLeaveDuration)
                        {
                            unit.Kill();
                            EventBus.Global.Publish(new UnitDiedEvent(unit));
                            toEnd.Add(id);
                        }
                        break;

                    // ---- Sabotage -----------------------------------------------
                    case StrikeType.Sabotage:
                        // Pulse every SabotageIntervalSeconds
                        if (currentTime - rec.LastSabotagePulse >= SabotageIntervalSeconds)
                        {
                            rec.LastSabotagePulse = currentTime;
                            ApplySabotagePulse(unit, roomList, resources);
                        }
                        break;

                    // ---- FightEachOther -----------------------------------------
                    case StrikeType.FightEachOther:
                        // Assign a partner if we don't have one yet
                        if (rec.FightPartner == null || !rec.FightPartner.IsAlive)
                            rec.FightPartner = FindFightPartner(id, unitList);

                        if (rec.FightPartner != null && rec.FightPartner.IsAlive)
                        {
                            float damage = FightEachOtherDamagePerSecond * deltaTime;

                            bool unitDied    = unit.ApplyHealthDelta(-damage);
                            bool partnerDied = rec.FightPartner.ApplyHealthDelta(-damage);

                            if (unitDied)
                            {
                                EventBus.Global.Publish(new UnitDiedEvent(unit));
                                toEnd.Add(id);
                                break;
                            }

                            if (partnerDied)
                            {
                                EventBus.Global.Publish(new UnitDiedEvent(rec.FightPartner));
                                _strikes.Remove(rec.FightPartner.Id);
                                rec.FightPartner = null;
                            }
                        }
                        break;

                    // ---- FullRebellion ------------------------------------------
                    case StrikeType.FullRebellion:
                        // Damage assigned room every tick
                        if (!string.IsNullOrEmpty(unit.AssignedRoomId))
                        {
                            var room = FindRoom(roomList, unit.AssignedRoomId);
                            if (room != null && room.IsOperational)
                            {
                                float rateDamage = FullRebellionRoomDamagePerSec * deltaTime;
                                room.ProductionRate = Math.Max(0f, room.ProductionRate - rateDamage);
                                room.Health         = Math.Max(0f, room.Health - rateDamage);
                            }
                        }

                        // Drain Gold
                        resources?.TrySpend(ResourceType.Gold, FullRebellionGoldDrainPerSec * deltaTime);

                        // Spread to adjacent low-loyalty units not already striking
                        SpreadRebellion(unit, unitList, currentTime);
                        break;
                }
            }

            // Remove ended strikes
            foreach (var id in toEnd)
            {
                if (_strikes.TryGetValue(id, out var rec))
                    EndStrikeImmediate(rec.Unit, rec, calmDown: true);
                _strikes.Remove(id);
            }
        }

        // -------------------------------------------------------------------------
        // GetReport
        // -------------------------------------------------------------------------

        /// <summary>Returns a point-in-time report of all active strike activity.</summary>
        public StrikeReport GetReport()
        {
            int   activeStrikers      = _strikes.Count;
            int   sabotaging          = 0;
            bool  fullRebellionActive = false;
            float productionLoss      = 0f;

            foreach (var kv in _strikes)
            {
                switch (kv.Value.StrikeType)
                {
                    case StrikeType.Sabotage:
                        sabotaging++;
                        productionLoss += 0.4f;
                        break;
                    case StrikeType.SlowDown:
                        productionLoss += 0.6f;
                        break;
                    case StrikeType.Walkout:
                        productionLoss += 1.0f;
                        break;
                    case StrikeType.FullRebellion:
                        fullRebellionActive = true;
                        productionLoss      += 1.0f;
                        break;
                    case StrikeType.FightEachOther:
                        productionLoss += 0.8f;
                        break;
                }
            }

            // Normalise by active strikers (avoid division by zero)
            if (activeStrikers > 0)
                productionLoss /= activeStrikers;

            return new StrikeReport(activeStrikers, sabotaging, fullRebellionActive, productionLoss);
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        private void EndStrike(string unitId, bool calmDown)
        {
            if (!_strikes.TryGetValue(unitId, out var rec)) return;
            EndStrikeImmediate(rec.Unit, rec, calmDown);
            _strikes.Remove(unitId);
        }

        private static void EndStrikeImmediate(UnitData unit, StrikeRecord rec, bool calmDown)
        {
            if (unit == null || !unit.IsAlive) return;

            switch (rec.StrikeType)
            {
                case StrikeType.SlowDown:
                    // Restore productivity (may have been further modified — restore original)
                    unit.Productivity = rec.OriginalProductivity;
                    break;

                case StrikeType.Walkout:
                case StrikeType.Sabotage:
                case StrikeType.FightEachOther:
                case StrikeType.FullRebellion:
                    break;
            }

            if (calmDown && unit.IsAlive && !unit.IsImprisoned)
            {
                unit.CurrentState = UnitState.Idle;
                unit.CurrentTask  = TaskType.None;
                // Partial anger reduction; morale takes a long-term hit
                unit.Anger  = Math.Max(0f, unit.Anger  - 30f);
                unit.Morale = Math.Max(0f, unit.Morale - 10f);
            }
        }

        private void ApplySabotagePulse(UnitData unit, IList<RoomData> rooms, ResourceSystem resources)
        {
            if (!string.IsNullOrEmpty(unit.AssignedRoomId))
            {
                var room = FindRoom(rooms, unit.AssignedRoomId);
                if (room != null && room.IsOperational)
                {
                    float penalty = room.ProductionRate * SabotageProductionRatePenalty;
                    room.ProductionRate = Math.Max(0f, room.ProductionRate - penalty);
                }
            }

            // Also steal a small amount of Gold
            resources?.TrySpend(ResourceType.Gold, SabotageGoldDrain);
        }

        private void SpreadRebellion(UnitData instigator, IList<UnitData> allUnits, float currentTime)
        {
            for (int i = 0; i < allUnits.Count; i++)
            {
                var candidate = allUnits[i];
                if (!candidate.IsAlive)                          continue;
                if (candidate.Id == instigator.Id)               continue;
                if (_strikes.ContainsKey(candidate.Id))          continue;
                if (candidate.IsImprisoned)                      continue;
                if (candidate.CurrentState == UnitState.Dead)    continue;
                if (candidate.Loyalty >= RebellionContagionLoyaltyThres) continue;

                // Spread with a probability proportional to how low their loyalty is
                // and how low the instigator's morale already is.
                float spreadChance = (1f - candidate.Loyalty / 100f) * 0.03f; // 3% max per second per tick
                // (We approximate by checking a fixed threshold rather than using Random here
                //  so as not to require per-system RNG injection; callers can seed if needed.)
                if (candidate.Loyalty < RebellionContagionLoyaltyThres * 0.5f)
                {
                    // Very low loyalty — always spread
                    StartStrike(candidate, StrikeType.SlowDown, currentTime);
                }
            }
        }

        private static UnitData FindFightPartner(string excludeId, IList<UnitData> units)
        {
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u.IsAlive)          continue;
                if (u.Id == excludeId)   continue;
                if (u.CurrentState == UnitState.Rebelling) return u;
            }
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

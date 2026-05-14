using System;
using System.Collections.Generic;
using System.Linq;

namespace DungKeeper
{
    // =========================================================================
    // TaskSystem
    // =========================================================================

    /// <summary>
    /// Pure-C# task assignment system.  Evaluates idle units and assigns them
    /// to the most appropriate available room, then maps that room type to a
    /// concrete <see cref="TaskType"/>.
    ///
    /// Design constraints:
    ///   - No Unity dependency; all state lives on <see cref="UnitData"/> and
    ///     <see cref="RoomData"/>.
    ///   - Assignment decisions are deterministic; no RNG is involved.
    ///   - Priority scoring is purely additive so callers can extend it via
    ///     <see cref="GetPriorityScore"/>.
    ///   - The system does not fire events; callers subscribe to unit-state
    ///     changes through the <see cref="EventBus"/> if needed.
    ///
    /// Intended usage:
    /// <code>
    ///   // Each tick (or on demand when idle units appear)
    ///   taskSystem.AssignTasks(dungeon.Units, dungeon.Rooms, settings);
    /// </code>
    /// </summary>
    public sealed class TaskSystem
    {
        // ------------------------------------------------------------------ //
        // Priority score constants — additive modifiers applied per rule
        // ------------------------------------------------------------------ //

        // Role match
        private const int ScoreRoleMatch            =  40;
        private const int ScoreRoleMismatch         = -20;

        // Need satisfaction (urgent needs score very high)
        private const int ScoreHungerNeedFeast      =  60;  // hungry → FeastingHall
        private const int ScoreFatigueNeedRest      =  60;  // fatigued → BarracksPit rest
        private const int ScoreHungerInNonFeast     = -30;  // hungry being assigned elsewhere

        // Personality bonuses
        private const int ScoreMasochisticTorture   =  25;  // Masochistic → TortureDen
        private const int ScoreLoyalAnyTask         =  10;  // Loyal accept almost anything
        private const int ScoreMercenaryProduction  =  15;  // Mercenary prefer gold-generating rooms
        private const int ScoreLazyPenalty          = -15;  // Lazy penalised for active rooms
        private const int ScoreAggressiveGuard      =  20;  // Aggressive prefer BarracksPit/Guard

        // Room demand (favour under-staffed rooms)
        private const int ScoreRoomUnderstaffed     =  20;  // < 50% capacity filled
        private const int ScoreRoomAtCapacity       = -50;  // room is at/over capacity

        // Mercenary gold calculation thresholds (use Productivity as pay proxy)
        private const float MercenaryAcceptablePayProxy = 40f;

        // ------------------------------------------------------------------ //
        // Primary API — AssignTasks
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Evaluates every <see cref="UnitState.Idle"/> unit in
        /// <paramref name="units"/> and assigns each to the best available room
        /// according to its role, personality, and current needs.
        ///
        /// Mutates <see cref="UnitData.AssignedRoomId"/>,
        /// <see cref="UnitData.CurrentTask"/>, <see cref="UnitData.CurrentState"/>,
        /// and the target <see cref="RoomData.AssignedUnitIds"/> list.
        /// </summary>
        /// <param name="units">Full dungeon population (non-null).</param>
        /// <param name="rooms">All available rooms (non-null).</param>
        /// <param name="settings">Active balance settings (non-null).</param>
        public void AssignTasks(
            IEnumerable<UnitData> units,
            IEnumerable<RoomData> rooms,
            GameSettings settings)
        {
            if (units    == null) throw new ArgumentNullException(nameof(units));
            if (rooms    == null) throw new ArgumentNullException(nameof(rooms));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            // Materialise to allow repeated enumeration
            var roomList = rooms as IReadOnlyList<RoomData> ?? rooms.ToList();

            foreach (UnitData unit in units)
            {
                // Only assign idle, living units that have no current assignment
                if (!unit.IsAlive)                           continue;
                if (unit.CurrentState != UnitState.Idle)     continue;
                if (unit.AssignedRoomId != null)             continue;

                // --------------------------------------------------------
                // Urgent need override: hungry → FeastingHall
                // --------------------------------------------------------
                if (unit.Hunger > settings.HungerDistressThreshold)
                {
                    RoomData feast = FindAvailableRoom(roomList, RoomType.FeastingHall, unit);
                    if (feast != null)
                    {
                        AssignUnitToRoom(unit, feast, TaskType.Feast);
                        continue;
                    }
                }

                // --------------------------------------------------------
                // Urgent need override: fatigued → BarracksPit (rest)
                // --------------------------------------------------------
                if (unit.Fatigue > settings.FatigueDistressThreshold)
                {
                    RoomData barracks = FindAvailableRoom(roomList, RoomType.BarracksPit, unit);
                    if (barracks != null)
                    {
                        AssignUnitToRoom(unit, barracks, TaskType.Rest);
                        continue;
                    }
                }

                // --------------------------------------------------------
                // Scored best-fit assignment
                // --------------------------------------------------------
                RoomData bestRoom  = null;
                int      bestScore = int.MinValue;

                foreach (RoomData room in roomList)
                {
                    if (!room.IsActive || !room.IsOperational) continue;
                    if (room.AssignedUnitIds.Count >= room.Capacity) continue;

                    int score = GetPriorityScore(unit, room);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRoom  = room;
                    }
                }

                if (bestRoom != null && bestScore >= 0)
                {
                    TaskType task = RoomTypeToTask(bestRoom.Type, unit);
                    AssignUnitToRoom(unit, bestRoom, task);
                }
                // If no room scores >= 0 the unit remains Idle
            }
        }

        // ------------------------------------------------------------------ //
        // UnassignUnit
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Removes <paramref name="unit"/> from its currently assigned room,
        /// clears its task, and returns it to the <see cref="UnitState.Idle"/> state.
        /// Safe to call even if the unit has no current assignment.
        /// </summary>
        public void UnassignUnit(UnitData unit, IEnumerable<RoomData> rooms)
        {
            if (unit  == null) throw new ArgumentNullException(nameof(unit));
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));

            if (unit.AssignedRoomId != null)
            {
                foreach (RoomData room in rooms)
                {
                    if (room.Id == unit.AssignedRoomId)
                    {
                        room.AssignedUnitIds.Remove(unit.Id);
                        break;
                    }
                }
            }

            unit.AssignedRoomId = null;
            unit.CurrentTask    = TaskType.None;

            // Only transition to Idle if currently Working / Resting / Eating.
            // Leave other states (Fighting, Rebelling, etc.) alone.
            if (unit.CurrentState == UnitState.Working
                || unit.CurrentState == UnitState.Resting
                || unit.CurrentState == UnitState.Eating)
            {
                unit.CurrentState = UnitState.Idle;
            }
        }

        // ------------------------------------------------------------------ //
        // GetIdealTask
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns the ideal <see cref="TaskType"/> for a unit based purely on
        /// its <see cref="UnitRole"/>, ignoring current needs or room availability.
        /// Used by the AI planner for long-range scheduling.
        /// </summary>
        public TaskType GetIdealTask(UnitData unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));

            return unit.Role switch
            {
                UnitRole.Worker     => TaskType.MineGold,
                UnitRole.Guard      => TaskType.Guard,
                UnitRole.Researcher => TaskType.Research,
                UnitRole.Torturer   => TaskType.Torture,
                UnitRole.Summoner   => TaskType.ProcessEssence,
                _                   => TaskType.None
            };
        }

        // ------------------------------------------------------------------ //
        // GetPriorityScore
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Returns a signed integer priority score representing how well
        /// <paramref name="unit"/> fits <paramref name="room"/>.
        /// Higher score = better fit.  Negative scores are treated as
        /// "do not assign" by <see cref="AssignTasks"/>.
        ///
        /// Factors considered:
        ///   - Role match / mismatch with room type
        ///   - Personality preferences and aversions
        ///   - Immediate biological needs
        ///   - Current room staffing level (under/over capacity)
        ///   - Mercenary pay satisfaction
        /// </summary>
        public int GetPriorityScore(UnitData unit, RoomData room)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            if (room == null) throw new ArgumentNullException(nameof(room));

            int score = 0;

            // ----------------------------------------------------------------
            // A. Role match
            // ----------------------------------------------------------------
            bool roleMatches = RoleMatchesRoom(unit.Role, room.Type);
            score += roleMatches ? ScoreRoleMatch : ScoreRoleMismatch;

            // ----------------------------------------------------------------
            // B. Personality modifiers
            // ----------------------------------------------------------------
            score += PersonalityBonus(unit, room);

            // ----------------------------------------------------------------
            // C. Need satisfaction
            // ----------------------------------------------------------------
            if (unit.Hunger > 50f && room.Type == RoomType.FeastingHall)
                score += ScoreHungerNeedFeast;

            if (unit.Hunger > 50f && room.Type != RoomType.FeastingHall)
                score += ScoreHungerInNonFeast;

            if (unit.Fatigue > 60f && room.Type == RoomType.BarracksPit)
                score += ScoreFatigueNeedRest;

            // ----------------------------------------------------------------
            // D. Room staffing level
            // ----------------------------------------------------------------
            float fillFraction = room.Capacity > 0
                ? (float)room.AssignedUnitIds.Count / room.Capacity
                : 1f;

            if (fillFraction >= 1f)
                score += ScoreRoomAtCapacity;
            else if (fillFraction < 0.5f)
                score += ScoreRoomUnderstaffed;

            return score;
        }

        // ------------------------------------------------------------------ //
        // Private helpers
        // ------------------------------------------------------------------ //

        private static int PersonalityBonus(UnitData unit, RoomData room)
        {
            int bonus = 0;

            switch (unit.Personality)
            {
                case PersonalityTrait.Masochistic:
                    // Masochistic units seek the TortureDen — both as torturer and subject
                    if (room.Type == RoomType.TortureDen)
                        bonus += ScoreMasochisticTorture;
                    break;

                case PersonalityTrait.Loyal:
                    // Loyal units accept any assignment willingly
                    bonus += ScoreLoyalAnyTask;
                    break;

                case PersonalityTrait.Mercenary:
                {
                    // Mercenary units strongly prefer gold-generating rooms when underpaid.
                    bool goldRoom = room.Type == RoomType.ProductionChamber
                                 || room.Type == RoomType.TreasureVault;
                    if (goldRoom)
                        bonus += ScoreMercenaryProduction;

                    // If pay proxy (Productivity) is below threshold, they resist other rooms
                    if (unit.Productivity < MercenaryAcceptablePayProxy && !goldRoom)
                        bonus -= 10;
                    break;
                }

                case PersonalityTrait.Lazy:
                    // Lazy units dislike active-work rooms
                    if (room.Type == RoomType.ProductionChamber
                        || room.Type == RoomType.TortureDen)
                        bonus += ScoreLazyPenalty;

                    // But they don't mind feasting or resting
                    if (room.Type == RoomType.FeastingHall || room.Type == RoomType.BarracksPit)
                        bonus += 10;
                    break;

                case PersonalityTrait.Aggressive:
                    // Aggressive units prefer guard / combat roles
                    if (room.Type == RoomType.BarracksPit)
                        bonus += ScoreAggressiveGuard;
                    if (room.Type == RoomType.TortureDen)
                        bonus += 10; // tolerable; close to fighting
                    break;

                case PersonalityTrait.Cowardly:
                    // Cowardly units avoid combat/torture rooms
                    if (room.Type == RoomType.BarracksPit || room.Type == RoomType.TortureDen)
                        bonus -= 15;
                    if (room.Type == RoomType.ResearchVault)
                        bonus += 10; // safe, quiet
                    break;
            }

            return bonus;
        }

        /// <summary>
        /// Returns true when the given role has a natural fit with the room type.
        /// Guards work best in barracks; researchers in vaults; etc.
        /// </summary>
        private static bool RoleMatchesRoom(UnitRole role, RoomType roomType)
        {
            return (role, roomType) switch
            {
                (UnitRole.Worker,     RoomType.ProductionChamber) => true,
                (UnitRole.Worker,     RoomType.TreasureVault)     => true,
                (UnitRole.Guard,      RoomType.BarracksPit)       => true,
                (UnitRole.Researcher, RoomType.ResearchVault)     => true,
                (UnitRole.Torturer,   RoomType.TortureDen)        => true,
                (UnitRole.Summoner,   RoomType.SummoningCircle)   => true,
                _                                                  => false
            };
        }

        /// <summary>
        /// Maps a room type to the task that units perform inside it.
        /// Accounts for personality-specific overrides (e.g. Guard resting in BarracksPit).
        /// </summary>
        private static TaskType RoomTypeToTask(RoomType roomType, UnitData unit)
        {
            return roomType switch
            {
                RoomType.ProductionChamber => TaskType.MineGold,
                RoomType.ResearchVault     => TaskType.Research,
                RoomType.TortureDen        => TaskType.Torture,
                RoomType.SummoningCircle   => TaskType.ProcessEssence,
                RoomType.FeastingHall      => TaskType.Feast,
                RoomType.TreasureVault     => TaskType.MineGold, // guards/workers protect vault
                RoomType.BarracksPit       => unit.Role == UnitRole.Guard
                                              ? TaskType.Guard
                                              : TaskType.Rest,
                _                          => TaskType.None
            };
        }

        /// <summary>
        /// Finds the first available (active, operational, under capacity) room of
        /// the given type, or null if none exist.
        /// </summary>
        private static RoomData FindAvailableRoom(
            IReadOnlyList<RoomData> rooms, RoomType type, UnitData unit)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomData r = rooms[i];
                if (r.Type != type)           continue;
                if (!r.IsActive)              continue;
                if (!r.IsOperational)         continue;
                if (r.AssignedUnitIds.Count >= r.Capacity) continue;
                return r;
            }
            return null;
        }

        /// <summary>
        /// Performs the actual assignment: updates both the unit's fields and
        /// the room's unit list, then transitions the unit out of Idle.
        /// </summary>
        private static void AssignUnitToRoom(UnitData unit, RoomData room, TaskType task)
        {
            unit.AssignedRoomId = room.Id;
            unit.CurrentTask    = task;
            unit.CurrentState   = task == TaskType.Rest  ? UnitState.Resting
                                : task == TaskType.Feast ? UnitState.Eating
                                : UnitState.Working;

            if (!room.AssignedUnitIds.Contains(unit.Id))
                room.AssignedUnitIds.Add(unit.Id);
        }
    }
}

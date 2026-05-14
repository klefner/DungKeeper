using System.Collections.Generic;

namespace DungKeeper
{
    /// <summary>
    /// Assigns and advances discrete <see cref="TaskType"/> work for each unit each tick.
    /// Routes units to the correct <see cref="RoomData"/> based on their role and the room's type.
    ///
    /// Pure C# — no MonoBehaviour dependency.
    /// </summary>
    public sealed class TaskSystem
    {
        private readonly GameSettings _settings;

        public TaskSystem(GameSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Evaluates and updates the <see cref="UnitData.CurrentTask"/> for all living units.
        /// Called once per simulation frame.
        /// </summary>
        public void Tick(IList<UnitData> units, IList<RoomData> rooms, float deltaTime)
        {
            for (int i = 0; i < units.Count; i++)
            {
                UnitData unit = units[i];
                if (!unit.IsAlive) continue;

                // Dead, fleeing, or rebelling units are not managed by task system.
                if (unit.State == UnitState.Dead      ||
                    unit.State == UnitState.Fleeing   ||
                    unit.State == UnitState.Rebelling ||
                    unit.State == UnitState.Imprisoned)
                    continue;

                // Priority: hunger → fatigue → assigned room work → idle
                if (unit.Hunger >= _settings.HungerDistressThreshold && unit.State != UnitState.Eating)
                {
                    unit.State       = UnitState.Eating;
                    unit.CurrentTask = TaskType.Feast;
                    continue;
                }

                if (unit.Fatigue >= _settings.FatigueDistressThreshold && unit.State != UnitState.Resting)
                {
                    unit.State       = UnitState.Resting;
                    unit.CurrentTask = TaskType.Rest;
                    continue;
                }

                // Eating / resting recovery
                if (unit.State == UnitState.Eating)
                {
                    unit.Hunger = System.Math.Max(0f, unit.Hunger - _settings.EatRecoveryRate * deltaTime);
                    if (unit.Hunger <= 0f)
                    {
                        unit.State       = UnitState.Idle;
                        unit.CurrentTask = TaskType.None;
                    }
                    continue;
                }

                if (unit.State == UnitState.Resting)
                {
                    unit.Fatigue = System.Math.Max(0f, unit.Fatigue - _settings.RestRecoveryRate * deltaTime);
                    if (unit.Fatigue <= 0f)
                    {
                        unit.State       = UnitState.Idle;
                        unit.CurrentTask = TaskType.None;
                    }
                    continue;
                }

                // Assigned room work
                if (!string.IsNullOrEmpty(unit.AssignedRoomId))
                {
                    RoomData room = FindRoom(rooms, unit.AssignedRoomId);
                    if (room != null && room.IsOperational)
                    {
                        unit.State       = UnitState.Working;
                        unit.CurrentTask = RoleToTask(unit.Role);
                        continue;
                    }
                }

                // Nothing to do
                unit.State       = UnitState.Idle;
                unit.CurrentTask = TaskType.None;
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static TaskType RoleToTask(UnitRole role) => role switch
        {
            UnitRole.Worker     => TaskType.MineGold,
            UnitRole.Guard      => TaskType.Guard,
            UnitRole.Researcher => TaskType.Research,
            UnitRole.Torturer   => TaskType.Torture,
            UnitRole.Summoner   => TaskType.ProcessEssence,
            _                   => TaskType.None
        };

        private static RoomData FindRoom(IList<RoomData> rooms, string id)
        {
            for (int i = 0; i < rooms.Count; i++)
                if (rooms[i].Id == id) return rooms[i];
            return null;
        }
    }
}

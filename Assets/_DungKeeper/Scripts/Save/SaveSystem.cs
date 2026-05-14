using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DungKeeper
{
    // =========================================================================
    // Save data models
    // =========================================================================

    /// <summary>
    /// Serialisable snapshot of a single unit's persistent state.
    /// </summary>
    [Serializable]
    public sealed class UnitSaveData
    {
        public string Id;
        public string Name;
        public UnitRole Role;
        public PersonalityTrait Personality;
        public UnitState State;
        public string AssignedRoomId;
        public TaskType CurrentTask;
        public float Health;
        public float MaxHealth;
        public float Hunger;
        public float Fatigue;
        public float Morale;
        public float Fear;
        public float Anger;
        public float Loyalty;
        public float Productivity;
        public float Attack;
        public float Defense;
        // World position for respawning the visual controller.
        public float PosX, PosY, PosZ;
    }

    /// <summary>
    /// Serialisable snapshot of a single room's persistent state.
    /// </summary>
    [Serializable]
    public sealed class RoomSaveData
    {
        public string Id;
        public string Name;
        public RoomType Type;
        public int Capacity;
        public float ProductionRate;
        public float BaseProductionRate;
        public float Health;
        public float MaxHealth;
        public List<string> AssignedUnitIds = new List<string>();
        // Grid position
        public int GridX, GridY, GridZ;
    }

    /// <summary>
    /// Top-level save data container.  Extend with resource totals,
    /// settings overrides, and dungeon-level metadata as the game grows.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public int SchemaVersion = 1;
        public string SaveSlot   = "default";
        public string Timestamp;
        public float GameTime;
        public List<UnitSaveData> Units = new List<UnitSaveData>();
        public List<RoomSaveData> Rooms = new List<RoomSaveData>();

        // Dungeon-level resource totals.
        public float Gold;
        public float Essence;
        public float FearResource;
    }

    // =========================================================================
    // Save system
    // =========================================================================

    /// <summary>
    /// Handles JSON serialisation and deserialisation of <see cref="SaveData"/>
    /// to and from <see cref="Application.persistentDataPath"/>.
    ///
    /// Designed to be called statically so <see cref="GameManager"/> does not
    /// need to hold a reference.  All I/O is synchronous for MVP; swap to
    /// async file writes when the save file grows large.
    /// </summary>
    public static class SaveSystem
    {
        // ------------------------------------------------------------------
        // Constants
        // ------------------------------------------------------------------

        private const string SaveDirectory = "Saves";
        private const string FileExtension = ".json";
        private const int    CurrentSchemaVersion = 1;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Writes <paramref name="data"/> to disk for the given <paramref name="slot"/>.</summary>
        /// <exception cref="IOException">Rethrown if the write fails.</exception>
        public static void Save(SaveData data, string slot = "default")
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.SaveSlot  = slot;
            data.Timestamp = DateTime.UtcNow.ToString("o");

            string path = GetPath(slot);
            EnsureSaveDirectory();

            string json = JsonUtility.ToJson(data, prettyPrint: true);
            File.WriteAllText(path, json);

            Debug.Log($"[SaveSystem] Game saved to {path}");
            EventBus.Global.Publish(new GameSavedEvent(slot));
        }

        /// <summary>
        /// Loads and returns the <see cref="SaveData"/> for <paramref name="slot"/>,
        /// or <c>null</c> if no save file exists for that slot.
        /// </summary>
        public static SaveData Load(string slot = "default")
        {
            string path = GetPath(slot);

            if (!File.Exists(path))
            {
                Debug.Log($"[SaveSystem] No save file found at {path}.");
                return null;
            }

            string json = File.ReadAllText(path);
            SaveData data = JsonUtility.FromJson<SaveData>(json);

            if (data == null)
            {
                Debug.LogWarning($"[SaveSystem] Failed to deserialise save file at {path}.");
                return null;
            }

            if (data.SchemaVersion != CurrentSchemaVersion)
            {
                Debug.LogWarning(
                    $"[SaveSystem] Schema mismatch: file={data.SchemaVersion}, " +
                    $"expected={CurrentSchemaVersion}. Migration not yet implemented.");
            }

            Debug.Log($"[SaveSystem] Loaded save from {path} (timestamp: {data.Timestamp})");
            return data;
        }

        /// <summary>Returns true if a save file exists for <paramref name="slot"/>.</summary>
        public static bool HasSave(string slot = "default")
            => File.Exists(GetPath(slot));

        /// <summary>Deletes the save file for <paramref name="slot"/>. Safe to call when file is absent.</summary>
        public static void Delete(string slot = "default")
        {
            string path = GetPath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log($"[SaveSystem] Deleted save at {path}");
            }
        }

        // ------------------------------------------------------------------
        // Private helpers
        // ------------------------------------------------------------------

        private static string GetPath(string slot)
        {
            string safeSlot = string.IsNullOrWhiteSpace(slot) ? "default" : slot;
            return Path.Combine(Application.persistentDataPath, SaveDirectory, safeSlot + FileExtension);
        }

        private static void EnsureSaveDirectory()
        {
            string dir = Path.Combine(Application.persistentDataPath, SaveDirectory);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}

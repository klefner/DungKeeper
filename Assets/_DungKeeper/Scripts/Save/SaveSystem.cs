using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DungKeeper
{
    // UnitSaveData, RoomSaveData, and SaveData are defined in SaveData.cs.

    /// <summary>
    /// Static utility class that persists <see cref="SaveData"/> to
    /// <see cref="Application.persistentDataPath"/> as XOR-obfuscated JSON.
    ///
    /// XOR obfuscation is NOT cryptographic security: it raises the bar against
    /// casual hex-editor cheating only. For real tamper-proofing, use HMAC or
    /// authenticated encryption.
    ///
    /// Thread safety: all public methods must be called from the Unity main thread
    /// because they fire <see cref="EventBus.Global"/> events and call
    /// <see cref="Application.persistentDataPath"/>.
    /// </summary>
    public static class SaveSystem
    {
        // -------------------------------------------------------------------------
        // Configuration
        // -------------------------------------------------------------------------

        private const string FileExtension = ".dksav";
        private const string SaveDirectory = "saves";

        // XOR key — changing this byte sequence invalidates all existing saves.
        // Length does not need to match the save file size; it is applied cyclically.
        private static readonly byte[] XorKey =
            Encoding.UTF8.GetBytes("DungKpr_Obfusc@2026#");

        // -------------------------------------------------------------------------
        // Public API
        // -------------------------------------------------------------------------

        /// <summary>
        /// Serializes <paramref name="data"/> to JSON, XOR-obfuscates it, and writes
        /// the result to disk. Fires <see cref="GameSavedEvent"/> on the global bus
        /// after a successful write.
        /// </summary>
        /// <param name="data">The save-data container. Must not be null.</param>
        /// <param name="slot">Save slot identifier (default: "slot0").</param>
        public static void Save(SaveData data, string slot = "slot0")
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            data.SaveSlot      = slot;
            data.SaveTimestamp = DateTime.UtcNow;

            try
            {
                string json      = JsonUtility.ToJson(data, prettyPrint: false);
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                byte[] obfuscated = XorTransform(jsonBytes);

                string path = GetSavePath(slot);
                EnsureDirectory(path);
                File.WriteAllBytes(path, obfuscated);

                Debug.Log($"[SaveSystem] Slot '{slot}' saved → {path} ({obfuscated.Length} bytes)");
                EventBus.Global.Publish(new GameSavedEvent(slot));
            }
            catch (IOException ex)
            {
                Debug.LogError($"[SaveSystem] IO error saving slot '{slot}': {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Unexpected error saving slot '{slot}': {ex}");
            }
        }

        /// <summary>
        /// Reads, de-obfuscates, and deserializes the save file for the given slot.
        /// </summary>
        /// <param name="slot">Save slot identifier (default: "slot0").</param>
        /// <returns>
        /// The deserialized <see cref="SaveData"/>, or <c>null</c> if the file does
        /// not exist, cannot be read, or is corrupt.
        /// </returns>
        public static SaveData Load(string slot = "slot0")
        {
            string path = GetSavePath(slot);

            if (!File.Exists(path))
            {
                Debug.Log($"[SaveSystem] No save file found for slot '{slot}' at {path}.");
                return null;
            }

            try
            {
                byte[]   obfuscated = File.ReadAllBytes(path);
                byte[]   jsonBytes  = XorTransform(obfuscated); // XOR is its own inverse
                string   json       = Encoding.UTF8.GetString(jsonBytes);

                var data = JsonUtility.FromJson<SaveData>(json);
                if (data == null)
                {
                    Debug.LogError($"[SaveSystem] Deserialization returned null for slot '{slot}'.");
                    return null;
                }

                Debug.Log(
                    $"[SaveSystem] Loaded slot '{slot}' " +
                    $"(version {data.Version}, saved {data.SaveTimestamp:u}, " +
                    $"play time {data.TotalPlayTime:F0} s).");

                return data;
            }
            catch (IOException ex)
            {
                Debug.LogError($"[SaveSystem] IO error reading slot '{slot}': {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveSystem] Corrupt or unreadable save for slot '{slot}': {ex.Message}");
                return null;
            }
        }

        /// <summary>Returns true if a save file exists for the given slot.</summary>
        public static bool SlotExists(string slot = "slot0")
            => File.Exists(GetSavePath(slot));

        /// <summary>
        /// Permanently deletes the save file for the given slot.
        /// No-op if the file does not exist.
        /// </summary>
        public static void DeleteSlot(string slot = "slot0")
        {
            string path = GetSavePath(slot);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    Debug.Log($"[SaveSystem] Deleted save slot '{slot}'.");
                }
            }
            catch (IOException ex)
            {
                Debug.LogError($"[SaveSystem] Failed to delete slot '{slot}': {ex.Message}");
            }
        }

        /// <summary>
        /// Returns the absolute file path for the given save slot.
        /// </summary>
        public static string GetSavePath(string slot = "slot0")
        {
            // Strip characters that are illegal in file names to prevent traversal attacks.
            string safeName = string.Concat(slot.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(safeName)) safeName = "slot0";
            return Path.Combine(Application.persistentDataPath, SaveDirectory, safeName + FileExtension);
        }

        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        /// <summary>
        /// Applies repeating XOR with <see cref="XorKey"/> to <paramref name="data"/>.
        /// Because XOR is its own inverse, calling this method twice restores the
        /// original bytes.
        /// </summary>
        private static byte[] XorTransform(byte[] data)
        {
            if (data == null || data.Length == 0) return Array.Empty<byte>();

            byte[] result = new byte[data.Length];
            int    keyLen = XorKey.Length;

            for (int i = 0; i < data.Length; i++)
                result[i] = (byte)(data[i] ^ XorKey[i % keyLen]);

            return result;
        }

        private static void EnsureDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}

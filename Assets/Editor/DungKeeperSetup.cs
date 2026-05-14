using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DungKeeper.Editor
{
    public static class DungKeeperSetup
    {
        [MenuItem("DungKeeper/Setup Scene %#s")]
        public static void SetupScene()
        {
            bool changed = false;

            var gm = EnsureGameManager(ref changed);
            EnsureGameSettings(gm, ref changed);
            EnsureUnitPrefab(gm, ref changed);
            EnsureRoomPrefabs(gm, ref changed);
            EnsureNavMesh(ref changed);

            if (changed)
            {
                EditorSceneManager.SaveOpenScenes();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log("[DungKeeperSetup] Done. " + (changed ? "Scene saved." : "Nothing needed changing."));
        }

        // ── 1. GameManager in scene ──────────────────────────────────────────

        static Component EnsureGameManager(ref bool changed)
        {
            var gmType = FindType("DungKeeper.GameManager") ?? FindType("GameManager");
            if (gmType == null)
            {
                Debug.LogError("[DungKeeperSetup] Could not find GameManager type. Is the script compiled?");
                return null;
            }

#if UNITY_2023_1_OR_NEWER
            var existing = (Component)UnityEngine.Object.FindFirstObjectByType(gmType);
#else
            var existing = (Component)UnityEngine.Object.FindObjectOfType(gmType);
#endif
            if (existing != null)
            {
                Debug.Log("[DungKeeperSetup] GameManager already in scene — skipping.");
                return existing;
            }

            var go = new GameObject("GameManager");
            var comp = (Component)go.AddComponent(gmType);
            Debug.Log("[DungKeeperSetup] Created GameManager GameObject and added component.");
            changed = true;
            return comp;
        }

        // ── 2. GameSettings ScriptableObject ────────────────────────────────

        static void EnsureGameSettings(Component gm, ref bool changed)
        {
            if (gm == null) return;

            var settingsType = FindType("DungKeeper.GameSettings") ?? FindType("GameSettings");
            if (settingsType == null)
            {
                Debug.LogWarning("[DungKeeperSetup] Could not find GameSettings type — skipping.");
                return;
            }

            const string dir = "Assets/Settings";
            const string path = "Assets/Settings/GameSettings.asset";

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var asset = AssetDatabase.LoadAssetAtPath(path, settingsType);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance(settingsType);
                AssetDatabase.CreateAsset(asset, path);
                Debug.Log($"[DungKeeperSetup] Created GameSettings asset at {path}.");
                changed = true;
            }

            // Assign to the first ObjectReference property on GameManager that accepts this type
            var so = new SerializedObject(gm);
            so.Update();
            var prop = FindPropertyOfType(so, settingsType);
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = asset;
                so.ApplyModifiedProperties();
                Debug.Log("[DungKeeperSetup] Assigned GameSettings to GameManager.");
                changed = true;
            }
        }

        // ── 3. Unit placeholder prefab ───────────────────────────────────────

        static void EnsureUnitPrefab(Component gm, ref bool changed)
        {
            if (gm == null) return;

            const string dir = "Assets/Prefabs";
            const string path = "Assets/Prefabs/Unit_Placeholder.prefab";

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                temp.name = "Unit_Placeholder";
                prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
                UnityEngine.Object.DestroyImmediate(temp);
                Debug.Log($"[DungKeeperSetup] Created unit placeholder prefab at {path}.");
                changed = true;
            }

            // Try the known field name first, then fall back to any unassigned GameObject field
            var so = new SerializedObject(gm);
            so.Update();

            var prop = so.FindProperty("_unitPrefab") ?? so.FindProperty("unitPrefab");
            if (prop == null)
                prop = FindUnassignedGameObjectProperty(so);

            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
                Debug.Log($"[DungKeeperSetup] Assigned unit prefab to '{prop.name}'.");
                changed = true;
            }
        }

        // ── 4. Room placeholder prefabs ──────────────────────────────────────

        static void EnsureRoomPrefabs(Component gm, ref bool changed)
        {
            if (gm == null) return;

            const string dir = "Assets/Prefabs";
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Build one placeholder prefab per RoomType value we can discover
            var roomType = FindType("DungKeeper.RoomType") ?? FindType("RoomType");

            string[] roomNames = roomType != null
                ? Enum.GetNames(roomType)
                : new[] { "ProductionChamber" }; // fallback to what we know from the console

            var so = new SerializedObject(gm);
            so.Update();

            // Common names for a room prefab array on GameManager
            SerializedProperty roomArray = so.FindProperty("_roomPrefabs")
                ?? so.FindProperty("roomPrefabs")
                ?? so.FindProperty("_roomTypePrefabs")
                ?? so.FindProperty("roomTypePrefabs");

            foreach (var roomName in roomNames)
            {
                string path = $"Assets/Prefabs/Room_{roomName}_Placeholder.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    temp.name = $"Room_{roomName}_Placeholder";
                    // Tint the cube so rooms look different from units at a glance
                    var renderer = temp.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Standard"));
                        renderer.sharedMaterial.color = new Color(0.4f, 0.6f, 1f);
                    }
                    prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
                    UnityEngine.Object.DestroyImmediate(temp);
                    Debug.Log($"[DungKeeperSetup] Created room placeholder prefab at {path}.");
                    changed = true;
                }

                if (roomArray != null)
                {
                    int enumIndex = roomType != null
                        ? (int)Enum.Parse(roomType, roomName)
                        : 0;

                    // Grow array if needed
                    if (roomArray.arraySize <= enumIndex)
                    {
                        roomArray.arraySize = enumIndex + 1;
                        changed = true;
                    }

                    var element = roomArray.GetArrayElementAtIndex(enumIndex);
                    if (element.objectReferenceValue == null)
                    {
                        element.objectReferenceValue = prefab;
                        Debug.Log($"[DungKeeperSetup] Assigned room prefab for {roomName} at index {enumIndex}.");
                        changed = true;
                    }
                }
            }

            if (roomArray != null)
                so.ApplyModifiedProperties();
        }

        // ── 5. NavMesh ───────────────────────────────────────────────────────

        static void EnsureNavMesh(ref bool changed)
        {
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            Debug.Log("[DungKeeperSetup] NavMesh baked.");
            changed = true;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.FullName == fullName || t.Name == fullName);
        }

        static SerializedProperty FindPropertyOfType(SerializedObject so, Type targetType)
        {
            var it = so.GetIterator();
            if (!it.NextVisible(true)) return null;
            do
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                var field = FindField(so.targetObject.GetType(), it.name);
                if (field == null) continue;
                if (targetType.IsAssignableFrom(field.FieldType))
                    return it.Copy();
            } while (it.NextVisible(false));
            return null;
        }

        static SerializedProperty FindUnassignedGameObjectProperty(SerializedObject so)
        {
            var it = so.GetIterator();
            if (!it.NextVisible(true)) return null;
            do
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.objectReferenceValue != null) continue;
                var field = FindField(so.targetObject.GetType(), it.name);
                if (field == null) continue;
                if (field.FieldType == typeof(GameObject))
                    return it.Copy();
            } while (it.NextVisible(false));
            return null;
        }

        static FieldInfo FindField(Type type, string name)
        {
            while (type != null && type != typeof(object))
            {
                var f = type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null) return f;
                type = type.BaseType;
            }
            return null;
        }
    }
}

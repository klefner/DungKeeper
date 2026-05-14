using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;

namespace DungKeeper.Editor
{
    public static class DungKeeperSetup
    {
        [MenuItem("DungKeeper/Setup Scene")]
        public static void SetupScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[DungKeeperSetup] Stop play mode before running scene setup.");
                return;
            }

            EnsureGameManager();
            BakeNavMesh();

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[DungKeeperSetup] Scene setup complete.");
        }

        private static void EnsureGameManager()
        {
            var gmType = System.Type.GetType("DungKeeper.GameManager, Assembly-CSharp");
            if (gmType == null)
            {
                Debug.LogError("[DungKeeperSetup] Could not find GameManager type. Is the script compiled?");
                return;
            }

            if (Object.FindFirstObjectByType(gmType) != null)
                return;

            var go = new GameObject("GameManager");
            go.AddComponent(gmType);
            Undo.RegisterCreatedObjectUndo(go, "Create GameManager");
            Debug.Log("[DungKeeperSetup] GameManager added to scene.");
        }

        private static void BakeNavMesh()
        {
            NavMeshBuilder.BuildNavMesh();
            Debug.Log("[DungKeeperSetup] NavMesh baked.");
        }
    }
}

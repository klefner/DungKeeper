using System.Diagnostics;
using UnityEditor;
using UnityEditor.AI;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace DungKeeper.Editor
{
    public static class DungKeeperSetup
    {
        [MenuItem("DungKeeper/Sync Latest + Setup Scene")]
        public static void SyncAndSetup()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[DungKeeperSetup] Stop play mode first.");
                return;
            }

            if (!PullLatest())
                return;

            AssetDatabase.Refresh();
            SetupScene();
        }

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

        private static bool PullLatest()
        {
            var repoPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, ".."));

            var result = RunGit("pull", repoPath);
            if (result.exitCode == 0)
            {
                Debug.Log($"[DungKeeperSetup] Git pull succeeded:\n{result.output}");
                return true;
            }

            Debug.LogError($"[DungKeeperSetup] Git pull failed:\n{result.error}");
            return false;
        }

        private static (int exitCode, string output, string error) RunGit(string args, string workingDir)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output, error);
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

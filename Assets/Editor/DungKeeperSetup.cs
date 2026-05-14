using System.Diagnostics;
using UnityEditor;
using UnityEditor.AI;
using UnityEditor.Compilation;
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

            // If GameManager isn't compiled yet, wait for the recompile that Refresh triggers.
            if (FindGameManagerType() != null)
            {
                AssetDatabase.Refresh();
                SetupScene();
            }
            else
            {
                Debug.Log("[DungKeeperSetup] New scripts detected — waiting for recompile before setup.");
                CompilationPipeline.compilationFinished += OnCompilationFinished;
                AssetDatabase.Refresh();
            }
        }

        private static void OnCompilationFinished(object _)
        {
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
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

        private static System.Type FindGameManagerType()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                foreach (var t in asm.GetTypes())
                    if (t.Name == "GameManager" && typeof(MonoBehaviour).IsAssignableFrom(t))
                        return t;
            return null;
        }

        private static void EnsureGameManager()
        {
            var gmType = FindGameManagerType();
            if (gmType == null)
            {
                Debug.LogWarning("[DungKeeperSetup] GameManager not compiled yet — run Setup Scene again once Unity finishes compiling.");
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

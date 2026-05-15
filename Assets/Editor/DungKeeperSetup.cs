using System.Diagnostics;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
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
            EnsureRoom();
            EnsureCreature();
            SetupCamera();
            BakeNavMesh();

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[DungKeeperSetup] Scene setup complete.");
        }

        private static bool PullLatest()
        {
            // The git repo lives separately from the Unity project.
            // Try the repo path first; fall back to the Unity project root.
            var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            var repoCandidates = new[]
            {
                System.IO.Path.Combine(userProfile, "Documents", "GitHub", "DungKeeper"),
                System.IO.Path.Combine(userProfile, "GitHub", "DungKeeper"),
                System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..")),
            };

            string repoPath = null;
            foreach (var candidate in repoCandidates)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(candidate, ".git")))
                {
                    repoPath = candidate;
                    break;
                }
            }

            if (repoPath == null)
            {
                Debug.LogError("[DungKeeperSetup] Could not find the DungKeeper git repository.");
                return false;
            }

            var result = RunGit("pull", repoPath);
            if (result.exitCode != 0)
            {
                Debug.LogError($"[DungKeeperSetup] Git pull failed:\n{result.error}");
                return false;
            }

            Debug.Log($"[DungKeeperSetup] Git pull succeeded from {repoPath}");

            // Copy updated Assets into the Unity project if they live in separate folders.
            var unityAssets = Application.dataPath;
            var repoAssets = System.IO.Path.Combine(repoPath, "Assets");
            if (!string.Equals(unityAssets, repoAssets, System.StringComparison.OrdinalIgnoreCase)
                && System.IO.Directory.Exists(repoAssets))
            {
                CopyDirectory(repoAssets, unityAssets);
                Debug.Log("[DungKeeperSetup] Assets synced from git repo to Unity project.");
            }

            return true;
        }

        private static void CopyDirectory(string source, string dest)
        {
            System.IO.Directory.CreateDirectory(dest);
            foreach (var file in System.IO.Directory.GetFiles(source, "*", System.IO.SearchOption.AllDirectories))
            {
                var relative = file.Substring(source.Length).TrimStart('\\', '/');
                var destFile = System.IO.Path.Combine(dest, relative);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destFile));
                System.IO.File.Copy(file, destFile, overwrite: true);
            }
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

        private static void EnsureRoom()
        {
            if (Object.FindFirstObjectByType<RoomGenerator>() != null) return;
            var go = new GameObject("DungeonRoom");
            go.AddComponent<RoomGenerator>();
            Undo.RegisterCreatedObjectUndo(go, "Create DungeonRoom");
            Debug.Log("[DungKeeperSetup] Dungeon room created.");
        }

        private static void EnsureCreature()
        {
            if (Object.FindFirstObjectByType<Creature>() != null) return;

            // Body
            var root = new GameObject("Creature");
            root.transform.position = new Vector3(0, 0.75f, 0);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(0.6f, 0.75f, 0.6f);
            SetMaterialColor(body, new Color(0.18f, 0.72f, 0.22f));

            // Eyes
            AddEye(root.transform, new Vector3( 0.13f, 0.55f, 0.28f));
            AddEye(root.transform, new Vector3(-0.13f, 0.55f, 0.28f));

            root.AddComponent<Creature>();
            root.AddComponent<SlapController>();

            Undo.RegisterCreatedObjectUndo(root, "Create Creature");
            Debug.Log("[DungKeeperSetup] Creature added to scene.");
        }

        private static void AddEye(Transform parent, Vector3 localPos)
        {
            var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = "Eye";
            eye.transform.SetParent(parent);
            eye.transform.localPosition = localPos;
            eye.transform.localScale = Vector3.one * 0.12f;
            SetMaterialColor(eye, Color.white);
            Object.DestroyImmediate(eye.GetComponent<Collider>());

            var pupil = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pupil.name = "Pupil";
            pupil.transform.SetParent(eye.transform);
            pupil.transform.localPosition = new Vector3(0, 0, 0.5f);
            pupil.transform.localScale = Vector3.one * 0.5f;
            SetMaterialColor(pupil, Color.black);
            Object.DestroyImmediate(pupil.GetComponent<Collider>());
        }

        private static void SetMaterialColor(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            r.sharedMaterial = mat;
        }

        private static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(0, 12f, -8f);
            cam.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            cam.backgroundColor = new Color(0.05f, 0.03f, 0.05f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private static void BakeNavMesh()
        {
            // Legacy bake API — suppressed until project adopts NavMeshSurface workflow
#pragma warning disable CS0618
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore CS0618
            Debug.Log("[DungKeeperSetup] NavMesh baked.");
        }
    }
}

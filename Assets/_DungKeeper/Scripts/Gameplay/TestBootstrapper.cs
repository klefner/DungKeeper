using System.Collections;
using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Temporary test-only bootstrapper. Creates a ground plane, one visible unit,
    /// and one visible room so the simulation can be observed in Play mode.
    /// Uses RuntimeInitializeOnLoadMethod so no scene edits are required — remove
    /// this file before shipping.
    ///
    /// Note: No NavMesh is baked here, so the unit will not navigate. The simulation
    /// (morale, anger, fear ticks) still runs. Bake a NavMesh in the Editor for
    /// full movement testing.
    /// </summary>
    public sealed class TestBootstrapper : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateBootstrapper()
        {
            new GameObject("TestBootstrapper").AddComponent<TestBootstrapper>();
        }

        private IEnumerator Start()
        {
            // Wait for all MonoBehaviour Start() calls to complete (including GameManager.Start).
            yield return new WaitForEndOfFrame();

            if (GameManager.Instance == null)
            {
                Debug.LogError("[TestBootstrapper] GameManager not found in scene.");
                yield break;
            }

            // Ground plane — Plane primitive is 10x10 units; scale 4 makes it 40x40.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "TestGround";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            // Create a visible capsule for the first unit GameManager spawned in StartNewGame.
            var allUnits = GameManager.Instance.AllUnits;
            if (allUnits.Count > 0)
            {
                UnitData unitData = allUnits[0];

                GameObject unitGO = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                unitGO.name = $"Visual_{unitData.Name}";
                unitGO.transform.position = new Vector3(2f, 1f, 2f);

                // UnitController carries [RequireComponent(typeof(NavMeshAgent))],
                // so Unity auto-adds the agent before adding the controller.
                UnitController unitCtrl = unitGO.AddComponent<UnitController>();
                unitCtrl.Initialize(unitData);
                GameManager.Instance.RegisterUnit(unitCtrl);
            }

            // Create a visible cube for the first room GameManager built in StartNewGame.
            var allRooms = GameManager.Instance.AllRooms;
            if (allRooms.Count > 0)
            {
                RoomData roomData = allRooms[0];

                GameObject roomGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                roomGO.name = $"Visual_{roomData.Name}";
                roomGO.transform.position = new Vector3(5f, 0.5f, 5f);
                roomGO.transform.localScale = new Vector3(3f, 1f, 3f);

                RoomController roomCtrl = roomGO.AddComponent<RoomController>();
                roomCtrl.Initialize(roomData, null);
                GameManager.Instance.RegisterRoom(roomCtrl);
            }

            // Aim the camera so the unit and room are both visible.
            if (Camera.main != null)
            {
                Camera.main.transform.position = new Vector3(5f, 12f, -5f);
                Camera.main.transform.LookAt(new Vector3(5f, 0f, 5f));
            }

            Debug.Log($"[TestBootstrapper] Ready — {GameManager.Instance.AllUnits.Count} unit(s), " +
                      $"{GameManager.Instance.AllRooms.Count} room(s) in simulation.");
        }
    }
}

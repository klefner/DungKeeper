using UnityEngine;

namespace DungKeeper
{
    public class SlapController : MonoBehaviour
    {
        private Camera cam;

        private void Start() => cam = Camera.main;

        private void Update()
        {
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            bool overCreature = Physics.Raycast(ray, out var hit)
                                && hit.collider.GetComponentInParent<Creature>() != null;

            if (DungeonCursor.Instance != null)
            {
                if (Input.GetMouseButton(0) && overCreature)
                    DungeonCursor.Instance.CurrentState = DungeonCursor.State.Slapping;
                else if (overCreature)
                    DungeonCursor.Instance.CurrentState = DungeonCursor.State.CanSlap;
                else
                    DungeonCursor.Instance.CurrentState = DungeonCursor.State.Point;
            }

            if (Input.GetMouseButtonDown(0) && overCreature)
                hit.collider.GetComponentInParent<Creature>().Slap();
        }
    }
}

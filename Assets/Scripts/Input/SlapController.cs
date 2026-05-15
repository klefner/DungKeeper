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
                    DungeonCursor.Instance.State = DungeonCursor.CursorState.Slapping;
                else if (overCreature)
                    DungeonCursor.Instance.State = DungeonCursor.CursorState.CanSlap;
                else
                    DungeonCursor.Instance.State = DungeonCursor.CursorState.Hover;
            }

            if (Input.GetMouseButtonDown(0) && overCreature)
                hit.collider.GetComponentInParent<Creature>().Slap();
        }
    }
}

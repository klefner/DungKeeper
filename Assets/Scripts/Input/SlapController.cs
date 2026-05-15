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

            // Hover state — only update when not mid-slap so animation isn't interrupted
            if (DungeonCursor.Instance != null && !DungeonCursor.Instance.IsSlapping)
            {
                DungeonCursor.Instance.CurrentState = overCreature
                    ? DungeonCursor.State.CanSlap
                    : DungeonCursor.State.Point;
            }

            // Any left-click triggers the slap animation; creature effect only when hit
            if (Input.GetMouseButtonDown(0))
            {
                DungeonCursor.Instance?.TriggerSlap();
                if (overCreature)
                    hit.collider.GetComponentInParent<Creature>().Slap();
            }
        }
    }
}

using UnityEngine;

namespace DungKeeper
{
    public class SlapController : MonoBehaviour
    {
        private void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            var cam = Camera.main;
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit))
                hit.collider.GetComponentInParent<Creature>()?.Slap();
        }
    }
}

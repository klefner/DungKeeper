using System.Collections;
using UnityEngine;

namespace DungKeeper
{
    public class Creature : MonoBehaviour
    {
        [SerializeField] private float roamSpeed = 2f;
        [SerializeField] private float roamRadius = 5f;

        private Renderer[] renderers;
        private Vector3 target;
        private bool slapped;
        private float currentSpeed;
        private static readonly Color NormalColor  = new Color(0.18f, 0.72f, 0.22f);
        private static readonly Color SlappedColor = new Color(0.95f, 0.15f, 0.10f);

        private void Start()
        {
            renderers = GetComponentsInChildren<Renderer>();
            currentSpeed = roamSpeed;
            SetColor(NormalColor);
            PickTarget();
        }

        private void Update()
        {
            transform.position = Vector3.MoveTowards(transform.position, target, currentSpeed * Time.deltaTime);

            var dir = target - transform.position;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);

            if (Vector3.Distance(transform.position, target) < 0.15f)
                PickTarget();
        }

        private void PickTarget()
        {
            target = new Vector3(
                Random.Range(-roamRadius, roamRadius),
                transform.position.y,
                Random.Range(-roamRadius, roamRadius));
        }

        public void Slap()
        {
            if (!slapped)
                StartCoroutine(SlapRoutine());
        }

        private IEnumerator SlapRoutine()
        {
            slapped = true;
            currentSpeed = roamSpeed * 3.5f;
            SetColor(SlappedColor);

            // bounce
            var origin = transform.position;
            for (float t = 0; t < 1f; t += Time.deltaTime * 10f)
            {
                transform.position = origin + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.6f);
                yield return null;
            }
            transform.position = origin;

            yield return new WaitForSeconds(3f);

            currentSpeed = roamSpeed;
            SetColor(NormalColor);
            slapped = false;
        }

        private void SetColor(Color c)
        {
            foreach (var r in renderers)
                r.material.color = c;
        }
    }
}

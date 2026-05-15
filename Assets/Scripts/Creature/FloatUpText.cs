using System.Collections;
using UnityEngine;

namespace DungKeeper
{
    /// Floating world-space label that rises and fades out.
    public class FloatUpText : MonoBehaviour
    {
        public void Init(string message)
        {
            // Build a canvas in world space
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvas.GetComponent<UnityEngine.RectTransform>();
            rt.sizeDelta = new Vector2(3f, 1f);
            transform.localScale = Vector3.one * 0.015f;

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(transform);
            var txt = textGo.AddComponent<UnityEngine.UI.Text>();
            txt.text = message;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.fontSize = 120;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.yellow;
            var trect = txt.GetComponent<UnityEngine.RectTransform>();
            trect.sizeDelta = new Vector2(200, 70);
            trect.localPosition = Vector3.zero;

            StartCoroutine(FloatAndFade(txt));
        }

        private IEnumerator FloatAndFade(UnityEngine.UI.Text txt)
        {
            var start = transform.position;
            for (float t = 0; t < 1.2f; t += Time.deltaTime)
            {
                transform.position = start + Vector3.up * (t * 2.5f);
                // face camera
                if (Camera.main != null)
                    transform.forward = Camera.main.transform.forward;

                var c = txt.color;
                c.a = 1f - (t / 1.2f);
                txt.color = c;
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}

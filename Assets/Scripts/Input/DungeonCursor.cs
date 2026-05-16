using System.Collections;
using UnityEngine;

namespace DungKeeper
{
    /// 3-D hand cursor built from Unity primitives.
    ///
    /// A dedicated camera (depth 1, clearFlags=Depth) renders only the hand
    /// layer on top of everything else — no special shaders or RenderTexture needed.
    /// Unity's own lighting gives it proper 3-D shading automatically.
    ///
    /// Idle  : each finger wiggles independently at a different phase / speed.
    /// Slap  : hand tilts back, slams DOWN toward the dungeon floor (back-of-hand
    ///         leading), holds at impact, then rises back to idle.
    public class DungeonCursor : MonoBehaviour
    {
        public enum State { Point, CanSlap, Slapping }
        public static DungeonCursor Instance { get; private set; }
        public bool IsSlapping { get; private set; }

        private State _state;
        private Transform _anchor;      // moves with the mouse (world-space)
        private Transform _handGroup;   // child of anchor; animated up/down for slap
        private Transform[] _proximal;  // 5 proximal finger joints (wiggle targets)
        private Camera _handCam;

        private const int HandLayer = 6; // dedicated layer so the hand-camera sees only this

        // ── public API ────────────────────────────────────────────────────

        public State CurrentState
        {
            get => _state;
            set { if (!IsSlapping && _state != value) _state = value; }
        }

        public void TriggerSlap()
        {
            if (!IsSlapping) StartCoroutine(SlapRoutine());
        }

        // ── lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            Cursor.visible = false;

            _anchor    = new GameObject("HandAnchor").transform;
            _handGroup = new GameObject("HandGroup").transform;
            _handGroup.SetParent(_anchor);
            _handGroup.localPosition = Vector3.zero;
            _handGroup.localRotation = Quaternion.identity;

            BuildHand();

            // Karate-chop / backhand orientation: fingers point right (+X world),
            // thumb points up (+Y world), pinky edge faces the floor (-Y world).
            _handGroup.localRotation = Quaternion.Euler(0f, 0f, 90f);

            SetupHandCamera();
            StartCoroutine(IdleWiggle());
        }

        private void Update()         => FollowMouse();
        private void LateUpdate()     => SyncHandCamera();
        private void OnDestroy()      => Cursor.visible = true;

        // ── mouse following ───────────────────────────────────────────────

        private void FollowMouse()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray   = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (plane.Raycast(ray, out float dist))
                // Shift anchor left so fingertips (not palm) sit at cursor world position
                _anchor.position = ray.GetPoint(dist) + Vector3.up * 2.0f + Vector3.left * 0.9f;
        }

        private void SyncHandCamera()
        {
            if (Camera.main == null || _handCam == null) return;
            _handCam.transform.SetPositionAndRotation(
                Camera.main.transform.position,
                Camera.main.transform.rotation);
            _handCam.fieldOfView        = Camera.main.fieldOfView;
            _handCam.orthographic       = Camera.main.orthographic;
            _handCam.orthographicSize   = Camera.main.orthographicSize;
        }

        // ── hand geometry builder ─────────────────────────────────────────

        private void BuildHand()
        {
            var mat = MakeSkinMaterial();

            // ── Palm ──────────────────────────────────────────────────────
            Capsule("Palm", _handGroup, Vector3.zero,
                new Vector3(1.10f, 0.16f, 0.78f), mat);

            // ── Four fingers (pinky → index) ──────────────────────────────
            // x positions, relative finger lengths, names
            float[] fx   = { -0.44f, -0.14f,  0.14f,  0.44f };
            float[] fMul = {  0.83f,  1.00f,  1.10f,  1.00f };
            string[] fn  = { "Pinky", "Ring", "Middle", "Index" };

            _proximal = new Transform[5];

            for (int i = 0; i < 4; i++)
            {
                float ps = 0.230f * fMul[i]; // proximal segment half-height
                float ms = 0.210f * fMul[i]; // middle segment half-height
                float ds = 0.185f * fMul[i]; // distal segment half-height
                float fw = 0.130f;            // finger width (x/z scale)

                // Proximal phalanx — pivot here for idle wiggle
                var prox = new GameObject(fn[i]+"_Prox").transform;
                prox.SetParent(_handGroup);
                prox.localPosition    = new Vector3(fx[i], -0.18f, 0f);
                prox.localRotation    = Quaternion.identity;
                prox.gameObject.layer = HandLayer;
                _proximal[i] = prox;

                Capsule(fn[i]+"P", prox,
                    new Vector3(0, -ps, 0), new Vector3(fw, ps, fw), mat);

                // Middle phalanx
                var mid = new GameObject(fn[i]+"_Mid").transform;
                mid.SetParent(prox);
                mid.localPosition    = new Vector3(0, -ps * 1.95f, 0);
                mid.localRotation    = Quaternion.identity;
                mid.gameObject.layer = HandLayer;

                Capsule(fn[i]+"M", mid,
                    new Vector3(0, -ms * 0.90f, 0),
                    new Vector3(fw * 0.93f, ms * 0.90f, fw * 0.93f), mat);

                // Distal phalanx (fingertip)
                var dist = new GameObject(fn[i]+"_Dist").transform;
                dist.SetParent(mid);
                dist.localPosition    = new Vector3(0, -ms * 1.82f, 0);
                dist.localRotation    = Quaternion.identity;
                dist.gameObject.layer = HandLayer;

                Capsule(fn[i]+"D", dist,
                    new Vector3(0, -ds * 0.80f, 0),
                    new Vector3(fw * 0.84f, ds * 0.80f, fw * 0.84f), mat);
            }

            // ── Thumb ─────────────────────────────────────────────────────
            var thumb = new GameObject("Thumb_Prox").transform;
            thumb.SetParent(_handGroup);
            thumb.localPosition    = new Vector3(0.60f, -0.08f, 0f);
            thumb.localEulerAngles = new Vector3(0f, 0f, -38f);
            thumb.gameObject.layer = HandLayer;
            _proximal[4] = thumb;

            Capsule("ThumbP", thumb,
                new Vector3(0, -0.18f, 0), new Vector3(0.14f, 0.18f, 0.14f), mat);

            var thumbDist = new GameObject("Thumb_Dist").transform;
            thumbDist.SetParent(thumb);
            thumbDist.localPosition    = new Vector3(0, -0.36f, 0);
            thumbDist.localRotation    = Quaternion.identity;
            thumbDist.gameObject.layer = HandLayer;

            Capsule("ThumbD", thumbDist,
                new Vector3(0, -0.15f, 0), new Vector3(0.12f, 0.15f, 0.12f), mat);

            // Apply global hand size and set all layers
            _handGroup.localScale = Vector3.one * 1.4f;
            SetLayer(_anchor.gameObject);
            SetLayer(_handGroup.gameObject);
        }

        private static void Capsule(string name, Transform parent,
                                     Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name  = name;
            go.layer = HandLayer;
            Object.Destroy(go.GetComponent<Collider>());
            var t = go.transform;
            t.SetParent(parent);
            t.localPosition = localPos;
            t.localScale    = localScale;
            t.localRotation = Quaternion.identity;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static Material MakeSkinMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader);
            // Warm human skin tone
            mat.color = new Color(0.90f, 0.70f, 0.50f);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.22f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.22f);
            return mat;
        }

        private static void SetLayer(GameObject go)
        {
            go.layer = HandLayer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject);
        }

        // ── camera ────────────────────────────────────────────────────────

        private void SetupHandCamera()
        {
            var go = new GameObject("HandCamera");
            go.transform.SetParent(transform);

            _handCam = go.AddComponent<Camera>();
            _handCam.depth       = 1;                        // renders after main camera
            _handCam.clearFlags  = CameraClearFlags.Depth;   // keeps scene color, only clears depth
            _handCam.cullingMask = 1 << HandLayer;           // sees only the hand

            // Dedicated point light that only illuminates the hand
            var lightGo = new GameObject("HandLight");
            lightGo.transform.SetParent(_anchor);
            lightGo.transform.localPosition = new Vector3(0.3f, 1.5f, -0.8f);
            var light = lightGo.AddComponent<Light>();
            light.type         = LightType.Point;
            light.color        = new Color(1.0f, 0.95f, 0.85f);
            light.intensity    = 3.0f;
            light.range        = 5.0f;
            light.cullingMask  = 1 << HandLayer; // only lights the hand, not the dungeon
        }

        // ── animations ────────────────────────────────────────────────────

        private IEnumerator IdleWiggle()
        {
            // Unsynchronised, organic-feeling finger movement
            float[] phase = { 0.00f, 1.20f, 2.40f, 3.60f, 0.65f };
            float[] speed = { 0.85f, 1.10f, 0.78f, 1.00f, 0.72f };
            float[] amp   = { 7.0f,  8.0f,  6.0f,  7.0f,  4.5f };

            while (true)
            {
                if (!IsSlapping)
                {
                    float now = Time.time;
                    for (int i = 0; i < _proximal.Length; i++)
                    {
                        if (_proximal[i] == null) continue;
                        float angle = Mathf.Sin(now * speed[i] + phase[i]) * amp[i];
                        var e = _proximal[i].localEulerAngles;
                        // Rotate around local Z — correct curl axis now fingers point right
                        _proximal[i].localEulerAngles = new Vector3(e.x, e.y, angle);
                    }
                }
                yield return null;
            }
        }

        private IEnumerator SlapRoutine()
        {
            IsSlapping = true;
            var restPos = _handGroup.localPosition;
            // Base orientation: fingers right, thumb up, pinky toward floor.

            // ── 1. Wind-up: pull left, wrist tilts back (Y+) ──────────────
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.14f)
            {
                float e = Mathf.SmoothStep(0, 1, t);
                _handGroup.localPosition    = restPos + Vector3.left * Mathf.Lerp(0, 0.32f, e);
                // Rotate around Y so wrist leads left, fingertips angle back-right
                _handGroup.localEulerAngles = new Vector3(0, Mathf.Lerp(0, 22f, e), 90);
                yield return null;
            }

            // ── 2. Sweep right: fingertips arc farther than wrist ─────────
            var windupPos = _handGroup.localPosition;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.08f)
            {
                float e = Mathf.Pow(t, 0.35f); // fast initial burst
                _handGroup.localPosition    = windupPos + Vector3.right * Mathf.Lerp(0, 0.80f, e);
                // Y angle flips: wrist trails then follows through past centre
                float yAngle = Mathf.Lerp(22f, -18f, t);
                _handGroup.localEulerAngles = new Vector3(0, yAngle, 90);
                yield return null;
            }

            // ── 3. Hold at impact ─────────────────────────────────────────
            yield return new WaitForSeconds(0.08f);

            // ── 4. Drift back to idle ─────────────────────────────────────
            var impactPos = _handGroup.localPosition;
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.24f)
            {
                float e = Mathf.SmoothStep(0, 1, t);
                _handGroup.localPosition    = Vector3.Lerp(impactPos, restPos, e);
                _handGroup.localEulerAngles = new Vector3(0, Mathf.Lerp(-18f, 0f, e), 90);
                yield return null;
            }

            _handGroup.localPosition    = restPos;
            _handGroup.localEulerAngles = new Vector3(0, 0, 90);

            IsSlapping = false;
            _state = State.Point;
        }
    }
}

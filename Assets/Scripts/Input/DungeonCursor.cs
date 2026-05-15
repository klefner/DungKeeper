using UnityEngine;
using UnityEngine.UI;

namespace DungKeeper
{
    public class DungeonCursor : MonoBehaviour
    {
        public enum State { Point, CanSlap, Slapping }

        public static DungeonCursor Instance { get; private set; }

        private State _state;
        private Texture2D _pointTex;
        private Texture2D _canSlapTex;
        private Texture2D[] _slapFrames;
        private int _slapFrame;
        private float _frameTimer;
        private RawImage _cursorImg;
        private RectTransform _cursorRect;

        private const int S = 96;           // texture resolution
        private const int Display = 96;     // screen-pixel size of the cursor

        // Per-frame durations: backswing → mid → IMPACT (lingers) → follow-thru → mid-return → backswing
        private static readonly float[] FrameTimes = { 0.07f, 0.05f, 0.10f, 0.06f, 0.05f, 0.07f };

        public State CurrentState
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                _slapFrame = 0;
                _frameTimer = 0f;
                Apply();
            }
        }

        private void Awake()
        {
            Instance = this;
            Cursor.visible = false; // hide the OS cursor entirely

            _pointTex   = BuildPoint();
            _canSlapTex = BuildCanSlap();

            // Six frames: hand sweeps RIGHT → LEFT (dark back-of-hand → light palm),
            // then LEFT → RIGHT (light palm → dark back-of-hand) to complete the slap cycle
            var farRight   = BuildSlapFrame(xOff: 32, Col.Back,    knuckles: true,  flash: false);
            var midRight   = BuildSlapFrame(xOff: 20, Col.BackMid, knuckles: true,  flash: false);
            var impact     = BuildSlapFrame(xOff:  8, Col.Palm,    knuckles: false, flash: true);
            var followThru = BuildSlapFrame(xOff:  0, Col.Palm,    knuckles: false, flash: false);

            _slapFrames = new[] { farRight, midRight, impact, followThru, midRight, farRight };

            CreateUICursor();
            Apply();
        }

        private void CreateUICursor()
        {
            var canvasGo = new GameObject("CursorCanvas");
            canvasGo.transform.SetParent(transform);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767; // on top of everything

            var imgGo = new GameObject("CursorImg");
            imgGo.transform.SetParent(canvasGo.transform, false);
            _cursorImg = imgGo.AddComponent<RawImage>();
            _cursorImg.raycastTarget = false; // don't block game clicks
            _cursorImg.color = Color.white;

            _cursorRect = _cursorImg.rectTransform;
            _cursorRect.anchorMin = _cursorRect.anchorMax = Vector2.zero;
            _cursorRect.pivot = new Vector2(0f, 1f); // top-left corner tracks mouse
            _cursorRect.sizeDelta = new Vector2(Display, Display);
        }

        private void Update()
        {
            // Cursor follows mouse every frame
            _cursorRect.position = Input.mousePosition;

            if (_state != State.Slapping) return;
            _frameTimer += Time.deltaTime;
            float threshold = FrameTimes[_slapFrame % FrameTimes.Length];
            if (_frameTimer < threshold) return;
            _frameTimer -= threshold;
            _slapFrame = (_slapFrame + 1) % _slapFrames.Length;
            _cursorImg.texture = _slapFrames[_slapFrame];
        }

        private void OnDestroy()
        {
            Cursor.visible = true;
        }

        private void Apply()
        {
            switch (_state)
            {
                case State.Slapping:
                    _cursorImg.texture = _slapFrames[0];
                    break;
                case State.CanSlap:
                    _cursorImg.texture = _canSlapTex;
                    break;
                default:
                    _cursorImg.texture = _pointTex;
                    break;
            }
        }

        // ── colors ────────────────────────────────────────────────────────

        private static class Col
        {
            public static readonly Color32 Back    = new Color32(185, 135, 80,  255); // dark back-of-hand
            public static readonly Color32 BackMid = new Color32(212, 165, 108, 255); // mid-rotation
            public static readonly Color32 Palm    = new Color32(240, 195, 145, 255); // light palm
            public static readonly Color32 Knuckle = new Color32(150, 105, 52,  255);
            public static readonly Color32 Flash   = new Color32(255, 238, 50,  255);
            public static readonly Color32 Nail    = new Color32(255, 228, 205, 255);
        }

        // ── texture builders ──────────────────────────────────────────────

        private static Texture2D BuildPoint()
        {
            var px = Blank(S);
            // Index finger extended upward
            Rect(px, S, 27, 0,  14, 54, Col.Palm);
            Rect(px, S, 29, 0,  10,  8, Col.Nail); // nail
            // Curled fingers (middle, ring, pinky)
            Rect(px, S, 41, 21, 14, 39, Col.Palm);
            Rect(px, S, 55, 27, 14, 33, Col.Palm);
            Rect(px, S, 69, 33, 12, 27, Col.Palm);
            // Palm
            Rect(px, S, 21, 54, 63, 30, Col.Palm);
            // Thumb
            Rect(px, S,  9, 63, 24, 21, Col.Palm);
            return Bake(px, S);
        }

        private static Texture2D BuildCanSlap()
        {
            var px = Blank(S);
            DrawOpenHand(px, S, xOff: 8, skin: Col.Palm, knuckles: false, flash: false);
            return Bake(px, S);
        }

        private static Texture2D BuildSlapFrame(int xOff, Color32 skin, bool knuckles, bool flash)
        {
            var px = Blank(S);
            DrawOpenHand(px, S, xOff, skin, knuckles, flash);
            return Bake(px, S);
        }

        // Open hand with fingers pointing down. xOff shifts the whole hand horizontally
        // to simulate the lateral sweep of the slap.
        private static void DrawOpenHand(Color32[] px, int S, int xOff, Color32 skin,
                                         bool knuckles, bool flash)
        {
            const int yOff = 6;

            // Fingers (varying heights; middle is tallest)
            Rect(px, S, xOff +  0, yOff,      9, 33, skin); // pinky
            Rect(px, S, xOff + 12, yOff,      9, 39, skin); // ring
            Rect(px, S, xOff + 24, yOff,      9, 45, skin); // middle
            Rect(px, S, xOff + 36, yOff,      9, 39, skin); // index
            Rect(px, S, xOff + 48, yOff +  9, 9, 27, skin); // thumb
            // Palm
            Rect(px, S, xOff,      yOff + 33, 60, 24, skin);

            if (knuckles)
            {
                // Knuckle ridge at finger-palm junction
                Rect(px, S, xOff +  0, yOff + 30, 8, 3, Col.Knuckle);
                Rect(px, S, xOff + 12, yOff + 30, 8, 3, Col.Knuckle);
                Rect(px, S, xOff + 24, yOff + 30, 8, 3, Col.Knuckle);
                Rect(px, S, xOff + 36, yOff + 30, 8, 3, Col.Knuckle);
                // Mid-finger knuckle joints
                Rect(px, S, xOff +  0, yOff + 16, 7, 2, Col.Knuckle);
                Rect(px, S, xOff + 12, yOff + 19, 7, 2, Col.Knuckle);
                Rect(px, S, xOff + 24, yOff + 22, 7, 2, Col.Knuckle);
                Rect(px, S, xOff + 36, yOff + 19, 7, 2, Col.Knuckle);
            }

            if (flash)
            {
                // Yellow impact burst at fingertips
                Rect(px, S, xOff +  0, yOff, 9, 9, Col.Flash);
                Rect(px, S, xOff + 12, yOff, 9, 9, Col.Flash);
                Rect(px, S, xOff + 24, yOff, 9, 9, Col.Flash);
                Rect(px, S, xOff + 36, yOff, 9, 9, Col.Flash);
            }
        }

        // ── helpers ───────────────────────────────────────────────────────

        private static Color32[] Blank(int size)
        {
            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            return px;
        }

        // y=0 = top of cursor visually; Unity textures are bottom-left origin, so flip y.
        // cols outside [0, S) are silently clipped so off-edge hands look natural.
        private static void Rect(Color32[] px, int size, int x, int y, int w, int h, Color32 c)
        {
            for (int row = y; row < y + h && row < size; row++)
                for (int col = x; col < x + w; col++)
                {
                    if (col < 0 || col >= size) continue;
                    px[(size - 1 - row) * size + col] = c;
                }
        }

        private static Texture2D Bake(Color32[] px, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}

using UnityEngine;

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

        // Timings: backswing-start, backswing-mid, IMPACT (lingers), follow-thru, return-mid, return-end
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
            _pointTex   = BuildPoint();
            _canSlapTex = BuildCanSlap();

            // Six frames: hand sweeps right→left (back-of-hand→palm) then left→right (palm→back)
            // xOff is the left edge of the hand within the 64×64 frame
            var farRight  = BuildSlapFrame(xOff: 22, C.Back,    knuckles: true,  flash: false);
            var midRight  = BuildSlapFrame(xOff: 14, C.BackMid, knuckles: true,  flash: false);
            var impact    = BuildSlapFrame(xOff:  6, C.Palm,    knuckles: false, flash: true);
            var followThru= BuildSlapFrame(xOff:  0, C.Palm,    knuckles: false, flash: false);

            // Symmetric: right → right-mid → IMPACT → follow-through → right-mid → right
            _slapFrames = new[] { farRight, midRight, impact, followThru, midRight, farRight };
            Apply();
        }

        private void Update()
        {
            if (_state != State.Slapping) return;
            _frameTimer += Time.deltaTime;
            float threshold = FrameTimes[_slapFrame % FrameTimes.Length];
            if (_frameTimer < threshold) return;
            _frameTimer -= threshold;
            _slapFrame = (_slapFrame + 1) % _slapFrames.Length;
            Cursor.SetCursor(_slapFrames[_slapFrame], SlapHotspot, CursorMode.ForceSoftware);
        }

        private void OnDestroy() =>
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        // Hotspot sits at the fingertips of the impact frame (xOff=6, yOff=4 → tip at ~x9, y4)
        private static readonly Vector2 SlapHotspot = new Vector2(9, 4);

        private void Apply()
        {
            switch (_state)
            {
                case State.Slapping:
                    Cursor.SetCursor(_slapFrames[0], SlapHotspot, CursorMode.ForceSoftware);
                    break;
                case State.CanSlap:
                    Cursor.SetCursor(_canSlapTex, new Vector2(20, 4), CursorMode.ForceSoftware);
                    break;
                default:
                    Cursor.SetCursor(_pointTex, new Vector2(10, 2), CursorMode.ForceSoftware);
                    break;
            }
        }

        // ── colors ────────────────────────────────────────────────────────

        private static class C
        {
            public static readonly Color32 Back    = new Color32(188, 138, 82, 255);  // dark back-of-hand
            public static readonly Color32 BackMid = new Color32(214, 168, 112, 255); // mid-rotation
            public static readonly Color32 Palm    = new Color32(240, 195, 145, 255); // light palm
            public static readonly Color32 Knuckle = new Color32(152, 108, 56, 255);  // knuckle bumps
            public static readonly Color32 Flash   = new Color32(255, 238, 55, 255);  // impact flash
            public static readonly Color32 Nail    = new Color32(255, 230, 210, 255);
        }

        // ── texture builders ──────────────────────────────────────────────

        private static Texture2D BuildPoint()
        {
            // 64×64 pointing finger
            const int S = 64;
            var px = Blank(S);
            // Index finger extended
            Rect(px, S, 18, 0, 10, 36, C.Palm);
            Rect(px, S, 20, 0, 6,  6,  C.Nail);
            // Curled fingers
            Rect(px, S, 28, 14, 10, 26, C.Palm);
            Rect(px, S, 38, 18, 10, 22, C.Palm);
            Rect(px, S, 48, 22, 8,  18, C.Palm);
            // Palm
            Rect(px, S, 14, 36, 42, 20, C.Palm);
            // Thumb
            Rect(px, S, 6,  42, 16, 14, C.Palm);
            return Bake(px, S);
        }

        private static Texture2D BuildCanSlap()
        {
            // 64×64 open palm (hover — no flash)
            const int S = 64;
            var px = Blank(S);
            DrawOpenHand(px, S, xOff: 6, yOff: 4, skin: C.Palm, knuckles: false, flash: false);
            return Bake(px, S);
        }

        // All six slap animation frames share the same hand shape; only xOff and colors differ
        private static Texture2D BuildSlapFrame(int xOff, Color32 skin, bool knuckles, bool flash)
        {
            const int S = 64;
            var px = Blank(S);
            DrawOpenHand(px, S, xOff, yOff: 4, skin, knuckles, flash);
            return Bake(px, S);
        }

        // Draw an open hand (fingers pointing down) at the given offset within the texture
        private static void DrawOpenHand(Color32[] px, int S, int xOff, int yOff,
                                         Color32 skin, bool knuckles, bool flash)
        {
            // Five fingers (lengths vary: pinky shortest, middle tallest)
            Rect(px, S, xOff +  0, yOff,      6, 22, skin); // pinky
            Rect(px, S, xOff +  8, yOff,      6, 26, skin); // ring
            Rect(px, S, xOff + 16, yOff,      6, 30, skin); // middle
            Rect(px, S, xOff + 24, yOff,      6, 26, skin); // index
            Rect(px, S, xOff + 32, yOff +  6, 6, 18, skin); // thumb
            // Palm
            Rect(px, S, xOff,      yOff + 22, 40, 16, skin);

            if (knuckles)
            {
                // Knuckle ridge where fingers join palm
                Rect(px, S, xOff +  0, yOff + 20, 5, 2, C.Knuckle);
                Rect(px, S, xOff +  8, yOff + 20, 5, 2, C.Knuckle);
                Rect(px, S, xOff + 16, yOff + 20, 5, 2, C.Knuckle);
                Rect(px, S, xOff + 24, yOff + 20, 5, 2, C.Knuckle);
                // Mid-finger knuckle joints
                Rect(px, S, xOff +  0, yOff + 11, 4, 1, C.Knuckle);
                Rect(px, S, xOff +  8, yOff + 13, 4, 1, C.Knuckle);
                Rect(px, S, xOff + 16, yOff + 15, 4, 1, C.Knuckle);
                Rect(px, S, xOff + 24, yOff + 13, 4, 1, C.Knuckle);
            }

            if (flash)
            {
                // Yellow impact flash on fingertips
                Rect(px, S, xOff +  0, yOff, 6, 6, C.Flash);
                Rect(px, S, xOff +  8, yOff, 6, 6, C.Flash);
                Rect(px, S, xOff + 16, yOff, 6, 6, C.Flash);
                Rect(px, S, xOff + 24, yOff, 6, 6, C.Flash);
            }
        }

        // ── helpers ───────────────────────────────────────────────────────

        private static Color32[] Blank(int S)
        {
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
            return px;
        }

        // y=0 = top of cursor visually; texture origin is bottom-left, so flip y
        private static void Rect(Color32[] px, int S, int x, int y, int w, int h, Color32 c)
        {
            for (int row = y; row < y + h && row < S; row++)
                for (int col = x; col < x + w && col < S; col++)
                {
                    if (col < 0 || col >= S) continue; // allow partial off-edge hands
                    px[(S - 1 - row) * S + col] = c;
                }
        }

        private static Texture2D Bake(Color32[] px, int S)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Point };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}

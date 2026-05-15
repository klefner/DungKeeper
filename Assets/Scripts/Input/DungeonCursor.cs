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

        // back → rotate → IMPACT → rotate → back (one slap cycle)
        private static readonly float[] FrameTimes = { 0.07f, 0.05f, 0.10f, 0.05f, 0.08f };

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

            var back    = BuildBack();       // dark back-of-hand, knuckles visible
            var rotate  = BuildRotating();   // mid-tone, fingers spreading, fading knuckles
            var impact  = BuildImpact();     // light palm + yellow flash

            // symmetric: back → rotate → IMPACT → rotate → back
            _slapFrames = new[] { back, rotate, impact, rotate, back };
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
            Cursor.SetCursor(_slapFrames[_slapFrame], new Vector2(12, 2), CursorMode.Auto);
        }

        private void OnDestroy() =>
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        private void Apply()
        {
            switch (_state)
            {
                case State.Slapping:
                    Cursor.SetCursor(_slapFrames[0], new Vector2(12, 2), CursorMode.Auto);
                    break;
                case State.CanSlap:
                    Cursor.SetCursor(_canSlapTex, new Vector2(12, 2), CursorMode.Auto);
                    break;
                default:
                    Cursor.SetCursor(_pointTex, new Vector2(10, 2), CursorMode.Auto);
                    break;
            }
        }

        // ── colors ────────────────────────────────────────────────────────

        private static readonly Color32 PalmColor    = new Color32(240, 195, 145, 255);
        private static readonly Color32 BackColor    = new Color32(195, 148, 92, 255);
        private static readonly Color32 RotateColor  = new Color32(218, 172, 118, 255);
        private static readonly Color32 KnuckleColor = new Color32(160, 115, 62, 255);
        private static readonly Color32 FlashColor   = new Color32(255, 235, 55, 255);
        private static readonly Color32 NailColor    = new Color32(255, 230, 210, 255);

        // ── frame builders ────────────────────────────────────────────────

        // Pointing finger (normal state)
        private static Texture2D BuildPoint()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 9, 0, 5, 18, PalmColor);
            Rect(px, S, 10, 0, 3, 3, NailColor);
            Rect(px, S, 14, 7, 5, 13, PalmColor);
            Rect(px, S, 19, 9, 5, 11, PalmColor);
            Rect(px, S, 24, 11, 4, 9, PalmColor);
            Rect(px, S, 7, 18, 21, 10, PalmColor);
            Rect(px, S, 3, 21, 8, 7, PalmColor);
            return Bake(px, S);
        }

        // Open palm hover (CanSlap — same shape as impact but no flash)
        private static Texture2D BuildCanSlap()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 1,  0, 4, 18, PalmColor); // pinky
            Rect(px, S, 6,  0, 4, 20, PalmColor); // ring
            Rect(px, S, 11, 0, 4, 22, PalmColor); // middle
            Rect(px, S, 16, 0, 4, 20, PalmColor); // index
            Rect(px, S, 21, 4, 4, 14, PalmColor); // thumb
            Rect(px, S, 1, 17, 24, 12, PalmColor); // palm
            return Bake(px, S);
        }

        // Frame 0 & 4 — back of hand raised: dark skin, knuckle bumps, fingers close together
        private static Texture2D BuildBack()
        {
            const int S = 32;
            var px = Blank(S);

            // Fingers slightly narrower and closer than palm view (back-of-hand look)
            Rect(px, S, 3,  0, 3, 18, BackColor); // pinky
            Rect(px, S, 7,  0, 3, 20, BackColor); // ring
            Rect(px, S, 11, 0, 3, 22, BackColor); // middle
            Rect(px, S, 15, 0, 3, 20, BackColor); // index
            Rect(px, S, 19, 4, 3, 14, BackColor); // thumb (mirrored side vs palm)
            Rect(px, S, 3, 17, 20, 12, BackColor); // palm

            // Knuckle bumps where fingers meet palm
            Rect(px, S, 4,  15, 2, 2, KnuckleColor);
            Rect(px, S, 8,  15, 2, 2, KnuckleColor);
            Rect(px, S, 12, 15, 2, 2, KnuckleColor);
            Rect(px, S, 16, 15, 2, 2, KnuckleColor);

            // Knuckle joints mid-finger
            Rect(px, S, 4,  9,  2, 1, KnuckleColor);
            Rect(px, S, 8,  10, 2, 1, KnuckleColor);
            Rect(px, S, 12, 11, 2, 1, KnuckleColor);
            Rect(px, S, 16, 10, 2, 1, KnuckleColor);

            return Bake(px, S);
        }

        // Frames 1 & 3 — hand mid-rotation: fingers spreading, knuckles fading, mid-tone
        private static Texture2D BuildRotating()
        {
            const int S = 32;
            var px = Blank(S);

            // Fingers spreading to palm width, tone between back and palm
            Rect(px, S, 2,  0, 3, 18, RotateColor); // pinky (spreading left)
            Rect(px, S, 6,  0, 4, 20, RotateColor); // ring
            Rect(px, S, 11, 0, 4, 22, RotateColor); // middle
            Rect(px, S, 16, 0, 4, 20, RotateColor); // index
            Rect(px, S, 21, 4, 4, 14, RotateColor); // thumb (spreading right)
            Rect(px, S, 2, 17, 23, 12, RotateColor); // palm

            // Faint knuckle hints (half-visible as hand rotates through)
            Rect(px, S, 3,  15, 2, 1, KnuckleColor);
            Rect(px, S, 7,  15, 2, 1, KnuckleColor);
            Rect(px, S, 12, 15, 2, 1, KnuckleColor);
            Rect(px, S, 17, 15, 2, 1, KnuckleColor);

            return Bake(px, S);
        }

        // Frame 2 — IMPACT: full light palm, fingers fully spread, yellow fingertip flash
        private static Texture2D BuildImpact()
        {
            const int S = 32;
            var px = Blank(S);

            Rect(px, S, 1,  0, 4, 18, PalmColor); // pinky
            Rect(px, S, 6,  0, 4, 20, PalmColor); // ring
            Rect(px, S, 11, 0, 4, 22, PalmColor); // middle
            Rect(px, S, 16, 0, 4, 20, PalmColor); // index
            Rect(px, S, 21, 4, 4, 14, PalmColor); // thumb
            Rect(px, S, 1, 17, 24, 12, PalmColor); // palm

            // Yellow flash on fingertips
            Rect(px, S, 1,  0, 4, 4, FlashColor);
            Rect(px, S, 6,  0, 4, 4, FlashColor);
            Rect(px, S, 11, 0, 4, 4, FlashColor);
            Rect(px, S, 16, 0, 4, 4, FlashColor);

            return Bake(px, S);
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
                    px[(S - 1 - row) * S + col] = c;
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

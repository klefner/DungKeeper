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

        // Each frame has its own duration so the swing feels weighted
        private static readonly float[] FrameTimes = { 0.06f, 0.06f, 0.05f, 0.09f, 0.06f, 0.08f };

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
            _slapFrames = new[]
            {
                BuildBack_Small(),    // 0 – hand small & raised, back visible
                BuildBack_Medium(),   // 1 – swinging in, back visible, larger
                BuildBack_Large(),    // 2 – just before flip, full back of hand
                BuildPalm_Impact(),   // 3 – IMPACT: full palm + yellow flash
                BuildPalm_FollowThru(), // 4 – past impact, palm still visible, lower
                BuildPalm_Recoil(),   // 5 – small, retreating, returning to start
            };
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
                    Cursor.SetCursor(_canSlapTex, new Vector2(16, 16), CursorMode.Auto);
                    break;
                default:
                    Cursor.SetCursor(_pointTex, new Vector2(10, 2), CursorMode.Auto);
                    break;
            }
        }

        // ── static colors ─────────────────────────────────────────────────

        private static readonly Color32 Back    = new Color32(195, 145, 90, 255);  // darker back-of-hand
        private static readonly Color32 Knuckle = new Color32(165, 115, 65, 255);  // knuckle bumps
        private static readonly Color32 Palm    = new Color32(240, 195, 145, 255); // lighter palm
        private static readonly Color32 Flash   = new Color32(255, 235, 60, 255);  // impact flash

        // ── texture builders ──────────────────────────────────────────────

        private static Texture2D BuildPoint()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 9, 0, 5, 18, Palm);
            Rect(px, S, 10, 0, 3, 3, new Color32(255, 230, 210, 255)); // nail
            Rect(px, S, 14, 7, 5, 13, Palm);
            Rect(px, S, 19, 9, 5, 11, Palm);
            Rect(px, S, 24, 11, 4, 9, Palm);
            Rect(px, S, 7, 18, 21, 10, Palm);
            Rect(px, S, 3, 21, 8, 7, Palm);
            return Bake(px, S);
        }

        private static Texture2D BuildCanSlap()
        {
            // Open palm hover — same as impact but no flash
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 1,  0, 4, 18, Palm);
            Rect(px, S, 6,  0, 4, 20, Palm);
            Rect(px, S, 11, 0, 4, 22, Palm);
            Rect(px, S, 16, 0, 4, 20, Palm);
            Rect(px, S, 21, 4, 4, 14, Palm);
            Rect(px, S, 1, 17, 24, 12, Palm);
            return Bake(px, S);
        }

        // Frame 0 – tiny back-of-hand (raised, far away)
        private static Texture2D BuildBack_Small()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 11, 0, 2, 8, Back);  // middle
            Rect(px, S, 9,  1, 2, 7, Back);  // ring
            Rect(px, S, 13, 1, 2, 7, Back);  // index
            Rect(px, S, 7,  2, 2, 5, Back);  // pinky
            Rect(px, S, 15, 3, 2, 4, Back);  // thumb
            Rect(px, S, 7,  6, 10, 1, Knuckle); // knuckle ridge
            Rect(px, S, 7,  7, 10, 4, Back);  // palm (compact)
            return Bake(px, S);
        }

        // Frame 1 – medium back-of-hand (swinging in)
        private static Texture2D BuildBack_Medium()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 10, 0, 3, 12, Back); // middle
            Rect(px, S, 7,  1, 3, 11, Back); // ring
            Rect(px, S, 13, 1, 3, 11, Back); // index
            Rect(px, S, 4,  3, 3,  9, Back); // pinky
            Rect(px, S, 16, 4, 3,  8, Back); // thumb
            Rect(px, S, 4, 10, 16,  1, Knuckle); // knuckle ridge
            Rect(px, S, 4, 11, 16,  6, Back); // palm
            return Bake(px, S);
        }

        // Frame 2 – full back-of-hand (just before flip, large)
        private static Texture2D BuildBack_Large()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 9,  0, 4, 16, Back); // middle
            Rect(px, S, 5,  1, 4, 15, Back); // ring
            Rect(px, S, 13, 1, 4, 15, Back); // index
            Rect(px, S, 1,  3, 4, 13, Back); // pinky
            Rect(px, S, 17, 4, 4, 12, Back); // thumb
            Rect(px, S, 1, 14, 21,  1, Knuckle); // knuckle ridge
            Rect(px, S, 1, 15, 21,  8, Back); // palm
            // knuckle dots on each finger
            Rect(px, S, 10, 12, 2, 2, Knuckle);
            Rect(px, S, 6,  12, 2, 2, Knuckle);
            Rect(px, S, 14, 12, 2, 2, Knuckle);
            Rect(px, S, 2,  11, 2, 2, Knuckle);
            return Bake(px, S);
        }

        // Frame 3 – IMPACT: full palm + yellow fingertip flash
        private static Texture2D BuildPalm_Impact()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 1,  0, 4, 18, Palm);
            Rect(px, S, 6,  0, 4, 20, Palm);
            Rect(px, S, 11, 0, 4, 22, Palm);
            Rect(px, S, 16, 0, 4, 20, Palm);
            Rect(px, S, 21, 4, 4, 14, Palm);
            Rect(px, S, 1, 17, 24, 12, Palm);
            // flash on fingertips
            Rect(px, S, 1,  0, 4, 4, Flash);
            Rect(px, S, 6,  0, 4, 4, Flash);
            Rect(px, S, 11, 0, 4, 4, Flash);
            Rect(px, S, 16, 0, 4, 4, Flash);
            return Bake(px, S);
        }

        // Frame 4 – follow-through: palm still large but shifted down (past impact)
        private static Texture2D BuildPalm_FollowThru()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 2,  5, 4, 16, Palm); // pinky
            Rect(px, S, 7,  4, 4, 18, Palm); // ring
            Rect(px, S, 12, 3, 4, 19, Palm); // middle
            Rect(px, S, 17, 4, 4, 18, Palm); // index
            Rect(px, S, 22, 7, 4, 13, Palm); // thumb
            Rect(px, S, 2, 20, 23, 10, Palm); // palm
            return Bake(px, S);
        }

        // Frame 5 – recoil: small palm retreating back up
        private static Texture2D BuildPalm_Recoil()
        {
            const int S = 32;
            var px = Blank(S);
            Rect(px, S, 8,  9, 3, 9, Palm);  // ring
            Rect(px, S, 12, 8, 3, 11, Palm); // middle
            Rect(px, S, 16, 9, 3, 9, Palm);  // index
            Rect(px, S, 5,  10, 3, 7, Palm); // pinky
            Rect(px, S, 19, 11, 3, 6, Palm); // thumb
            Rect(px, S, 5,  17, 18, 5, Palm); // palm (small)
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

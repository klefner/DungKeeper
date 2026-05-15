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
        private const float FrameInterval = 0.07f; // ~14 fps

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
            _canSlapTex = BuildSlap();
            _slapFrames = new[]
            {
                BuildSlapRaised(),  // 0 – backswing: hand compact and high
                BuildSlapMid(),     // 1 – mid-swing
                BuildSlapImpact(),  // 2 – impact + yellow flash
                BuildSlapMid(),     // 3 – recoil (reuse mid frame)
            };
            Apply();
        }

        private void Update()
        {
            if (_state != State.Slapping) return;
            _frameTimer += Time.deltaTime;
            if (_frameTimer < FrameInterval) return;
            _frameTimer -= FrameInterval;
            _slapFrame = (_slapFrame + 1) % _slapFrames.Length;
            Cursor.SetCursor(_slapFrames[_slapFrame], SlapHotspot, CursorMode.Auto);
        }

        private void OnDestroy() =>
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        private static readonly Vector2 SlapHotspot = new Vector2(12, 2);

        private void Apply()
        {
            switch (_state)
            {
                case State.Slapping:
                    Cursor.SetCursor(_slapFrames[0], SlapHotspot, CursorMode.Auto);
                    break;
                case State.CanSlap:
                    Cursor.SetCursor(_canSlapTex, new Vector2(16, 16), CursorMode.Auto);
                    break;
                default:
                    Cursor.SetCursor(_pointTex, new Vector2(10, 2), CursorMode.Auto);
                    break;
            }
        }

        // ── texture builders ──────────────────────────────────────────────

        private static Texture2D BuildPoint()
        {
            const int S = 32;
            var skin = new Color32(240, 195, 145, 255);
            var nail = new Color32(255, 230, 210, 255);
            var px   = Blank(S);

            Rect(px, S, 9, 0, 5, 18, skin);
            Rect(px, S, 10, 0, 3, 3, nail);

            Rect(px, S, 14, 7, 5, 13, skin);
            Rect(px, S, 19, 9, 5, 11, skin);
            Rect(px, S, 24, 11, 4, 9, skin);
            Rect(px, S, 7, 18, 21, 10, skin);
            Rect(px, S, 3, 21, 8, 7, skin);

            return Bake(px, S);
        }

        private static Texture2D BuildSlap()
        {
            // CanSlap hover – open palm facing down
            const int S = 32;
            var skin = new Color32(240, 195, 145, 255);
            var px   = Blank(S);

            Rect(px, S, 1,  0, 4, 18, skin);
            Rect(px, S, 6,  0, 4, 20, skin);
            Rect(px, S, 11, 0, 4, 22, skin);
            Rect(px, S, 16, 0, 4, 20, skin);
            Rect(px, S, 21, 4, 4, 14, skin);
            Rect(px, S, 1, 17, 24, 12, skin);

            return Bake(px, S);
        }

        // Frame 0 – hand compact and high (backswing)
        private static Texture2D BuildSlapRaised()
        {
            const int S = 32;
            var skin = new Color32(240, 195, 145, 255);
            var px   = Blank(S);

            Rect(px, S, 3,  0, 3, 11, skin); // pinky
            Rect(px, S, 7,  0, 3, 13, skin); // ring
            Rect(px, S, 11, 0, 3, 15, skin); // middle
            Rect(px, S, 15, 0, 3, 13, skin); // index
            Rect(px, S, 19, 3, 3,  8, skin); // thumb
            Rect(px, S, 2, 10, 20,  7, skin); // palm (compact)

            return Bake(px, S);
        }

        // Frames 1 & 3 – hand at mid-swing
        private static Texture2D BuildSlapMid()
        {
            const int S = 32;
            var skin = new Color32(240, 195, 145, 255);
            var px   = Blank(S);

            Rect(px, S, 2,  0, 3, 14, skin); // pinky
            Rect(px, S, 6,  0, 4, 16, skin); // ring
            Rect(px, S, 11, 0, 4, 18, skin); // middle
            Rect(px, S, 15, 0, 4, 16, skin); // index
            Rect(px, S, 20, 2, 4, 11, skin); // thumb
            Rect(px, S, 1, 13, 22,  9, skin); // palm

            return Bake(px, S);
        }

        // Frame 2 – full-size impact with yellow fingertip flash
        private static Texture2D BuildSlapImpact()
        {
            const int S = 32;
            var skin  = new Color32(240, 195, 145, 255);
            var flash = new Color32(255, 230, 50, 255);
            var px    = Blank(S);

            Rect(px, S, 1,  0, 4, 18, skin);
            Rect(px, S, 6,  0, 4, 20, skin);
            Rect(px, S, 11, 0, 4, 22, skin);
            Rect(px, S, 16, 0, 4, 20, skin);
            Rect(px, S, 21, 4, 4, 14, skin);
            Rect(px, S, 1, 17, 24, 12, skin);
            // yellow flash on fingertips at impact
            Rect(px, S, 1,  0, 4, 3, flash);
            Rect(px, S, 6,  0, 4, 3, flash);
            Rect(px, S, 11, 0, 4, 3, flash);
            Rect(px, S, 16, 0, 4, 3, flash);

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

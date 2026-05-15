using UnityEngine;

namespace DungKeeper
{
    public class DungeonCursor : MonoBehaviour
    {
        public enum State { Point, CanSlap, Slapping }

        public static DungeonCursor Instance { get; private set; }

        private State _state;
        private Texture2D _pointTex;
        private Texture2D _slapTex;

        public State CurrentState
        {
            get => _state;
            set { if (_state != value) { _state = value; Apply(); } }
        }

        private void Awake()
        {
            Instance = this;
            _pointTex = BuildPoint();
            _slapTex  = BuildSlap();
            Apply();
        }

        private void OnDestroy() =>
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        private void Apply()
        {
            if (_state == State.Slapping)
                Cursor.SetCursor(_slapTex, new Vector2(16, 16), CursorMode.Auto);
            else
                Cursor.SetCursor(_pointTex, new Vector2(10, 2), CursorMode.Auto);
        }

        // ── texture builders ──────────────────────────────────────────────

        private static Texture2D BuildPoint()
        {
            // 32x32 pointing-hand cursor (index finger up)
            const int S = 32;
            var skin  = new Color32(240, 195, 145, 255);
            var nail  = new Color32(255, 230, 210, 255);
            var px    = Blank(S);

            // index finger — columns 9-13, rows 0-17
            Rect(px, S, 9, 0, 5, 18, skin);
            Rect(px, S, 10, 0, 3, 3, nail);        // nail

            // middle finger curled — cols 14-18, rows 7-19
            Rect(px, S, 14, 7, 5, 13, skin);

            // ring finger curled — cols 19-23, rows 9-19
            Rect(px, S, 19, 9, 5, 11, skin);

            // pinky curled — cols 24-27, rows 11-19
            Rect(px, S, 24, 11, 4, 9, skin);

            // palm — cols 7-27, rows 18-28
            Rect(px, S, 7, 18, 21, 10, skin);

            // thumb — cols 3-10, rows 21-27
            Rect(px, S, 3, 21, 8, 7, skin);

            return Bake(px, S);
        }

        private static Texture2D BuildSlap()
        {
            // 32x32 open-palm slap cursor (hand flat, fingers spread down)
            const int S = 32;
            var skin = new Color32(240, 195, 145, 255);
            var px   = Blank(S);

            // fingers (spread, pointing down from palm)
            Rect(px, S, 1,  0, 4, 18, skin); // pinky
            Rect(px, S, 6,  0, 4, 20, skin); // ring
            Rect(px, S, 11, 0, 4, 22, skin); // middle
            Rect(px, S, 16, 0, 4, 20, skin); // index
            Rect(px, S, 21, 4, 4, 14, skin); // thumb

            // palm
            Rect(px, S, 1, 17, 24, 12, skin);

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

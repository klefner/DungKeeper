using UnityEngine;
using UnityEngine.UI;

namespace DungKeeper
{
    /// Custom cursor rendered as a UI overlay so we can animate it freely.
    /// Draws at 512×512 internally, displays at 128px with bilinear filtering
    /// so edges smooth out and the hand looks less blocky.
    public class DungeonCursor : MonoBehaviour
    {
        public enum State { Point, CanSlap, Slapping }
        public static DungeonCursor Instance { get; private set; }
        public bool IsSlapping => _state == State.Slapping;

        private State _state;
        private Texture2D[] _idleFrames;   // gentle finger-wiggle when idle
        private Texture2D[] _slapFrames;   // backhand sweep animation
        private int  _slapFrame, _idleFrame;
        private float _slapTimer, _idleTimer;
        private RawImage     _cursorImg;
        private RectTransform _cursorRect;

        private const int TexSize   = 512;   // internal draw resolution
        private const int DisplayPx = 128;   // on-screen pixel size

        // Per-frame hold times for the slap (pull-back, swing-in, just-before, IMPACT, follow-thru, return)
        private static readonly float[] SlapTimes = { 0.10f, 0.08f, 0.07f, 0.14f, 0.08f, 0.10f };

        // ── public API ────────────────────────────────────────────────────

        public State CurrentState
        {
            get => _state;
            set
            {
                if (_state == State.Slapping || _state == value) return;
                _state = value;
                // no texture change needed — idle loop handles it in Update
            }
        }

        public void TriggerSlap()
        {
            _state     = State.Slapping;
            _slapFrame = 0;
            _slapTimer = 0f;
            _cursorImg.texture = _slapFrames[0];
        }

        // ── lifecycle ─────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
            Cursor.visible = false;

            // Idle: 3 frames, fingers shift slightly to look alive
            _idleFrames = new[]
            {
                BuildHandTex(xOff: 0,  spreadBias: 0f,    curl: 0f,    flash: false),
                BuildHandTex(xOff: 0,  spreadBias: 0.06f, curl: 0.05f, flash: false),
                BuildHandTex(xOff: 0,  spreadBias:-0.04f, curl: 0.08f, flash: false),
            };

            // Slap: backhand sweep — back of hand ALWAYS visible (no palm flip)
            // The hand slides right → center → left (like a real backhand)
            _slapFrames = new[]
            {
                BuildHandTex(xOff:  90, spreadBias:-0.10f, curl: 0.06f, flash: false), // pull-back (right)
                BuildHandTex(xOff:  45, spreadBias:-0.05f, curl: 0.03f, flash: false), // swing in
                BuildHandTex(xOff:  10, spreadBias: 0.05f, curl: 0f,    flash: false), // approaching impact
                BuildHandTex(xOff: -15, spreadBias: 0.15f, curl: 0f,    flash: true),  // IMPACT
                BuildHandTex(xOff: -65, spreadBias: 0.10f, curl: 0.04f, flash: false), // follow-through
                BuildHandTex(xOff:  20, spreadBias: 0f,    curl: 0.02f, flash: false), // return
            };

            CreateUICursor();
            _cursorImg.texture = _idleFrames[0];
        }

        private void CreateUICursor()
        {
            var cgo = new GameObject("CursorCanvas");
            cgo.transform.SetParent(transform);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;

            var igo = new GameObject("CursorImg");
            igo.transform.SetParent(cgo.transform, false);
            _cursorImg              = igo.AddComponent<RawImage>();
            _cursorImg.raycastTarget = false;
            _cursorImg.color        = Color.white;

            _cursorRect = _cursorImg.rectTransform;
            _cursorRect.anchorMin = _cursorRect.anchorMax = Vector2.zero;
            _cursorRect.pivot     = new Vector2(0f, 1f);  // top-left corner tracks mouse
            _cursorRect.sizeDelta = new Vector2(DisplayPx, DisplayPx);
        }

        private void Update()
        {
            _cursorRect.position = Input.mousePosition;

            if (_state == State.Slapping)
            {
                _slapTimer += Time.deltaTime;
                if (_slapTimer >= SlapTimes[_slapFrame])
                {
                    _slapTimer = 0f;
                    _slapFrame++;
                    if (_slapFrame >= _slapFrames.Length)
                    {
                        _state = State.Point;
                        _cursorImg.texture = _idleFrames[_idleFrame];
                        return;
                    }
                    _cursorImg.texture = _slapFrames[_slapFrame];
                }
            }
            else
            {
                _idleTimer += Time.deltaTime;
                if (_idleTimer >= 0.75f)
                {
                    _idleTimer = 0f;
                    _idleFrame = (_idleFrame + 1) % _idleFrames.Length;
                    _cursorImg.texture = _idleFrames[_idleFrame];
                }
            }
        }

        private void OnDestroy() => Cursor.visible = true;

        // ── skin palette ──────────────────────────────────────────────────

        private static readonly Color32
            SkinDark    = new Color32(172, 118, 64,  255),
            SkinMid     = new Color32(205, 155, 98,  255),
            SkinLight   = new Color32(230, 182, 130, 255),
            SkinHighlit = new Color32(248, 210, 162, 255),
            KnuckleDk   = new Color32(145, 96,  44,  255),
            KnuckleMd   = new Color32(188, 138, 82,  255),
            NailBase    = new Color32(218, 176, 136, 255),
            NailShine   = new Color32(248, 220, 192, 255),
            ImpFlash    = new Color32(255, 238, 48,  255),
            ImpRing     = new Color32(255, 160, 30,  180);

        // ── texture builder ───────────────────────────────────────────────

        /// <param name="xOff">Lateral offset: positive = hand shifted right (pulled back), negative = left (follow-through)</param>
        /// <param name="spreadBias">Additional finger spread fraction</param>
        /// <param name="curl">0=fully extended, 1=fully curled (shortens finger heights)</param>
        /// <param name="flash">Draw impact burst on knuckle area</param>
        private static Texture2D BuildHandTex(int xOff, float spreadBias, float curl, bool flash)
        {
            var px = new Color32[TexSize * TexSize];

            // ── layout constants (designed for 512×512, centred at x=256) ──
            // Finger base x-centres at rest (pinky→index, then thumb)
            // Thumb is offset right and starts lower
            float baseSpread = 1f + spreadBias;

            int[] baseCx  = { 148, 208, 270, 332, 395 };   // pinky, ring, mid, index, thumb
            int[] baseTipY = {  55,  28,  12,  28, 105 };   // tip y (0 = top)
            int[] baseH   = { 165, 198, 218, 198, 162 };    // full finger height
            int[] baseW   = {  46,  52,  56,  52,  50 };    // finger width

            int centerX = 256; // hand centre at rest
            int handCentreBase = (baseCx[0] + baseCx[3]) / 2; // ≈240

            int[] cx  = new int[5];
            int[] h   = new int[5];
            int[] w   = new int[5];
            int[] tipY= new int[5];

            for (int i = 0; i < 5; i++)
            {
                // Spread fingers around the hand centre, scaled by baseSpread
                int dx = baseCx[i] - handCentreBase;
                cx[i]  = centerX + (int)(dx * baseSpread) + xOff;
                tipY[i]= baseTipY[i];
                // Curl shortens fingers (raises their tip slightly, lowers their bottom)
                h[i]   = (int)(baseH[i] * (1f - curl * 0.25f));
                w[i]   = baseW[i];
            }

            // Palm position: just below where fingers end (at the MCP joints)
            int palmTop = 220;
            int palmLeft = cx[0] - 32 + xOff / 8;   // slight lag so palm doesn't swing as far
            int palmW    = cx[4] + 32 - cx[0];
            int palmH    = 152;
            // clamp palm so it stays in frame
            if (palmLeft < 8) palmLeft = 8;

            DrawPalm(px, palmLeft, palmTop, palmW, palmH);

            for (int i = 0; i < 5; i++)
                DrawFinger(px, cx[i], tipY[i], h[i], w[i]);

            if (flash)
            {
                int bx = (cx[1] + cx[3]) / 2;
                int by = palmTop - 10;
                FillEllipse(px, bx, by, 55, 38, ImpRing);
                FillEllipse(px, bx, by, 34, 22, ImpFlash);
            }

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // ── element drawers ───────────────────────────────────────────────

        private static void DrawFinger(Color32[] px, int cx, int tipY, int h, int w)
        {
            int r = w / 2;

            // Layer 1 – shadow (full width, rounded)
            RoundRect(px, cx - r, tipY, w, h, r, SkinDark);

            // Layer 2 – mid tone (inset 4px sides)
            int mw = (int)(w * 0.70f), mx = cx - mw / 2;
            RoundRect(px, mx, tipY + 5, mw, h - 10, mw / 2, SkinMid);

            // Layer 3 – highlight strip (left-of-centre, simulating rounded 3-D surface)
            int hw = (int)(w * 0.28f), hx = cx - hw / 2 - 6;
            RoundRect(px, hx, tipY + 8, hw, h - 18, hw / 2, SkinLight);

            // Knuckle joints (DIP at 1/3, PIP at 2/3 down finger)
            int dip = tipY + h / 3;
            int pip = tipY + h * 2 / 3;
            foreach (int jy in new[] { dip, pip })
            {
                FillEllipse(px, cx, jy, r + 3, 10, KnuckleDk);       // dark bump
                FillEllipse(px, cx, jy, r - 3, 6, SkinMid);           // restore centre
                FillEllipse(px, cx - 4, jy - 7, 7, 5, KnuckleMd);    // highlight ridge above
            }

            // Fingernail (back-of-hand = nails visible from top)
            int nailY = tipY + 10;
            int nrx = (int)(r * 0.64f), nry = 20;
            FillEllipse(px, cx, nailY, nrx, nry, NailBase);
            FillEllipse(px, cx - 4, nailY - 6, nrx / 3, 7, NailShine);  // shine spot
        }

        private static void DrawPalm(Color32[] px, int x, int y, int w, int h)
        {
            int r = 30;
            RoundRect(px, x,     y,     w,     h,     r, SkinDark);
            RoundRect(px, x + 6, y + 5, w - 12, h - 10, r, SkinMid);
            // Highlight upper-left area
            int hlW = (int)(w * 0.44f), hlH = (int)(h * 0.52f);
            RoundRect(px, x + 8, y + 7, hlW, hlH, r, SkinLight);
            // Soft brightening at centre of palm
            FillEllipse(px, x + w / 2 - 20, y + h / 2, (int)(w * 0.22f), (int)(h * 0.28f), SkinHighlit);
        }

        // ── drawing primitives ────────────────────────────────────────────

        private static void SetPx(Color32[] px, int x, int y, Color32 c)
        {
            if ((uint)x >= TexSize || (uint)y >= TexSize) return;
            px[(TexSize - 1 - y) * TexSize + x] = c;   // y=0 → top of cursor
        }

        private static void Rect(Color32[] px, int x, int y, int w, int h, Color32 c)
        {
            int x2 = x + w, y2 = y + h;
            for (int row = y; row < y2; row++)
                for (int col = x; col < x2; col++)
                    SetPx(px, col, row, c);
        }

        private static void FillEllipse(Color32[] px, int cx, int cy, int rx, int ry, Color32 c)
        {
            if (rx <= 0 || ry <= 0) return;
            float rxf = rx, ryf = ry;
            for (int y = cy - ry; y <= cy + ry; y++)
                for (int x = cx - rx; x <= cx + rx; x++)
                {
                    float dx = (x - cx) / rxf, dy = (y - cy) / ryf;
                    if (dx * dx + dy * dy <= 1f)
                        SetPx(px, x, y, c);
                }
        }

        private static void RoundRect(Color32[] px, int x, int y, int w, int h, int r, Color32 c)
        {
            if (w <= 0 || h <= 0) return;
            r = Mathf.Min(r, w / 2, h / 2);
            Rect(px, x + r, y, w - 2 * r, h, c);
            Rect(px, x, y + r, w, h - 2 * r, c);
            FillEllipse(px, x + r,     y + r,     r, r, c);
            FillEllipse(px, x + w - r, y + r,     r, r, c);
            FillEllipse(px, x + r,     y + h - r, r, r, c);
            FillEllipse(px, x + w - r, y + h - r, r, r, c);
        }
    }
}

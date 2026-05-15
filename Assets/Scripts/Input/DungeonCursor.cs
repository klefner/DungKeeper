using UnityEngine;
using UnityEngine.UI;

namespace DungKeeper
{
    /// Renders a human hand cursor as a UI overlay.
    /// Draw resolution: 512x512. Display size: 192px (2.67x supersampled → bilinear smoothing).
    ///
    /// IDLE  – palm faces camera, fingers hang down, individual fingers wiggle slowly.
    /// SLAP  – hand pulls UP (wind-up), then DESCENDS (back of hand / knuckles lead),
    ///         impacts, follows through, rises back to idle. This matches the classic
    ///         dungeon-keeper style of reaching down to slap something beneath you.
    public class DungeonCursor : MonoBehaviour
    {
        public enum State { Point, CanSlap, Slapping }
        public static DungeonCursor Instance { get; private set; }
        public bool IsSlapping => _state == State.Slapping;

        private State _state;
        private Texture2D[] _idleFrames;
        private Texture2D[] _slapFrames;
        private int  _slapFrame, _idleFrame;
        private float _slapTimer, _idleTimer;
        private RawImage      _cursorImg;
        private RectTransform _cursorRect;

        private const int TexSize   = 512;
        private const int DisplayPx = 192;

        // Slap hold times: wind-up, descend-fast, descend-faster, IMPACT, follow-thru, rise
        private static readonly float[] SlapTimes = { 0.09f, 0.07f, 0.05f, 0.13f, 0.06f, 0.10f };

        // ── public API ────────────────────────────────────────────────────

        public State CurrentState
        {
            get => _state;
            set
            {
                if (_state == State.Slapping || _state == value) return;
                _state = value;
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

            // Idle: palm faces camera, individual fingers at different extension heights.
            // Frame B/C have one or two fingers slightly shorter (gentle curl) to look alive.
            _idleFrames = new[]
            {
                BuildTex(view: View.Palm, yOff:  0, fingerH: new[]{ 165, 198, 220, 198, 162 }, flash: false),
                BuildTex(view: View.Palm, yOff:  0, fingerH: new[]{ 162, 192, 215, 205, 158 }, flash: false),
                BuildTex(view: View.Palm, yOff:  0, fingerH: new[]{ 168, 205, 210, 192, 165 }, flash: false),
            };

            // Slap: hand rises (wind-up), descends back-of-hand-first, impacts, returns.
            // yOff < 0 = hand drawn higher in frame (pulled up); yOff > 0 = lower (reaching down).
            _slapFrames = new[]
            {
                BuildTex(view: View.Palm, yOff: -45, fingerH: new[]{ 158, 188, 208, 188, 155 }, flash: false), // wind-up (rising)
                BuildTex(view: View.Back, yOff: -15, fingerH: new[]{ 162, 195, 215, 195, 158 }, flash: false), // rotating, starting to descend
                BuildTex(view: View.Back, yOff:  22, fingerH: new[]{ 165, 198, 220, 198, 162 }, flash: false), // descending fast
                BuildTex(view: View.Back, yOff:  52, fingerH: new[]{ 170, 202, 225, 202, 165 }, flash: true),  // IMPACT (lowest point)
                BuildTex(view: View.Back, yOff:  35, fingerH: new[]{ 165, 198, 220, 198, 162 }, flash: false), // follow-through
                BuildTex(view: View.Palm, yOff:   0, fingerH: new[]{ 165, 198, 220, 198, 162 }, flash: false), // risen back to idle
            };

            CreateUICursor();
            _cursorImg.texture = _idleFrames[0];
        }

        private void CreateUICursor()
        {
            var cgo = new GameObject("CursorCanvas");
            cgo.transform.SetParent(transform);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;

            var igo = new GameObject("CursorImg");
            igo.transform.SetParent(cgo.transform, false);
            _cursorImg              = igo.AddComponent<RawImage>();
            _cursorImg.raycastTarget = false;
            _cursorImg.color        = Color.white;

            _cursorRect = _cursorImg.rectTransform;
            _cursorRect.anchorMin = _cursorRect.anchorMax = Vector2.zero;
            _cursorRect.pivot     = new Vector2(0.5f, 1f); // top-centre tracks mouse
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
                if (_idleTimer >= 0.80f)
                {
                    _idleTimer = 0f;
                    _idleFrame = (_idleFrame + 1) % _idleFrames.Length;
                    _cursorImg.texture = _idleFrames[_idleFrame];
                }
            }
        }

        private void OnDestroy() => Cursor.visible = true;

        // ── view enum ─────────────────────────────────────────────────────

        private enum View
        {
            Palm,  // lighter, palm lines, fingernails hidden (facing away)
            Back,  // darker, knuckle bumps, fingernails visible on top
        }

        // ── colour palette ────────────────────────────────────────────────

        // Palm side (lighter)
        private static readonly Color32
            PalmShadow = new Color32(205, 155, 100, 255),
            PalmMid    = new Color32(235, 190, 140, 255),
            PalmLight  = new Color32(255, 218, 172, 255),
            PalmCrease = new Color32(185, 138, 86,  255);

        // Back of hand (darker, knuckles)
        private static readonly Color32
            BackShadow = new Color32(168, 115, 60,  255),
            BackMid    = new Color32(200, 152, 96,  255),
            BackLight  = new Color32(228, 178, 126, 255),
            KnuckleDk  = new Color32(142, 92,  38,  255),
            KnuckleMd  = new Color32(185, 135, 78,  255),
            NailBase   = new Color32(220, 178, 138, 255),
            NailShine  = new Color32(248, 220, 190, 255);

        // Impact
        private static readonly Color32
            ImpFlash = new Color32(255, 238, 48,  255),
            ImpRing  = new Color32(255, 158, 28,  185);

        // ── main texture builder ──────────────────────────────────────────

        /// <param name="view">Palm or Back of hand.</param>
        /// <param name="yOff">Vertical offset in texture coords: negative = hand drawn higher (pulled up), positive = lower (reaching down).</param>
        /// <param name="fingerH">Heights for [pinky, ring, middle, index, thumb].</param>
        /// <param name="flash">Draw impact burst.</param>
        private static Texture2D BuildTex(View view, int yOff, int[] fingerH, bool flash)
        {
            var px = new Color32[TexSize * TexSize];

            // Finger x-centres (fixed horizontal layout, centred in 512px frame)
            int[] cx = { 148, 208, 270, 332, 395 };
            // Finger y tips (where each finger starts from the top of the cursor + yOff)
            int[] tipY = { 55 + yOff, 28 + yOff, 12 + yOff, 28 + yOff, 105 + yOff };
            // Finger widths
            int[] fw = { 46, 52, 56, 52, 50 };

            // Palm
            int palmTop  = 222 + yOff;
            int palmLeft = cx[0] - 36;
            int palmW    = cx[4] + 36 - cx[0];
            int palmH    = 148;

            bool isPalm = view == View.Palm;
            Color32 sh = isPalm ? PalmShadow : BackShadow;
            Color32 md = isPalm ? PalmMid    : BackMid;
            Color32 lt = isPalm ? PalmLight  : BackLight;

            DrawPalm(px, palmLeft, palmTop, palmW, palmH, sh, md, lt, isPalm);

            for (int i = 0; i < 5; i++)
                DrawFinger(px, cx[i], tipY[i], fingerH[i], fw[i], sh, md, lt,
                           showKnuckles: !isPalm, showNail: !isPalm);

            if (isPalm)
                DrawPalmCreases(px, palmLeft, palmTop, palmW, palmH);

            if (flash)
            {
                int bx = (cx[1] + cx[3]) / 2;
                int by = palmTop - 5;
                FillEllipse(px, bx, by, 60, 40, ImpRing);
                FillEllipse(px, bx, by, 36, 24, ImpFlash);
            }

            // Wrist stub at bottom of hand
            DrawWrist(px, palmLeft + 20, palmTop + palmH, palmW - 40, 60, sh, md);

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // ── element drawers ───────────────────────────────────────────────

        private static void DrawFinger(Color32[] px, int cx, int tipY, int h, int w,
                                       Color32 sh, Color32 md, Color32 lt,
                                       bool showKnuckles, bool showNail)
        {
            int r = w / 2;
            // Shadow shell
            RoundRect(px, cx - r, tipY, w, h, r, sh);
            // Mid tone (inset)
            int mw = (int)(w * 0.68f), mx = cx - mw / 2;
            RoundRect(px, mx, tipY + 5, mw, h - 10, mw / 2, md);
            // Highlight (left-of-centre strip suggesting cylinder roundness)
            int hw = (int)(w * 0.26f), hx = cx - hw / 2 - 7;
            if (hw > 4)
                RoundRect(px, hx, tipY + 9, hw, h - 20, hw / 2, lt);

            if (showKnuckles)
            {
                // DIP and PIP joints
                int dip = tipY + h / 3;
                int pip = tipY + h * 2 / 3;
                foreach (int jy in new[] { dip, pip })
                {
                    FillEllipse(px, cx, jy, r + 4, 11, KnuckleDk);
                    FillEllipse(px, cx, jy, r - 2,  6, md);
                    FillEllipse(px, cx - 5, jy - 8, 8, 5, KnuckleMd);
                }
                // Fingernail (back-of-hand = visible from top)
                int nry = tipY + 12;
                FillEllipse(px, cx, nry, (int)(r * 0.62f), 22, NailBase);
                FillEllipse(px, cx - 5, nry - 7, (int)(r * 0.28f), 8, NailShine);
            }
        }

        private static void DrawPalm(Color32[] px, int x, int y, int w, int h,
                                     Color32 sh, Color32 md, Color32 lt, bool isPalm)
        {
            int r = 30;
            RoundRect(px, x,     y,      w,      h,      r, sh);
            RoundRect(px, x + 7, y + 6,  w - 14, h - 12, r, md);
            // Upper-left highlight
            int hlW = (int)(w * 0.42f), hlH = (int)(h * 0.50f);
            RoundRect(px, x + 9, y + 8, hlW, hlH, r, lt);
            // Centre brightening
            FillEllipse(px, x + w / 2 - 18, y + h / 2, (int)(w * 0.20f), (int)(h * 0.26f), lt);
        }

        private static void DrawPalmCreases(Color32[] px, int palmX, int palmY, int palmW, int palmH)
        {
            // Three horizontal creases across the palm suggesting skin folds
            int cx = palmX + palmW / 2;
            int y1 = palmY + (int)(palmH * 0.28f);
            int y2 = palmY + (int)(palmH * 0.52f);
            int y3 = palmY + (int)(palmH * 0.72f);
            int hw = (int)(palmW * 0.55f);
            foreach (int cy in new[] { y1, y2, y3 })
                FillEllipse(px, cx, cy, hw / 2, 3, PalmCrease);
        }

        private static void DrawWrist(Color32[] px, int x, int y, int w, int h,
                                      Color32 sh, Color32 md)
        {
            // Tapered wrist below palm — gives the hand a more grounded look
            RoundRect(px, x, y, w, h, 22, sh);
            RoundRect(px, x + 6, y + 4, w - 12, h - 8, 18, md);
        }

        // ── drawing primitives ────────────────────────────────────────────

        private static void SetPx(Color32[] px, int x, int y, Color32 c)
        {
            if ((uint)x >= TexSize || (uint)y >= TexSize) return;
            px[(TexSize - 1 - y) * TexSize + x] = c;  // y=0 → top of cursor
        }

        private static void Rect(Color32[] px, int x, int y, int w, int h, Color32 c)
        {
            for (int row = y; row < y + h; row++)
                for (int col = x; col < x + w; col++)
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
            Rect(px, x + r, y,     w - 2 * r, h,         c);
            Rect(px, x,     y + r, w,         h - 2 * r, c);
            FillEllipse(px, x + r,     y + r,     r, r, c);
            FillEllipse(px, x + w - r, y + r,     r, r, c);
            FillEllipse(px, x + r,     y + h - r, r, r, c);
            FillEllipse(px, x + w - r, y + h - r, r, r, c);
        }
    }
}

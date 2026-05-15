using UnityEngine;

namespace DungKeeper
{
    /// <summary>
    /// Draws an evil-hand cursor using GL/GUI lines.
    /// Switches between Hover, CanSlap, and Slapping states.
    /// Replace the procedural drawing with real texture assets when available.
    /// </summary>
    public class DungeonCursor : MonoBehaviour
    {
        public enum CursorState { Hover, CanSlap, Slapping }

        public static DungeonCursor Instance { get; private set; }
        public CursorState State { get; set; } = CursorState.Hover;

        private Camera cam;
        private static readonly Color HandColor    = new Color(0.85f, 0.70f, 0.55f);
        private static readonly Color SlapColor    = new Color(1.00f, 0.25f, 0.10f);
        private static readonly Color OutlineColor = new Color(0.10f, 0.05f, 0.00f);

        private void Awake()
        {
            Instance = this;
            Cursor.visible = false;
        }

        private void Start() => cam = Camera.main;

        private void OnDestroy() => Cursor.visible = true;

        private void OnGUI()
        {
            var mp = new Vector2(Input.mousePosition.x,
                                 Screen.height - Input.mousePosition.y);

            Color fill    = State == CursorState.Slapping ? SlapColor : HandColor;
            float angle   = State == CursorState.Slapping ? -35f : 0f;
            float slapOff = State == CursorState.Slapping ? 8f  : 0f;

            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, mp);

            DrawHand(mp + Vector2.up * slapOff, fill, OutlineColor);

            GUI.matrix = matrix;
        }

        private static void DrawHand(Vector2 o, Color fill, Color outline)
        {
            // Palm
            DrawRect(o + new Vector2(-9,  0), 18, 16, fill, outline);

            // Thumb
            DrawRect(o + new Vector2(-13, -6),  7, 10, fill, outline);

            // Fingers
            DrawRect(o + new Vector2(-8, -16),  5, 16, fill, outline);
            DrawRect(o + new Vector2(-3, -18),  5, 18, fill, outline);
            DrawRect(o + new Vector2( 2, -17),  5, 17, fill, outline);
            DrawRect(o + new Vector2( 7, -13),  5, 13, fill, outline);
        }

        private static void DrawRect(Vector2 pos, float w, float h, Color fill, Color outline)
        {
            var r = new Rect(pos.x, pos.y - h, w, h);
            GUI.color = fill;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = outline;
            GUI.DrawTexture(new Rect(r.x - 1,      r.y - 1,      r.width + 2, 1),           Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x - 1,      r.y + r.height, r.width + 2, 1),         Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x - 1,      r.y - 1,      1,           r.height + 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x + r.width, r.y - 1,     1,           r.height + 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}

using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>Optional border drawn around every overlay panel, toggled and recolored live from the
    /// F1 control center (persisted via Plugin.BorderOn / Plugin.BorderColor). Each overlay calls
    /// <see cref="Draw"/> right after painting its background box, inside its own GUI.matrix, so the
    /// border scales with the panel.</summary>
    public static class PanelBorder
    {
        public struct Preset { public string Id; public Color C; }
        public static readonly Preset[] Palette = new[]
        {
            new Preset { Id = "white",   C = new Color(1f, 1f, 1f) },
            new Preset { Id = "gold",    C = new Color(1f, 0.84f, 0.35f) },
            new Preset { Id = "green",   C = new Color(0.45f, 0.95f, 0.6f) },
            new Preset { Id = "cyan",    C = new Color(0.4f, 0.85f, 1f) },
            new Preset { Id = "blue",    C = new Color(0.45f, 0.6f, 1f) },
            new Preset { Id = "magenta", C = new Color(0.9f, 0.55f, 1f) },
            new Preset { Id = "red",     C = new Color(1f, 0.45f, 0.5f) },
        };

        private static Texture2D _white;
        private const float Th = 2f;   // thickness in panel-local px (GUI.matrix scales it with the panel)

        public static Color Current()
        {
            string id = null;
            try { id = Plugin.BorderColor.Value; } catch { }
            foreach (var p in Palette) if (p.Id == id) return p.C;
            return Palette[0].C;
        }

        public static void Draw(Rect r)
        {
            bool on = false;
            try { on = Plugin.BorderOn.Value; } catch { }
            if (!on) return;
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            var prev = GUI.color;
            GUI.color = Current();
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, Th), _white);                    // top
            GUI.DrawTexture(new Rect(r.x, r.yMax - Th, r.width, Th), _white);            // bottom
            GUI.DrawTexture(new Rect(r.x, r.y + Th, Th, r.height - Th * 2), _white);     // left
            GUI.DrawTexture(new Rect(r.xMax - Th, r.y + Th, Th, r.height - Th * 2), _white); // right
            GUI.color = prev;
        }
    }
}

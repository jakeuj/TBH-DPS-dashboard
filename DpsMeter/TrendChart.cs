using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>Shared clear-time trend plot, extracted from the F11 stage-compare panel so the
    /// loot-heatmap panel can reuse the exact same chart. X = attempt (oldest→newest), Y = value
    /// (clear seconds). Optionally highlights a baseline point (gold) and a selected point (white ring),
    /// and fills caller-provided lists with clickable point rects.</summary>
    public static class TrendChart
    {
        /// <param name="area">Plot region as (x=ix, y, width=iw, height=plotH) — same args the old
        /// DrawChartSection took.</param>
        /// <param name="panelX">Panel left edge, for the min/max axis labels (was _rect.x).</param>
        /// <param name="baselineIndex">Index to mark gold, or -1.</param>
        /// <param name="selectedIndex">Index to ring white, or -1.</param>
        /// <param name="outPointRects">Cleared+filled with each point's hit rect; null to skip.</param>
        /// <param name="outPointIndex">Cleared+filled with each point's index; null to skip.</param>
        public static void Draw(Rect area, float panelX, IReadOnlyList<float> values,
            int baselineIndex, int selectedIndex, Texture2D white, GUIStyle tiny,
            List<Rect> outPointRects, List<int> outPointIndex, string unit = "s")
        {
            float px = area.x + 30, pw = area.width - 36, py = area.y, ph = area.height;
            DrawRect(white, px, py, pw, ph, new Color(0f, 0f, 0f, 1f));
            DrawRect(white, px, py, pw, 1, new Color(1, 1, 1, 0.12f));
            DrawRect(white, px, py + ph - 1, pw, 1, new Color(1, 1, 1, 0.12f));

            if (outPointRects != null) outPointRects.Clear();
            if (outPointIndex != null) outPointIndex.Clear();

            int n = values != null ? values.Count : 0;
            float maxDur = 1f, minDur = float.MaxValue;
            for (int i = 0; i < n; i++) { float d = values[i]; if (d > maxDur) maxDur = d; if (d > 0 && d < minDur) minDur = d; }
            if (minDur == float.MaxValue) minDur = 0f;
            float span = Mathf.Max(1f, maxDur - minDur);

            GUI.Label(new Rect(panelX + 2, py - 6, 36, 14), $"<size=9>{maxDur:0}{unit}</size>", tiny);
            GUI.Label(new Rect(panelX + 2, py + ph - 12, 36, 14), $"<size=9>{minDur:0}{unit}</size>", tiny);

            float dx = n > 1 ? pw / (n - 1) : 0f;
            Vector2 prev = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                float t = (values[i] - minDur) / span;
                float ptx = n > 1 ? px + dx * i : px + pw * 0.5f;
                float pty = py + ph - 8 - t * (ph - 16);
                if (i > 0) DrawLine(white, prev, new Vector2(ptx, pty), new Color(0.45f, 0.7f, 1f, 0.9f));
                prev = new Vector2(ptx, pty);
                bool isBase = i == baselineIndex;
                bool isSel = i == selectedIndex;
                if (isSel) DrawRect(white, ptx - 6, pty - 6, 12, 12, new Color(1, 1, 1, 0.9f));   // selected ring
                var col = isBase ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.66f, 0.98f);
                float ds = isBase ? 9f : 7f;
                DrawRect(white, ptx - ds / 2, pty - ds / 2, ds, ds, col);
                if (outPointRects != null) outPointRects.Add(new Rect(ptx - 9, pty - 9, 18, 18));
                if (outPointIndex != null) outPointIndex.Add(i);
            }
            GUI.Label(new Rect(px - 4, py + ph + 2, 40, 14), "<size=9>#1</size>", tiny);
            if (n > 1) GUI.Label(new Rect(px + pw - 24, py + ph + 2, 30, 14), $"<size=9>#{n}</size>", tiny);
        }

        private static void DrawRect(Texture2D white, float x, float y, float w, float h, Color c)
        {
            var p = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), white); GUI.color = p;
        }

        private static void DrawLine(Texture2D white, Vector2 a, Vector2 b, Color c)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                DrawRect(white, p.x - 1, p.y - 1, 2, 2, c);
            }
        }
    }
}

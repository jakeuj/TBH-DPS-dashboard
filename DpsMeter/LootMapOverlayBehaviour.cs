using System;
using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay (F-key, hub slot 6): the 掉寶熱力圖 / Loot Heatmap. Shows two stacked
    /// day × 24-hour grids computed live from the box-open log — open rate (blue) and good-loot rate
    /// (gold-green) — a summary row, and the shared clear-time trend chart (per recent run) at the
    /// bottom. Read-only; resize-safe; drag from anywhere; auto-hides with the other panels while a
    /// game menu is open.</summary>
    public class LootMapOverlayBehaviour : MonoBehaviour
    {
        public LootMapOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 10f;
        private const int MaxDayRows = 7;        // two grids → keep compact
        private const float ChartH = 100f;
        private const int GoodGrade = 3;         // grade >= 3 counts as "good loot"

        private Rect _rect = new Rect(24, 120, 560, 0);
        private bool _visible, _placed;
        private float _wantX, _wantY;
        private Vector2 _dragOffset; private bool _dragging;

        private Texture2D _white, _bgTex;
        private GUIStyle _title, _label, _dim, _tiny, _btn, _box, _tip;
        private bool _stylesReady;

        private Rect _closeRect;
        private float _scale = 1f;

        // hover tooltip target (local-space cell rect + text), filled during the grid draw
        private bool _hasTip; private Rect _tipCell; private string _tipText;

        // per-run clear seconds for the trend chart, rebuilt only when RunStore.Version changes
        private readonly List<float> _trend = new List<float>();
        private int _trendVersion = -1;

        private Rect ScaledRect() => new Rect(_rect.x, _rect.y, _rect.width * _scale, _rect.height * _scale);

        void Awake()
        {
            _rect.width = Mathf.Max(560, Plugin.LootMapPanelWidth.Value);
            _visible = Plugin.LootMapStartVisible.Value;
            PanelRegistry.Register("lootmap", 6, "▦", () => Loc.G("lootmap_title"), Plugin.LootMapToggleKey, () => _visible, v => _visible = v);
        }
        void Start() => PlaceDefault();

        private void PlaceDefault()
        {
            float px = Plugin.LootMapPosX.Value, py = Plugin.LootMapPosY.Value;
            if (px < 0 || py < 0) { _rect.x = 24f; _rect.y = 120f; }
            else { _rect.x = px; _rect.y = py; }
            _wantX = _rect.x; _wantY = _rect.y; _placed = true;
        }

        void Update()
        {
            try
            {
                InputCompat.SetPanel(7, _visible && !GameUiState.MenuOpen(), ScaledRect());
                if (InputCompat.KeyPressed(Plugin.LootMapToggleKey)) _visible = !_visible;
                if (_visible) HandlePointer();
                else if (_dragging) _dragging = false;
            }
            catch { }
        }

        private void HandlePointer()
        {
            if (GameUiState.MenuOpen()) { if (_dragging) { _dragging = false; InputCompat.ReleaseDrag(7); } return; }
            Vector2 m = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);
            if (InputCompat.MousePressed())
            {
                if (_closeRect.Contains(m)) { _visible = false; return; }
                // drag from anywhere on the panel except the buttons (handled/returned above)
                if (_rect.Contains(m) && InputCompat.ClaimDrag(7)) { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            }
            if (_dragging)
            {
                if (!InputCompat.OwnsDrag(7)) { _dragging = false; return; }
                if (InputCompat.MouseHeld()) { _rect.x = m.x - _dragOffset.x; _rect.y = m.y - _dragOffset.y; }
                if (InputCompat.MouseReleased()) { _dragging = false; InputCompat.ReleaseDrag(7); _wantX = _rect.x; _wantY = _rect.y; Plugin.LootMapPosX.Value = _rect.x; Plugin.LootMapPosY.Value = _rect.y; }
            }
        }

        private void EnsureAssets()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_bgTex == null) { _bgTex = new Texture2D(1, 1); _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f)); _bgTex.Apply(); if (_box != null) _box.normal.background = _bgTex; }
            if (_stylesReady) return;
            int fs = Plugin.FontSize.Value;
            _title = new GUIStyle { fontSize = fs, fontStyle = FontStyle.Bold, richText = true }; _title.normal.textColor = new Color(1f, 0.86f, 0.35f);
            _label = new GUIStyle { fontSize = fs, richText = true }; _label.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            _dim = new GUIStyle { fontSize = fs - 2, richText = true }; _dim.normal.textColor = new Color(0.78f, 0.84f, 0.95f);
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4), richText = true }; _tiny.normal.textColor = new Color(0.7f, 0.75f, 0.85f);
            _btn = new GUIStyle(GUI.skin.button) { fontSize = fs - 2, fontStyle = FontStyle.Bold, richText = true };
            _tip = new GUIStyle { fontSize = fs - 2, richText = true, alignment = TextAnchor.MiddleCenter }; _tip.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
            _box = new GUIStyle(); _box.normal.background = _bgTex;
            _stylesReady = true;
        }

        private void DrawRect(float x, float y, float w, float h, Color c) { var p = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = p; }

        // dark slate -> bright ramp by normalized intensity. metric 0 = opens (blue), 1 = loot (gold-green).
        private static Color RampColor(float t, int metric)
        {
            t = Mathf.Clamp01(t);
            var lo = new Color(0.10f, 0.13f, 0.18f);
            var hi = metric == 1 ? new Color(0.95f, 0.82f, 0.30f) : new Color(0.30f, 0.62f, 1.00f);
            return Color.Lerp(lo, hi, t);
        }

        // Per-run clear seconds for the trend chart (chronological), most-recent ~30 runs.
        // Rebuilt only when RunStore.Version changes.
        private void RebuildTrend()
        {
            if (RunStore.Version == _trendVersion) return;
            _trendVersion = RunStore.Version;
            _trend.Clear();
            try
            {
                var runs = RunStore.LoadAll();           // oldest → newest
                int start = Mathf.Max(0, runs.Count - 30);
                for (int i = start; i < runs.Count; i++) _trend.Add(runs[i].Duration);
            }
            catch { }
        }

        void OnGUI()
        {
            if (!_visible || GameUiState.MenuOpen()) return;
            GUI.depth = -8;
            var prevM = GUI.matrix;
            try
            {
                EnsureAssets();
                if (!_placed) PlaceDefault();
                RebuildTrend();

                // ---- single pass over the box-open log → per-day hourly opens / good buckets ----
                var opensByDay = new Dictionary<string, long[]>();
                var goodByDay = new Dictionary<string, long[]>();
                long totalOpens = 0, totalGood = 0;
                try
                {
                    var log = BoxOpenTracker.Stats.Log;
                    for (int i = 0; i < log.Count; i++)
                    {
                        var e = log[i];
                        if (e == null) continue;
                        int g = e.Grade; if (g < 0) g = 0; else if (g > 9) g = 9;
                        string day = e.Time.ToString("yyyy-MM-dd");
                        int hh = e.Time.Hour; if (hh < 0) hh = 0; else if (hh > 23) hh = 23;
                        if (!opensByDay.TryGetValue(day, out var oArr)) { oArr = new long[24]; opensByDay[day] = oArr; goodByDay[day] = new long[24]; }
                        oArr[hh]++; totalOpens++;
                        if (g >= GoodGrade) { goodByDay[day][hh]++; totalGood++; }
                    }
                }
                catch { }

                // sorted distinct days, newest first, capped at MaxDayRows
                var days = new List<string>(opensByDay.Keys);
                days.Sort(StringComparer.Ordinal);
                days.Reverse();                                   // newest first
                if (days.Count > MaxDayRows) days = days.GetRange(0, MaxDayRows);
                int dayRows = days.Count;

                int fs = Plugin.FontSize.Value; float lh = fs + 6;
                bool hasChart = _trend.Count > 0;

                float cellH = lh + 2f;
                float oneGridH = dayRows > 0 ? lh /*grid header*/ + dayRows * cellH + lh /*hour axis*/ : lh /*grid header*/ + lh /*empty*/;
                float gridsH = oneGridH * 2f + lh * 0.4f /*gap between grids*/;

                // height = title + summary + two grids + (separator + chart header + chart) + padding
                float h = Pad + lh /*title*/ + lh /*summary*/ + gridsH;
                if (hasChart) h += lh * 0.6f /*sep*/ + lh /*chart header*/ + ChartH + lh;
                h += Pad;
                _rect.height = h;

                _scale = UiScale.Fit(_rect.width, _rect.height);
                if (!_dragging) { _rect.x = Mathf.Clamp(_wantX, 0f, Mathf.Max(0f, Screen.width - _rect.width * _scale)); _rect.y = Mathf.Clamp(_wantY, 0f, Mathf.Max(0f, Screen.height - _rect.height * _scale)); }
                float x = _rect.x, ix = x + Pad, w = _rect.width, iw = w - Pad * 2;
                GUI.matrix = UiScale.Matrix(_rect.x, _rect.y, _scale);
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;

                // ---- title bar ----
                GUI.Label(new Rect(ix, cy, iw - 26, lh), Loc.G("lootmap_title"), _title);
                _closeRect = new Rect(x + w - 26, cy - 2, 22, lh); GUI.Button(_closeRect, "✕", _btn);
                cy += lh;

                // ---- summary row: total opens / total good ----
                GUI.Label(new Rect(ix, cy, iw, lh),
                    $"<color=#aeb6c2>{Loc.G("lm_total")} <color=#eaf3ee>{totalOpens}</color>　{Loc.G("metric_loot")} <color=#eaf3ee>{totalGood}</color></color>", _label);
                cy += lh;

                // ---- two heatmap grids (opens, then loot) ----
                _hasTip = false;
                Vector2 mLocal = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);

                // grid max over visible cells (own normalization per grid)
                long maxOpens = 1, maxGood = 1;
                for (int r = 0; r < dayRows; r++)
                {
                    var oArr = opensByDay[days[r]];
                    var gArr = goodByDay[days[r]];
                    for (int hh = 0; hh < 24; hh++) { if (oArr[hh] > maxOpens) maxOpens = oArr[hh]; if (gArr[hh] > maxGood) maxGood = gArr[hh]; }
                }

                cy = DrawGrid(ix, cy, iw, lh, cellH, mLocal, days, dayRows, opensByDay, Loc.G("metric_opens"), maxOpens, 0);
                cy += lh * 0.4f;
                cy = DrawGrid(ix, cy, iw, lh, cellH, mLocal, days, dayRows, goodByDay, Loc.G("metric_loot"), maxGood, 1);

                // ---- separator + clear-time trend chart ----
                if (hasChart)
                {
                    DrawRect(ix, cy + lh * 0.3f, iw, 1, new Color(1, 1, 1, 0.12f));
                    cy += lh * 0.6f;
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#9fb4cc>{Loc.G("trend")}</color>", _dim);
                    cy += lh;
                    TrendChart.Draw(new Rect(ix, cy, iw, ChartH), _rect.x, _trend, -1, -1, _white, _tiny, null, null, "s");
                    cy += ChartH + lh;
                }

                // ---- hover tooltip (drawn last, on top) ----
                if (_hasTip)
                {
                    var sz = _tip.CalcSize(new GUIContent(_tipText));
                    float tw = sz.x + 12f, th = lh;
                    float tx = Mathf.Clamp(_tipCell.x + _tipCell.width * 0.5f - tw * 0.5f, ix, ix + iw - tw);
                    float ty = _tipCell.y - th - 2f; if (ty < _rect.y) ty = _tipCell.y + _tipCell.height + 2f;
                    DrawRect(tx, ty, tw, th, new Color(0.05f, 0.06f, 0.09f, 0.96f));
                    DrawRect(tx, ty, tw, 1, new Color(1, 1, 1, 0.18f));
                    GUI.Label(new Rect(tx, ty, tw, th), $"<color=#eaf3ee>{_tipText}</color>", _tip);
                }
            }
            catch { }
            finally { GUI.matrix = prevM; }
        }

        // Draws one labeled heatmap grid (header + day gutter + 24 hour columns + hour-axis ticks).
        // Returns the y just below the grid. Records a hover tooltip into _hasTip/_tipCell/_tipText.
        private float DrawGrid(float ix, float cy, float iw, float lh, float cellH, Vector2 mLocal,
            List<string> days, int dayRows, Dictionary<string, long[]> data, string header, long maxVisible, int metric)
        {
            GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#9fb4cc>{header}</color>", _dim);
            cy += lh;

            if (dayRows == 0)
            {
                GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#8a93a0>{Loc.G("box_empty")}</color>", _tiny);
                return cy + lh;
            }

            float gutterW = Mathf.Max(40f, iw * 0.11f);
            float gridX = ix + gutterW;
            float gridW = iw - gutterW;
            float cellW = gridW / 24f;

            float gy = cy;
            for (int r = 0; r < dayRows; r++)
            {
                string day = days[r];
                string shortDay = day.Length >= 10 ? day.Substring(5) : day;  // MM-DD
                GUI.Label(new Rect(ix, gy, gutterW - 2, cellH), $"<color=#aeb6c2>{shortDay}</color>", _tiny);
                var arr = data[day];
                for (int hh = 0; hh < 24; hh++)
                {
                    long v = arr[hh];
                    var cr = new Rect(gridX + hh * cellW, gy, cellW - 1f, cellH - 1f);
                    DrawRect(cr.x, cr.y, cr.width, cr.height, RampColor(v / (float)maxVisible, metric));
                    if (cr.Contains(mLocal)) { _hasTip = true; _tipCell = cr; _tipText = $"{shortDay} {hh:00}:00 · {v}"; }
                }
                gy += cellH;
            }

            // hour axis (0,6,12,18)
            int[] ticks = { 0, 6, 12, 18 };
            foreach (int t in ticks)
                GUI.Label(new Rect(gridX + t * cellW, gy, cellW * 3, lh), $"<size=9><color=#7a8390>{t}</color></size>", _tiny);
            return gy + lh;
        }
    }
}

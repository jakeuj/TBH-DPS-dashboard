using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay (F-key, hub slot 6): the 掉寶熱力圖 / Loot Heatmap. Shows a day × 24-hour
    /// grid of box-open activity with a metric toggle (open rate vs good-loot rate), a summary row, and
    /// the shared F11 clear-time trend chart at the bottom. Read-only; resize-safe; drag from anywhere;
    /// auto-hides with the other panels while a game menu is open.</summary>
    public class LootMapOverlayBehaviour : MonoBehaviour
    {
        public LootMapOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 10f;
        private const int MaxDayRows = 14;
        private const float ChartH = 100f;

        private Rect _rect = new Rect(24, 120, 480, 0);
        private bool _visible, _placed;
        private float _wantX, _wantY;
        private Vector2 _dragOffset; private bool _dragging;

        private Texture2D _white, _bgTex;
        private GUIStyle _title, _label, _dim, _tiny, _btn, _box, _icon, _tip;
        private bool _stylesReady;

        private Rect _closeRect, _mOpensRect, _mLootRect;
        private int _metric;
        private float _scale = 1f;

        // hover tooltip target (local-space cell rect + text), filled during the grid draw
        private bool _hasTip; private Rect _tipCell; private string _tipText;

        // lazily-cached run durations for the trend chart, rebuilt when RunStore changes
        private int _seenRunsVersion = -1;
        private readonly List<float> _trend = new List<float>();

        private Rect ScaledRect() => new Rect(_rect.x, _rect.y, _rect.width * _scale, _rect.height * _scale);

        void Awake()
        {
            _rect.width = Mathf.Max(480, Plugin.LootMapPanelWidth.Value);
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
                Plugin.Loot.Observe(BoxOpenTracker.Stats);
                Plugin.Loot.SaveIfDirty(Time.time);
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
                if (_mOpensRect.Contains(m)) { _metric = 0; return; }
                if (_mLootRect.Contains(m)) { _metric = 1; return; }
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
            _icon = new GUIStyle { fontSize = fs + 3, richText = true, alignment = TextAnchor.MiddleCenter }; _icon.normal.textColor = Color.white;
            _tip = new GUIStyle { fontSize = fs - 2, richText = true, alignment = TextAnchor.MiddleCenter }; _tip.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
            _box = new GUIStyle(); _box.normal.background = _bgTex;
            _stylesReady = true;
        }

        private void DrawRect(float x, float y, float w, float h, Color c) { var p = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = p; }

        private void RebuildTrendIfNeeded()
        {
            if (RunStore.Version == _seenRunsVersion) return;
            _seenRunsVersion = RunStore.Version;
            _trend.Clear();
            try
            {
                var runs = RunStore.LoadAll();        // oldest..newest
                int start = Mathf.Max(0, runs.Count - 30);
                for (int i = start; i < runs.Count; i++) _trend.Add(runs[i].Duration);
            }
            catch { }
        }

        // dark slate -> bright (green for good-loot, blue for opens) ramp by normalized intensity
        private static Color RampColor(float t, int metric)
        {
            t = Mathf.Clamp01(t);
            var lo = new Color(0.10f, 0.13f, 0.18f);
            var hi = metric == 1 ? new Color(0.30f, 0.95f, 0.45f) : new Color(0.30f, 0.62f, 1.00f);
            return Color.Lerp(lo, hi, t);
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
                RebuildTrendIfNeeded();

                int fs = Plugin.FontSize.Value; float lh = fs + 6;
                var loot = Plugin.Loot;
                var days = loot.Days();                          // newest first
                int dayRows = Mathf.Min(MaxDayRows, days.Count);
                bool hasChart = _trend.Count > 0;

                float cellH = lh + 2f;
                float gridH = dayRows > 0 ? dayRows * cellH + lh /*hour axis*/ : lh;

                // height = title + summary + metric toggle + grid + (separator+chart) + padding
                float h = Pad + lh /*title*/ + lh /*summary*/ + lh /*metric toggle*/ + gridH;
                if (hasChart) h += lh * 0.6f + ChartH + lh;
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

                // ---- summary row: total / today / week ----
                long total = loot.Total(_metric);
                string nowDay = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                long today = (days.Count > 0 && days[0] == nowDay) ? loot.DayTotal(nowDay, _metric) : 0;
                long week = 0;
                DateTime weekFloor = DateTime.Now.Date.AddDays(-6);
                foreach (var d in days)
                    if (DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) && dt.Date >= weekFloor)
                        week += loot.DayTotal(d, _metric);
                GUI.Label(new Rect(ix, cy, iw, lh),
                    $"<color=#aeb6c2>{Loc.G("lm_total")} <color=#eaf3ee>{total}</color>　{Loc.G("lm_today")} <color=#eaf3ee>{today}</color>　{Loc.G("lm_week")} <color=#eaf3ee>{week}</color></color>", _label);
                cy += lh;

                // ---- metric toggle ----
                float btnW = Mathf.Max(70f, iw * 0.30f);
                _mOpensRect = new Rect(ix, cy, btnW, lh - 2);
                _mLootRect = new Rect(ix + btnW + 6, cy, btnW, lh - 2);
                DrawToggle(_mOpensRect, Loc.G("metric_opens"), _metric == 0);
                DrawToggle(_mLootRect, Loc.G("metric_loot"), _metric == 1);
                cy += lh;

                // ---- heatmap ----
                _hasTip = false;
                if (dayRows == 0)
                {
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#8a93a0>{Loc.G("box_empty")}</color>", _tiny);
                }
                else
                {
                    Vector2 mLocal = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);
                    float gutterW = Mathf.Max(40f, iw * 0.11f);
                    float gridX = ix + gutterW;
                    float gridW = iw - gutterW;
                    float cellW = gridW / 24f;

                    // max over all visible cells (for intensity normalization)
                    long maxVisible = 1;
                    for (int r = 0; r < dayRows; r++)
                        for (int hh = 0; hh < 24; hh++)
                        {
                            long v = loot.Value(days[r], hh, _metric);
                            if (v > maxVisible) maxVisible = v;
                        }

                    float gy = cy;
                    for (int r = 0; r < dayRows; r++)
                    {
                        string day = days[r];
                        string shortDay = day.Length >= 10 ? day.Substring(5) : day;  // MM-DD
                        GUI.Label(new Rect(ix, gy, gutterW - 2, cellH), $"<color=#aeb6c2>{shortDay}</color>", _tiny);
                        for (int hh = 0; hh < 24; hh++)
                        {
                            long v = loot.Value(day, hh, _metric);
                            var cr = new Rect(gridX + hh * cellW, gy, cellW - 1f, cellH - 1f);
                            DrawRect(cr.x, cr.y, cr.width, cr.height, RampColor(v / (float)maxVisible, _metric));
                            if (cr.Contains(mLocal)) { _hasTip = true; _tipCell = cr; _tipText = $"{shortDay} {hh:00}:00 · {v}"; }
                        }
                        gy += cellH;
                    }

                    // hour axis (0,6,12,18)
                    int[] ticks = { 0, 6, 12, 18 };
                    foreach (int t in ticks)
                        GUI.Label(new Rect(gridX + t * cellW, gy, cellW * 3, lh), $"<size=9><color=#7a8390>{t}</color></size>", _tiny);
                    cy = gy + lh;
                }

                // ---- separator + trend chart ----
                if (hasChart)
                {
                    DrawRect(ix, cy + lh * 0.3f, iw, 1, new Color(1, 1, 1, 0.12f));
                    cy += lh * 0.6f;
                    TrendChart.Draw(new Rect(ix, cy, iw, ChartH), _rect.x, _trend, -1, -1, _white, _tiny, null, null);
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

        private void DrawToggle(Rect r, string text, bool active)
        {
            GUI.Button(r, GUIContent.none, _btn);
            if (active)
            {
                DrawRect(r.x + 2, r.y + r.height - 3, r.width - 4, 2, new Color(0.5f, 1f, 0.63f, 0.95f)); // lit underline
                DrawRect(r.x, r.y, r.width, r.height, new Color(0.5f, 1f, 0.63f, 0.10f));
            }
            string col = active ? "#eaf3ee" : "#9aa3b0";
            GUI.Label(r, $"<color={col}>{text}</color>", _icon);
        }
    }
}

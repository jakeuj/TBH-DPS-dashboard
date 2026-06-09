using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay (F-key, hub slot 6): the 掉寶熱力圖 / Loot Heatmap. Visualizes existing
    /// trackers over time — two stacked day × 24-hour grids: top = F5 box pickups (BoxTracker.Events,
    /// blue), bottom = ALL F4 opens (BoxOpenTracker.Stats.Log, gold-green) with a per-cell detail hover
    /// listing each open — a summary row, and a clear-time trend chart that follows F11's selected stage.
    /// Read-only; resize-safe; drag from anywhere; auto-hides with the other panels while a game menu
    /// is open.</summary>
    public class LootMapOverlayBehaviour : MonoBehaviour
    {
        public LootMapOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 10f;
        private const int MaxDayRows = 7;        // two grids → keep compact
        private const float ChartH = 100f;

        private Rect _rect = new Rect(24, 120, 560, 0);
        private bool _visible, _placed;
        private float _wantX, _wantY;
        private Vector2 _dragOffset; private bool _dragging;

        private Texture2D _white, _bgTex;
        private GUIStyle _title, _label, _dim, _tiny, _btn, _box, _tip, _tipMulti;
        private bool _stylesReady;

        private Rect _closeRect;
        private float _scale = 1f;

        // hover tooltip target (local-space cell rect + text), filled during the grid draw
        private bool _hasTip; private Rect _tipCell; private string _tipText;

        // per-run clear seconds for the trend chart (F11's currently-selected stage), rebuilt when
        // RunStore.Version OR the resolved stage changes
        private readonly List<float> _trend = new List<float>();
        private int _trendVersion = -1;
        private string _trendStage;

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
            _tipMulti = new GUIStyle { fontSize = fs - 2, richText = true, wordWrap = false, alignment = TextAnchor.UpperLeft }; _tipMulti.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
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

        // Per-run clear seconds for the trend chart, following F11's currently-selected stage
        // (CompareOverlayBehaviour.ActiveStageId; falls back to the most-recent run's stage).
        // Chronological (oldest → newest), capped to the last ~40 runs of that stage.
        // Rebuilt only when RunStore.Version OR the resolved stage changes.
        private void RebuildTrend()
        {
            try
            {
                var runs = RunStore.LoadAll();           // oldest → newest

                string stage = CompareOverlayBehaviour.ActiveStageId;
                if (string.IsNullOrEmpty(stage) && runs.Count > 0) stage = runs[runs.Count - 1].StageId;

                if (RunStore.Version == _trendVersion && stage == _trendStage) return;
                _trendVersion = RunStore.Version;
                _trendStage = stage;
                _trend.Clear();
                if (string.IsNullOrEmpty(stage)) return;

                for (int i = 0; i < runs.Count; i++)
                    if (runs[i] != null && runs[i].StageId == stage) _trend.Add(runs[i].Duration);
                if (_trend.Count > 40) _trend.RemoveRange(0, _trend.Count - 40);
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

                // ---- TOP grid source: F5 box pickups (BoxTracker.Events) bucketed by (day, hour) ----
                var pickupByDay = new Dictionary<string, long[]>();
                long totalPickups = 0;
                try
                {
                    var ev = BoxTracker.Events;
                    for (int i = 0; i < ev.Count; i++)
                    {
                        var e = ev[i];
                        if (e == null) continue;
                        string day = e.Time.ToString("yyyy-MM-dd");
                        int hh = e.Time.Hour; if (hh < 0) hh = 0; else if (hh > 23) hh = 23;
                        if (!pickupByDay.TryGetValue(day, out var pArr)) { pArr = new long[24]; pickupByDay[day] = pArr; }
                        pArr[hh]++; totalPickups++;
                    }
                }
                catch { }

                // ---- BOTTOM grid source: ALL F4 opens (BoxOpenTracker.Stats.Log) by (day, hour) ----
                var goodByDay = new Dictionary<string, long[]>();
                var openEvByCell = new Dictionary<string, List<BoxOpenEvent>>();   // "day|hour" -> events
                long totalOpen = 0;
                try
                {
                    var log = BoxOpenTracker.Stats.Log;
                    for (int i = 0; i < log.Count; i++)
                    {
                        var e = log[i];
                        if (e == null) continue;
                        if (e.Grade < 3) continue;   // 開箱紀錄: legendary (傳說) and above only
                        string day = e.Time.ToString("yyyy-MM-dd");
                        int hh = e.Time.Hour; if (hh < 0) hh = 0; else if (hh > 23) hh = 23;
                        if (!goodByDay.TryGetValue(day, out var gArr)) { gArr = new long[24]; goodByDay[day] = gArr; }
                        gArr[hh]++; totalOpen++;
                        string ckey = day + "|" + hh;
                        if (!openEvByCell.TryGetValue(ckey, out var lst)) { lst = new List<BoxOpenEvent>(); openEvByCell[ckey] = lst; }
                        lst.Add(e);
                    }
                }
                catch { }

                // sorted distinct days across BOTH grids, newest first, capped at MaxDayRows
                var daySet = new HashSet<string>(pickupByDay.Keys);
                foreach (var k in goodByDay.Keys) daySet.Add(k);
                var days = new List<string>(daySet);
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

                // ---- summary row: total pickups (F5) / total opens (F4) ----
                GUI.Label(new Rect(ix, cy, iw, lh),
                    $"<color=#aeb6c2>{Loc.G("metric_pickup")} <color=#eaf3ee>{totalPickups}</color>　{Loc.G("metric_openlog")} <color=#eaf3ee>{totalOpen}</color></color>", _label);
                cy += lh;

                // ---- two heatmap grids (opens, then loot) ----
                _hasTip = false;
                Vector2 mLocal = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);

                // grid max over visible cells (own normalization per grid)
                long maxPickup = 1, maxOpen = 1;
                for (int r = 0; r < dayRows; r++)
                {
                    pickupByDay.TryGetValue(days[r], out var pArr);
                    goodByDay.TryGetValue(days[r], out var gArr);
                    for (int hh = 0; hh < 24; hh++)
                    {
                        if (pArr != null && pArr[hh] > maxPickup) maxPickup = pArr[hh];
                        if (gArr != null && gArr[hh] > maxOpen) maxOpen = gArr[hh];
                    }
                }

                cy = DrawGrid(ix, cy, iw, lh, cellH, mLocal, days, dayRows, pickupByDay, Loc.G("metric_pickup"), maxPickup, 0, null);
                cy += lh * 0.4f;
                cy = DrawGrid(ix, cy, iw, lh, cellH, mLocal, days, dayRows, goodByDay, Loc.G("metric_openlog"), maxOpen, 1, openEvByCell);

                // ---- separator + clear-time trend chart ----
                if (hasChart)
                {
                    DrawRect(ix, cy + lh * 0.3f, iw, 1, new Color(1, 1, 1, 0.12f));
                    cy += lh * 0.6f;
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#9fb4cc>{Loc.G("trend")} · {_trendStage}</color>", _dim);
                    cy += lh;
                    TrendChart.Draw(new Rect(ix, cy, iw, ChartH), _rect.x, _trend, -1, -1, _white, _tiny, null, null, "s");
                    cy += ChartH + lh;
                }

                // ---- hover tooltip (drawn last, on top); may be multi-line (split on \n) ----
                if (_hasTip)
                {
                    int lineCount = 1; for (int i = 0; i < _tipText.Length; i++) if (_tipText[i] == '\n') lineCount++;
                    bool multi = lineCount > 1;
                    float tlh = fs + 4f;
                    float th = multi ? lineCount * tlh + 6f : lh;
                    float tw = multi ? Mathf.Min(360f, iw) : _tip.CalcSize(new GUIContent(_tipText)).x + 12f;
                    float tx = Mathf.Clamp(_tipCell.x + _tipCell.width * 0.5f - tw * 0.5f, ix, ix + iw - tw);
                    float ty = _tipCell.y - th - 2f; if (ty < _rect.y) ty = _tipCell.y + _tipCell.height + 2f;
                    ty = Mathf.Clamp(ty, _rect.y, _rect.y + _rect.height - th);
                    DrawRect(tx, ty, tw, th, new Color(0.05f, 0.06f, 0.09f, 0.96f));
                    DrawRect(tx, ty, tw, 1, new Color(1, 1, 1, 0.18f));
                    if (multi)
                        GUI.Label(new Rect(tx + 6f, ty + 3f, tw - 8f, th - 6f), $"<color=#eaf3ee>{_tipText}</color>", _tipMulti);
                    else
                        GUI.Label(new Rect(tx, ty, tw, th), $"<color=#eaf3ee>{_tipText}</color>", _tip);
                }
            }
            catch { }
            finally { GUI.matrix = prevM; }
        }

        // Draws one labeled heatmap grid (header + day gutter + 24 hour columns + hour-axis ticks).
        // Returns the y just below the grid. Records a hover tooltip into _hasTip/_tipCell/_tipText.
        private float DrawGrid(float ix, float cy, float iw, float lh, float cellH, Vector2 mLocal,
            List<string> days, int dayRows, Dictionary<string, long[]> data, string header, long maxVisible, int metric,
            Dictionary<string, List<BoxOpenEvent>> evByCell)
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
                data.TryGetValue(day, out var arr);
                for (int hh = 0; hh < 24; hh++)
                {
                    long v = arr != null ? arr[hh] : 0;
                    var cr = new Rect(gridX + hh * cellW, gy, cellW - 1f, cellH - 1f);
                    DrawRect(cr.x, cr.y, cr.width, cr.height, RampColor(v / (float)maxVisible, metric));
                    if (cr.Contains(mLocal))
                    {
                        _hasTip = true; _tipCell = cr;
                        if (evByCell != null) _tipText = BuildOpenTip(evByCell, day, hh, shortDay);
                        else _tipText = $"{shortDay} {hh:00}:00 · {v}";
                    }
                }
                gy += cellH;
            }

            // hour axis (0,6,12,18)
            int[] ticks = { 0, 6, 12, 18 };
            foreach (int t in ticks)
                GUI.Label(new Rect(gridX + t * cellW, gy, cellW * 3, lh), $"<size=9><color=#7a8390>{t}</color></size>", _tiny);
            return gy + lh;
        }

        private static readonly string[] kindKeys = { "box_kind_normal", "box_kind_boss", "box_kind_actboss", "box_kind_unknown" };

        // Detailed multi-line tooltip for a bottom-grid (開箱紀錄) cell: header "MM-DD HH:00" then up to
        // 8 newest-first detail lines "HH:mm:ss   品質   箱種   物品", plus a "… +N more" overflow line.
        private static string BuildOpenTip(Dictionary<string, List<BoxOpenEvent>> evByCell, string day, int hh, string shortDay)
        {
            var sb = new StringBuilder();
            sb.Append(shortDay).Append(' ').Append(hh.ToString("00")).Append(":00");
            if (evByCell.TryGetValue(day + "|" + hh, out var lst) && lst != null && lst.Count > 0)
            {
                int n = lst.Count;
                int shown = n > 8 ? 8 : n;
                for (int i = 0; i < shown; i++)
                {
                    var e = lst[n - 1 - i];   // newest first
                    int k = (e.Kind >= 0 && e.Kind < 4) ? e.Kind : 3;
                    sb.Append('\n')
                      .Append(e.Time.ToString("HH:mm:ss")).Append("   ")
                      .Append("<color=").Append(BoxOpenOverlayBehaviour.GradeHex(e.Grade)).Append('>')
                      .Append(Loc.G("grade_" + BoxGrade.KeyOf(e.Grade))).Append("</color>   ")
                      .Append(Loc.G(kindKeys[k])).Append("   ")
                      .Append(ResolveItem(e.Name));
                }
                if (n > shown) sb.Append("\n… +").Append(n - shown).Append(" more");
            }
            return sb.ToString();
        }

        private static string ResolveItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int us = key.LastIndexOf('_');
            string digits = us >= 0 ? key.Substring(us + 1) : key;
            if (int.TryParse(digits, out int id)) { string nm = ItemNameStore.Get(id); if (!string.IsNullOrEmpty(nm)) return nm; }
            return key;
        }
    }
}

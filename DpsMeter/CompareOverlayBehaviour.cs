using System;
using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay (F11) that compares saved runs of the SAME stage against a baseline
    /// (fastest clear by default, or a manually pinned run). Two-column baseline | current view plus
    /// damage distribution, per-wave times, and gear/skill diffs. Read-only over RunStore data.</summary>
    public class CompareOverlayBehaviour : MonoBehaviour
    {
        public CompareOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 10f;

        private Rect _rect = new Rect(40, 40, 380, 0);
        private bool _visible;
        private float _opacity = 0.9f;
        private bool _placed;

        private Vector2 _dragOffset;
        private bool _dragging;

        private Texture2D _white, _bgTex;
        private float _bgAlphaBaked = -1f;
        private GUIStyle _title, _label, _dim, _tiny, _btn, _box, _col;
        private bool _stylesReady;

        private Rect _stagePrev, _stageNext, _runPrev, _runNext, _pinRect, _closeRect, _handleRect;

        private List<RunRecord> _runs = new List<RunRecord>();
        private List<string> _stages = new List<string>();
        private readonly Dictionary<string, string> _pinned = new Dictionary<string, string>();
        private int _stageIndex;
        private int _runIndex;
        private bool _loaded;

        void Awake()
        {
            _opacity = Mathf.Clamp01(Plugin.Opacity.Value + 0.15f);
            _rect.width = Mathf.Max(320, Plugin.ComparePanelWidth.Value);
            _visible = Plugin.CompareStartVisible.Value;
        }

        void Start() => PlaceDefault();

        private void PlaceDefault()
        {
            float px = Plugin.ComparePosX.Value, py = Plugin.ComparePosY.Value;
            if (px < 0 || py < 0) { _rect.x = Mathf.Max(24, (Screen.width - _rect.width) * 0.5f); _rect.y = 60f; }
            else { _rect.x = px; _rect.y = py; }
            _placed = true;
        }

        void Update()
        {
            try
            {
                InputCompat.Poll();
                if (InputCompat.KeyPressed(Plugin.CompareToggleKey))
                {
                    _visible = !_visible;
                    if (_visible) Reload();
                }
                if (_visible) HandlePointer();
                else if (_dragging) _dragging = false;
            }
            catch { }
        }

        private void Reload()
        {
            _runs = RunStore.LoadAll();
            var groups = StageCompare.GroupByStage(_runs);
            // order stages by the recency of their newest run (newest stage first)
            _stages = new List<string>(groups.Keys);
            _stages.Sort((a, b) => LastIndexOfStage(b).CompareTo(LastIndexOfStage(a)));
            _loaded = true;
            // default to the stage of the most recent run, newest run selected
            _stageIndex = 0;
            var g = CurrentGroup();
            _runIndex = g.Count - 1;
        }

        private int LastIndexOfStage(string stage)
        {
            for (int i = _runs.Count - 1; i >= 0; i--)
                if ((_runs[i].StageId ?? "") == stage) return i;
            return -1;
        }

        private List<RunRecord> CurrentGroup()
        {
            if (_stages.Count == 0) return new List<RunRecord>();
            _stageIndex = Mathf.Clamp(_stageIndex, 0, _stages.Count - 1);
            var g = new List<RunRecord>();
            string key = _stages[_stageIndex];
            foreach (var r in _runs) if ((r.StageId ?? "") == key) g.Add(r);
            return g;
        }

        private void HandlePointer()
        {
            Vector2 m = InputCompat.MouseGuiPos();
            if (InputCompat.MousePressed())
            {
                if (_closeRect.Contains(m)) { _visible = false; return; }
                if (_stagePrev.Contains(m)) { _stageIndex--; if (_stageIndex < 0) _stageIndex = Mathf.Max(0, _stages.Count - 1); _runIndex = CurrentGroup().Count - 1; return; }
                if (_stageNext.Contains(m)) { _stageIndex++; if (_stageIndex >= _stages.Count) _stageIndex = 0; _runIndex = CurrentGroup().Count - 1; return; }
                if (_runPrev.Contains(m)) { _runIndex = Mathf.Max(0, _runIndex - 1); return; }
                if (_runNext.Contains(m)) { _runIndex = Mathf.Min(CurrentGroup().Count - 1, _runIndex + 1); return; }
                if (_pinRect.Contains(m)) { TogglePin(); return; }
                if (_rect.Contains(m)) { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            }
            if (_dragging)
            {
                if (InputCompat.MouseHeld()) { _rect.x = m.x - _dragOffset.x; _rect.y = m.y - _dragOffset.y; }
                if (InputCompat.MouseReleased())
                {
                    _dragging = false;
                    Plugin.ComparePosX.Value = _rect.x;
                    Plugin.ComparePosY.Value = _rect.y;
                }
            }
        }

        private void TogglePin()
        {
            var g = CurrentGroup();
            if (g.Count == 0) return;
            _runIndex = Mathf.Clamp(_runIndex, 0, g.Count - 1);
            string stage = _stages[_stageIndex];
            string cur = g[_runIndex].Title;
            if (_pinned.TryGetValue(stage, out var p) && p == cur) _pinned.Remove(stage);
            else _pinned[stage] = cur;
        }

        // ---------------- rendering ----------------

        private void EnsureAssets()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_bgTex == null || !Mathf.Approximately(_bgAlphaBaked, _opacity))
            {
                if (_bgTex == null) _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.08f, _opacity));
                _bgTex.Apply();
                _bgAlphaBaked = _opacity;
                if (_box != null) _box.normal.background = _bgTex;
            }
            if (_stylesReady) return;
            int fs = Plugin.FontSize.Value;
            _title = new GUIStyle { fontSize = fs, fontStyle = FontStyle.Bold, richText = true };
            _title.normal.textColor = new Color(1f, 0.86f, 0.35f);
            _label = new GUIStyle { fontSize = fs, richText = true };
            _label.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            _dim = new GUIStyle { fontSize = fs - 2, richText = true };
            _dim.normal.textColor = new Color(0.78f, 0.84f, 0.95f);
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4), richText = true };
            _tiny.normal.textColor = new Color(0.7f, 0.75f, 0.85f);
            _col = new GUIStyle { fontSize = fs, richText = true };
            _col.normal.textColor = Color.white;
            _btn = new GUIStyle(GUI.skin.button) { fontSize = fs - 2, fontStyle = FontStyle.Bold };
            _box = new GUIStyle(); _box.normal.background = _bgTex;
            _stylesReady = true;
        }

        private void DrawRect(float x, float y, float w, float h, Color c)
        {
            var prev = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, h), _white);
            GUI.color = prev;
        }

        void OnGUI()
        {
            if (!_visible) return;
            try
            {
                EnsureAssets();
                if (!_placed) PlaceDefault();
                if (!_loaded) Reload();

                int fs = Plugin.FontSize.Value;
                float lh = fs + 7;
                float x = _rect.x, w = _rect.width;
                float ix = x + Pad, iw = w - Pad * 2;

                var group = CurrentGroup();
                if (group.Count == 0) { DrawEmpty(ix, iw, lh, fs); return; }

                _runIndex = Mathf.Clamp(_runIndex, 0, group.Count - 1);
                string stage = _stages[_stageIndex];
                _pinned.TryGetValue(stage, out var pinTitle);
                var baseline = StageCompare.PickBaseline(group, pinTitle);
                var current = group[_runIndex];
                var cmp = StageCompare.Compare(baseline, current);
                bool curIsBase = ReferenceEquals(baseline, current);
                bool curPinned = pinTitle != null && current.Title == pinTitle;

                // ---- measure height ----
                int statRows = cmp.Stats.Count;
                int waveRows = Mathf.Min(cmp.Waves.Count, 8);
                int gearRows = cmp.Gear.Count;
                int skillRows = cmp.Skills.Count;
                float coreRows = 6f; // duration, active, idle, avg, peak, crit
                float h = Pad
                    + lh                               // header
                    + lh                               // stage nav
                    + lh                               // run nav + pin
                    + lh * coreRows                    // two-col core
                    + (statRows > 0 ? lh * 0.5f + lh * statRows : 0)
                    + lh + 14 + 14                     // dist header + 2 bars
                    + lh + lh * waveRows               // wave header + rows
                    + (gearRows > 0 ? lh + lh * gearRows : lh)
                    + (skillRows > 0 ? lh + lh * skillRows : 0)
                    + Pad;
                _rect.height = h;
                _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - _rect.width));
                _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - _rect.height));
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;
                _handleRect = new Rect(x, _rect.y, w, lh);

                // header: title + close
                string sid = string.IsNullOrEmpty(stage) ? Loc.G("uncategorized") : stage;
                GUI.Label(new Rect(ix, cy, iw - 28, lh), $"{Loc.G("compare_title")} <color=#7FB2FF>{sid}</color>", _title);
                _closeRect = new Rect(x + w - 26, cy - 2, 22, lh);
                GUI.Button(_closeRect, "✕", _btn);
                cy += lh;

                // stage nav
                _stagePrev = new Rect(ix, cy, 26, lh - 2);
                _stageNext = new Rect(ix + 30, cy, 26, lh - 2);
                GUI.Button(_stagePrev, "≪", _btn);
                GUI.Button(_stageNext, "≫", _btn);
                GUI.Label(new Rect(ix + 62, cy, iw - 62, lh), $"<size=11>{Loc.G("compare_title")} {_stageIndex + 1}/{_stages.Count}</size>", _dim);
                cy += lh;

                // run nav + pin
                _runPrev = new Rect(ix, cy, 26, lh - 2);
                _runNext = new Rect(ix + 30, cy, 26, lh - 2);
                GUI.Button(_runPrev, "◀", _btn);
                GUI.Button(_runNext, "▶", _btn);
                GUI.Label(new Rect(ix + 62, cy, iw - 130, lh), $"<size=11>{current.Title}  {_runIndex + 1}/{group.Count}</size>", _dim);
                _pinRect = new Rect(x + w - Pad - 64, cy - 1, 64, lh);
                GUI.Button(_pinRect, curPinned ? "📌" + Loc.G("pinned") : Loc.G("set_baseline"), _btn);
                cy += lh;

                // ---- two-column core ----
                float colW = iw / 2f;
                float lx = ix, rx = ix + colW;
                DrawRect(rx - 1, cy, 1, lh * coreRows, new Color(1, 1, 1, 0.10f));
                GUI.Label(new Rect(lx, cy, colW, lh), $"<size=11><color=#9fb4cc>📌 {Loc.G("baseline")}</color></size>", _dim);
                GUI.Label(new Rect(rx + 6, cy, colW, lh), curIsBase ? $"<size=11><color=#9fb4cc>{Loc.G("baseline")}</color></size>" : $"<size=11><color=#9fb4cc>{Loc.G("this_run")}</color></size>", _dim);
                cy += lh;

                cy = CoreRow(cy, colW, lx, rx, "total_time", cmp, "duration", false, true);
                cy = CoreRow(cy, colW, lx, rx, "active_time", cmp, "active", true, true);
                cy = CoreRow(cy, colW, lx, rx, "idle_time", cmp, "idle", false, true);
                cy = CoreRow(cy, colW, lx, rx, "avg", cmp, "avg", true, false);
                cy = CoreRow(cy, colW, lx, rx, "peak", cmp, "peak", true, false);
                cy = CoreRow(cy, colW, lx, rx, "crit", cmp, "crit", true, false, pct: true);

                // ---- stat changes ----
                if (statRows > 0)
                {
                    cy += lh * 0.5f;
                    foreach (var st in cmp.Stats)
                    {
                        bool better = st.Current >= st.Baseline;
                        string col = Mathf.Approximately((float)st.Delta, 0f) ? "#8a93a0" : (better ? "#5fd07c" : "#ef6a5a");
                        GUI.Label(new Rect(ix, cy, iw, lh),
                            $"<color=#aeb6c2>{Loc.G(st.Key)}</color>  {FmtStat(st.Baseline)} → <color={col}>{FmtStat(st.Current)}</color>", _label);
                        cy += lh;
                    }
                }

                // ---- damage distribution ----
                GUI.Label(new Rect(ix, cy, iw, lh), Loc.G("dmg_dist"), _dim); cy += lh;
                DrawDist(ix + 36, cy, iw - 36, 11, baseline);
                GUI.Label(new Rect(ix, cy - 1, 34, 12), $"<size=10>{Loc.G("baseline")}</size>", _tiny);
                cy += 14;
                DrawDist(ix + 36, cy, iw - 36, 11, current);
                GUI.Label(new Rect(ix, cy - 1, 34, 12), $"<size=10>{Loc.G("this_run")}</size>", _tiny);
                cy += 14;

                // ---- per-wave ----
                GUI.Label(new Rect(ix, cy, iw, lh), Loc.G("per_wave"), _dim); cy += lh;
                for (int i = 0; i < waveRows; i++)
                {
                    var wd = cmp.Waves[i];
                    string col = Mathf.Approximately(wd.Delta, 0f) ? "#8a93a0" : (wd.Delta < 0 ? "#5fd07c" : "#ef6a5a");
                    GUI.Label(new Rect(ix, cy, iw, lh),
                        $"<color=#aeb6c2>{Loc.G("wave_short")}{wd.Wave}</color>  {wd.Baseline:0.0}s → {wd.Current:0.0}s  <color={col}>{(wd.Delta >= 0 ? "+" : "")}{wd.Delta:0.0}</color>", _label);
                    cy += lh;
                }

                // ---- gear changes ----
                GUI.Label(new Rect(ix, cy, iw, lh), Loc.G("gear_changes"), _dim); cy += lh;
                if (cmp.Gear.Count == 0)
                {
                    // nothing
                }
                foreach (var gc in cmp.Gear) { DrawGearChange(ix, cy, iw, lh, gc); cy += lh; }

                // ---- skill changes ----
                if (cmp.Skills.Count > 0)
                {
                    GUI.Label(new Rect(ix, cy, iw, lh), Loc.G("skill_changes"), _dim); cy += lh;
                    foreach (var sc in cmp.Skills) { DrawSkillChange(ix, cy, iw, lh, sc); cy += lh; }
                }
            }
            catch { }
        }

        private float CoreRow(float cy, float colW, float lx, float rx, string labelKey, StageCompare.CompareResult cmp, string metricKey, bool higherBetter, bool seconds, bool pct = false)
        {
            int fs = Plugin.FontSize.Value; float lh = fs + 7;
            var md = cmp.Metrics.Find(mm => mm.Key == metricKey);
            string bv = seconds ? $"{md.Baseline:0.0}s" : (pct ? $"{md.Baseline * 100f:0.#}%" : Fmt(md.Baseline));
            string cvRaw = seconds ? $"{md.Current:0.0}s" : (pct ? $"{md.Current * 100f:0.#}%" : Fmt(md.Current));
            bool better = higherBetter ? md.Current >= md.Baseline : md.Current <= md.Baseline;
            string col = Mathf.Approximately((float)md.Delta, 0f) ? "#ffffff" : (better ? "#5fd07c" : "#ef6a5a");
            GUI.Label(new Rect(lx, cy, colW, lh), $"<color=#aeb6c2>{Loc.G(labelKey)}</color> {bv}", _label);
            GUI.Label(new Rect(rx + 6, cy, colW, lh), $"<color={col}>{cvRaw}</color>", _col);
            return cy + lh;
        }

        private void DrawEmpty(float ix, float iw, float lh, int fs)
        {
            float h = Pad + lh * 2 + Pad;
            _rect.height = h;
            GUI.Box(_rect, GUIContent.none, _box);
            float cy = _rect.y + Pad;
            GUI.Label(new Rect(ix, cy, iw - 28, lh), Loc.G("compare_title"), _title);
            _closeRect = new Rect(_rect.x + _rect.width - 26, cy - 2, 22, lh);
            GUI.Button(_closeRect, "✕", _btn);
            cy += lh;
            GUI.Label(new Rect(ix, cy, iw, lh), Loc.G("no_runs"), _dim);
            _handleRect = new Rect(_rect.x, _rect.y, _rect.width, lh);
        }

        private void DrawDist(float x, float y, float w, float h, RunRecord r)
        {
            DrawRect(x, y, w, h, new Color(0f, 0f, 0f, 1f));
            if (r == null || r.Total <= 0) return;
            var parts = new List<(int flag, float share)>();
            for (int i = 0; i < r.TypeFlags.Count && i < r.TypeAmounts.Count; i++)
                if (r.TypeAmounts[i] > 0) parts.Add((r.TypeFlags[i], (float)(r.TypeAmounts[i] / r.Total)));
            parts.Sort((a, b) => b.share.CompareTo(a.share));
            float cx = x;
            foreach (var p in parts) { float seg = p.share * w; DrawRect(cx, y, seg, h, ColorForFlag(p.flag)); cx += seg; }
        }

        private void DrawGearChange(float x, float y, float w, float lh, StageCompare.GearChange gc)
        {
            string s;
            switch (gc.Kind)
            {
                case StageCompare.ChangeKind.Added: s = $"<color=#5fd07c>＋ {GearStr(gc.Current)}</color>"; break;
                case StageCompare.ChangeKind.Removed: s = $"<color=#ef6a5a>− {GearStr(gc.Baseline)}</color>"; break;
                default: s = $"<color=#e7c25a>~ {GearStr(gc.Baseline)} → {GearStr(gc.Current)}</color>"; break;
            }
            GUI.Label(new Rect(x, y, w, lh), s, _tiny);
        }

        private void DrawSkillChange(float x, float y, float w, float lh, StageCompare.SkillChange sc)
        {
            string s;
            switch (sc.Kind)
            {
                case StageCompare.ChangeKind.Added: s = $"<color=#5fd07c>＋ {sc.Name} {Loc.G("lv")}{sc.CurrentLevel}</color>"; break;
                case StageCompare.ChangeKind.Removed: s = $"<color=#ef6a5a>− {sc.Name} {Loc.G("lv")}{sc.BaselineLevel}</color>"; break;
                default: s = $"<color=#e7c25a>~ {sc.Name} {Loc.G("lv")}{sc.BaselineLevel}→{sc.CurrentLevel}</color>"; break;
            }
            GUI.Label(new Rect(x, y, w, lh), s, _tiny);
        }

        private static string GearStr(GearItem g)
        {
            if (g == null) return "?";
            if (g.Affixes.Count == 0) return g.Name;
            var sb = new System.Text.StringBuilder(g.Name);
            sb.Append(" (");
            for (int i = 0; i < g.Affixes.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Loc.G(g.Affixes[i].Name)).Append('+').Append(g.Affixes[i].Value.ToString("0.##"));
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static string FmtStat(double v) => Math.Abs(v) < 100 ? v.ToString("0.##") : Fmt(v);

        private static string Fmt(double v)
        {
            if (v >= 1e9) return (v / 1e9).ToString("0.##") + "B";
            if (v >= 1e6) return (v / 1e6).ToString("0.##") + "M";
            if (v >= 1e3) return (v / 1e3).ToString("0.##") + "K";
            return v.ToString("0");
        }

        private static Color ColorForFlag(int flag)
        {
            switch (flag)
            {
                case 1: return new Color(0.93f, 0.32f, 0.26f);
                case 2: return new Color(0.30f, 0.68f, 0.96f);
                case 4: return new Color(0.74f, 0.46f, 0.97f);
                case 8: return new Color(0.40f, 0.86f, 0.46f);
                case 16: return new Color(0.97f, 0.46f, 0.86f);
                case 32: return new Color(0.97f, 0.83f, 0.32f);
            }
            float r = 0, g = 0, b = 0; int cnt = 0;
            int[] bits = { 1, 2, 4, 8, 16, 32 };
            foreach (var bit in bits) if ((flag & bit) != 0) { var c = ColorForFlag(bit); r += c.r; g += c.g; b += c.b; cnt++; }
            return cnt == 0 ? new Color(0.6f, 0.6f, 0.6f) : new Color(r / cnt, g / cnt, b / cnt);
        }
    }
}

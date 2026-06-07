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

        private Rect _stagePrev, _stageNext, _runPrev, _runNext, _pinRect, _closeRect, _handleRect, _resetAllRect;
        private bool _confirmReset;
        private readonly List<Rect> _charTabs = new List<Rect>();
        private int _charIndex;

        // chart points (clear-time trend): click a point to select that run for the detail below
        private readonly List<Rect> _pointRects = new List<Rect>();
        private readonly List<int> _pointRun = new List<int>();

        private List<RunRecord> _runs = new List<RunRecord>();
        private List<string> _stages = new List<string>();
        private readonly Dictionary<string, string> _pinned = new Dictionary<string, string>();
        private int _stageIndex;
        private int _runIndex;
        private bool _loaded;

        void Awake()
        {
            _opacity = Mathf.Clamp01(Plugin.Opacity.Value + 0.15f);
            _rect.width = Mathf.Max(520, Plugin.ComparePanelWidth.Value);   // two-column layout needs width
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
                if (_closeRect.Contains(m)) { _visible = false; _confirmReset = false; return; }
                if (_resetAllRect.Contains(m))
                {
                    if (_confirmReset) { RunStore.DeleteAll(); _confirmReset = false; _stageIndex = 0; _runIndex = 0; _charIndex = 0; Reload(); }
                    else _confirmReset = true;
                    return;
                }
                _confirmReset = false;   // any other click cancels the pending reset
                if (_stagePrev.Contains(m)) { _stageIndex--; if (_stageIndex < 0) _stageIndex = Mathf.Max(0, _stages.Count - 1); _runIndex = CurrentGroup().Count - 1; return; }
                if (_stageNext.Contains(m)) { _stageIndex++; if (_stageIndex >= _stages.Count) _stageIndex = 0; _runIndex = CurrentGroup().Count - 1; return; }
                // chart points are always shown on top: clicking one selects that run for the detail below
                for (int i = 0; i < _pointRects.Count; i++)
                    if (_pointRects[i].Contains(m)) { _runIndex = _pointRun[i]; return; }
                if (_runPrev.Contains(m)) { _runIndex = Mathf.Max(0, _runIndex - 1); return; }
                if (_runNext.Contains(m)) { _runIndex = Mathf.Min(CurrentGroup().Count - 1, _runIndex + 1); return; }
                if (_pinRect.Contains(m)) { TogglePin(); return; }
                for (int i = 0; i < _charTabs.Count; i++)
                    if (_charTabs[i].Contains(m)) { _charIndex = i; return; }
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

        /// <summary>Localize the difficulty suffix of a stage id ("2-2 HELL" -> "2-2 地獄").</summary>
        private static string LocalizeStage(string stage)
        {
            if (string.IsNullOrEmpty(stage)) return Loc.G("uncategorized");
            int sp = stage.LastIndexOf(' ');
            if (sp > 0)
            {
                string diff = stage.Substring(sp + 1);
                string loc = Loc.G(diff);
                if (loc != diff) return stage.Substring(0, sp) + " " + loc;
            }
            return stage;
        }

        /// <summary>Display name for a character id, looked up from either run's party.</summary>
        private static string CharName(RunRecord baseline, RunRecord current, string id)
        {
            // prefer the current run's (newer) name so a fresh localized capture wins over an
            // older run that may still hold English/legacy names
            foreach (var r in new[] { current, baseline })
                if (r != null)
                    foreach (var s in r.Party)
                        if (s != null && s.Character == id && !string.IsNullOrEmpty(s.CharacterName))
                            return s.CharacterName;
            return string.IsNullOrEmpty(id) ? "?" : id;
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
                _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f));   // solid black, no transparency
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
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4), richText = true, wordWrap = true };
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
            GUI.depth = -10;   // lower depth renders on top of the F9/F10 panels
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
                var chars = StageCompare.PartyCharacters(baseline, current);
                if (chars.Count == 0) chars.Add("");
                _charIndex = Mathf.Clamp(_charIndex, 0, chars.Count - 1);
                var cmp = StageCompare.Compare(baseline, current, chars[_charIndex]);
                bool curIsBase = ReferenceEquals(baseline, current);
                bool curPinned = pinTitle != null && current.Title == pinTitle;
                bool hasTabs = chars.Count > 1;

                // ---- measure ----
                int statRows = cmp.Stats.Count;
                int waveRows = Mathf.Min(cmp.Waves.Count, 8);
                int gearRows = cmp.Gear.Count;
                int skillRows = cmp.Skills.Count;
                float coreRows = 6f;
                var dist = MergedShares(baseline, current);
                int distRows = Mathf.Min(dist.Count, 4);
                float chartH = 120f;
                float leftH = lh + lh * coreRows
                    + (statRows > 0 ? lh * 0.5f + lh * statRows : 0)
                    + lh + 14 + 14 + lh * distRows
                    + lh + lh * waveRows;
                int curSkills = cmp.CurrentSnap != null ? cmp.CurrentSnap.Skills.Count : 0;
                int curGear = cmp.CurrentSnap != null ? cmp.CurrentSnap.Equipment.Count : 0;
                int rmSkills = 0, rmGear = 0;
                foreach (var sc in cmp.Skills) if (sc.Kind == StageCompare.ChangeKind.Removed) rmSkills++;
                foreach (var gc in cmp.Gear) if (gc.Kind == StageCompare.ChangeKind.Removed) rmGear++;
                float rightH = lh + lh * Mathf.Max(curSkills + rmSkills, 1)
                    + lh * 0.4f + lh + lh * 1.4f * Mathf.Max(curGear + rmGear, 1);
                float detailH = Mathf.Max(leftH, rightH);
                float h = Pad + lh /*header*/ + lh /*stage nav*/ + chartH + 18 + lh /*run nav*/ + (hasTabs ? lh : 0) + detailH + Pad;
                _rect.height = h;
                // keep the WHOLE panel on-screen — Unity clips anything past the game window,
                // so letting it go off-edge would just truncate it.
                _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - _rect.width));
                _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - _rect.height));
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;
                _handleRect = new Rect(x, _rect.y, w, lh);

                // header: title + reset-all + close
                string sid = LocalizeStage(stage);
                GUI.Label(new Rect(ix, cy, iw - 120, lh), $"{Loc.G("compare_title")} <color=#7FB2FF>{sid}</color>", _title);
                _resetAllRect = new Rect(x + w - 116, cy - 1, 86, lh);
                GUI.Button(_resetAllRect, _confirmReset ? Loc.G("confirm_reset") : Loc.G("reset_all"), _btn);
                _closeRect = new Rect(x + w - 26, cy - 2, 22, lh);
                GUI.Button(_closeRect, "✕", _btn);
                cy += lh;

                // stage nav + chart hint
                _stagePrev = new Rect(ix, cy, 26, lh - 2);
                _stageNext = new Rect(ix + 30, cy, 26, lh - 2);
                GUI.Button(_stagePrev, "≪", _btn);
                GUI.Button(_stageNext, "≫", _btn);
                GUI.Label(new Rect(ix + 62, cy, iw - 62, lh), $"<size=11>{_stageIndex + 1}/{_stages.Count}　{group.Count} {Loc.G("runs")}　<color=#FFC857>◆</color>{Loc.G("baseline")}　{Loc.G("chart_hint")}</size>", _dim);
                cy += lh;

                // ---- chart (always on top) ----
                DrawChartSection(group, baseline, ix, cy, iw, chartH);
                cy += chartH + 18;

                // run nav + pin
                _runPrev = new Rect(ix, cy, 26, lh - 2);
                _runNext = new Rect(ix + 30, cy, 26, lh - 2);
                GUI.Button(_runPrev, "◀", _btn);
                GUI.Button(_runNext, "▶", _btn);
                GUI.Label(new Rect(ix + 62, cy, iw - 200, lh), $"<size=11>{current.Title}  {_runIndex + 1}/{group.Count}</size>", _dim);
                _pinRect = new Rect(x + w - Pad - 64, cy - 1, 64, lh);
                GUI.Button(_pinRect, curPinned ? "📌" + Loc.G("pinned") : Loc.G("set_baseline"), _btn);
                cy += lh;

                // character tabs
                _charTabs.Clear();
                if (hasTabs)
                {
                    float tx = ix, tw = iw / Mathf.Min(chars.Count, 5);
                    for (int i = 0; i < chars.Count && i < 5; i++)
                    {
                        var tr = new Rect(tx, cy, tw - 2, lh - 1);
                        _charTabs.Add(tr);
                        string label = CharName(baseline, current, chars[i]);
                        bool sel = i == _charIndex;
                        GUI.Label(tr, sel ? $"<b><color=#FFC857>[{label}]</color></b>" : $"<color=#9fb4cc>{label}</color>", _dim);
                        tx += tw;
                    }
                    cy += lh;
                }

                // ---- two big columns: LEFT = numbers, RIGHT = gear/skill ----
                float gap = 12f;
                float leftColW = iw * 0.54f;
                float rightColX = ix + leftColW + gap;
                float rightColW = iw - leftColW - gap;
                DrawRect(rightColX - gap * 0.5f, cy, 1, detailH, new Color(1, 1, 1, 0.10f));

                // LEFT column
                float ly = cy, lx = ix;
                float subW = leftColW / 2f;
                float rxc = ix + subW;
                GUI.Label(new Rect(lx, ly, subW, lh), $"<size=11><color=#9fb4cc>📌 {Loc.G("baseline")}</color></size>", _dim);
                GUI.Label(new Rect(rxc + 4, ly, subW, lh), curIsBase ? $"<size=11><color=#9fb4cc>{Loc.G("baseline")}</color></size>" : $"<size=11><color=#9fb4cc>{Loc.G("this_run")}</color></size>", _dim);
                ly += lh;
                ly = CoreRow(ly, subW, lx, rxc, "total_time", cmp, "duration", false, true);
                ly = CoreRow(ly, subW, lx, rxc, "active_time", cmp, "active", true, true);
                ly = CoreRow(ly, subW, lx, rxc, "idle_time", cmp, "idle", false, true);
                ly = CoreRow(ly, subW, lx, rxc, "avg", cmp, "avg", true, false);
                ly = CoreRow(ly, subW, lx, rxc, "peak", cmp, "peak", true, false);
                ly = CoreRow(ly, subW, lx, rxc, "crit", cmp, "crit", true, false, pct: true);
                if (statRows > 0)
                {
                    ly += lh * 0.5f;
                    foreach (var st in cmp.Stats)
                    {
                        bool better = st.Current >= st.Baseline;
                        string col = Mathf.Approximately((float)st.Delta, 0f) ? "#8a93a0" : (better ? "#5fd07c" : "#ef6a5a");
                        GUI.Label(new Rect(lx, ly, leftColW, lh), $"<color=#aeb6c2>{Loc.G(st.Key)}</color> {FmtStat(st.Baseline)} → <color={col}>{FmtStat(st.Current)}</color>", _label);
                        ly += lh;
                    }
                }
                GUI.Label(new Rect(lx, ly, leftColW, lh), Loc.G("dmg_dist"), _dim); ly += lh;
                DrawDist(lx + 34, ly, leftColW - 34, 11, baseline);
                GUI.Label(new Rect(lx, ly - 1, 32, 12), $"<size=10>{Loc.G("baseline")}</size>", _tiny);
                ly += 14;
                DrawDist(lx + 34, ly, leftColW - 34, 11, current);
                GUI.Label(new Rect(lx, ly - 1, 32, 12), $"<size=10>{Loc.G("this_run")}</size>", _tiny);
                ly += 14;
                for (int i = 0; i < distRows; i++)
                {
                    var d = dist[i];
                    string col = Mathf.Abs(d.cur - d.bas) < 0.001f ? "#aeb6c2" : (d.cur > d.bas ? "#5fd07c" : "#ef6a5a");
                    string nm = Loc.Name(DpsTracker.DecodeName(d.flag));
                    GUI.Label(new Rect(lx, ly, leftColW, lh), $"<color=#{ColorHex(d.flag)}>■</color> {nm} {d.bas * 100f:0.#}% → <color={col}>{d.cur * 100f:0.#}%</color>", _label);
                    ly += lh;
                }
                GUI.Label(new Rect(lx, ly, leftColW, lh), Loc.G("per_wave"), _dim); ly += lh;
                for (int i = 0; i < waveRows; i++)
                {
                    var wd = cmp.Waves[i];
                    string col = Mathf.Approximately(wd.Delta, 0f) ? "#8a93a0" : (wd.Delta < 0 ? "#5fd07c" : "#ef6a5a");
                    GUI.Label(new Rect(lx, ly, leftColW, lh), $"<color=#aeb6c2>{Loc.G("wave_short")}{wd.Wave}</color> {wd.Baseline:0.0}→{wd.Current:0.0}s <color={col}>{(wd.Delta >= 0 ? "+" : "")}{wd.Delta:0.0}</color>", _label);
                    ly += lh;
                }

                // RIGHT column: FULL loadout of the selected character, with change markers vs baseline
                float ry = cy;
                ry = DrawLoadout(cmp, rightColX, ry, rightColW, lh);
            }
            catch { }
        }

        /// <summary>Dashboard: clear-time trend line for the stage. X = attempt (oldest→newest),
        /// Y = clear seconds. Click a point to open the detailed compare for that run.</summary>
        /// <summary>Draw the clear-time trend plot within [x,y,w,plotH]; registers clickable point rects.
        /// Highlights the baseline point (gold) and the currently-selected run (white ring).</summary>
        private void DrawChartSection(List<RunRecord> group, RunRecord baseline, float ix, float y, float iw, float plotH)
        {
            float x = _rect.x;
            float px = ix + 30, pw = iw - 36, py = y, ph = plotH;
            DrawRect(px, py, pw, ph, new Color(0f, 0f, 0f, 1f));
            DrawRect(px, py, pw, 1, new Color(1, 1, 1, 0.12f));
            DrawRect(px, py + ph - 1, pw, 1, new Color(1, 1, 1, 0.12f));

            _pointRects.Clear(); _pointRun.Clear();
            int n = group.Count;
            float maxDur = 1f, minDur = float.MaxValue;
            foreach (var r in group) { if (r.Duration > maxDur) maxDur = r.Duration; if (r.Duration > 0 && r.Duration < minDur) minDur = r.Duration; }
            if (minDur == float.MaxValue) minDur = 0f;
            float span = Mathf.Max(1f, maxDur - minDur);

            GUI.Label(new Rect(x + 2, py - 6, 30, 14), $"<size=9>{maxDur:0}s</size>", _tiny);
            GUI.Label(new Rect(x + 2, py + ph - 12, 30, 14), $"<size=9>{minDur:0}s</size>", _tiny);

            float dx = n > 1 ? pw / (n - 1) : 0f;
            Vector2 prev = Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                var r = group[i];
                float t = (r.Duration - minDur) / span;
                float ptx = n > 1 ? px + dx * i : px + pw * 0.5f;
                float pty = py + ph - 8 - t * (ph - 16);
                if (i > 0) DrawLine(prev, new Vector2(ptx, pty), new Color(0.45f, 0.7f, 1f, 0.9f));
                prev = new Vector2(ptx, pty);
                bool isBase = ReferenceEquals(r, baseline);
                bool isSel = i == _runIndex;
                if (isSel) DrawRect(ptx - 6, pty - 6, 12, 12, new Color(1, 1, 1, 0.9f));   // selected ring
                var col = isBase ? new Color(1f, 0.8f, 0.3f) : new Color(0.4f, 0.66f, 0.98f);
                float ds = isBase ? 9f : 7f;
                DrawRect(ptx - ds / 2, pty - ds / 2, ds, ds, col);
                _pointRects.Add(new Rect(ptx - 9, pty - 9, 18, 18)); _pointRun.Add(i);
            }
            GUI.Label(new Rect(px - 4, py + ph + 2, 40, 14), "<size=9>#1</size>", _tiny);
            if (n > 1) GUI.Label(new Rect(px + pw - 24, py + ph + 2, 30, 14), $"<size=9>#{n}</size>", _tiny);
        }

        private void DrawLine(Vector2 a, Vector2 b, Color c)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 3f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                DrawRect(p.x - 1, p.y - 1, 2, 2, c);
            }
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

        /// <summary>Merge baseline+current damage-type shares by flag, sorted by the larger share.</summary>
        private static List<(int flag, float bas, float cur)> MergedShares(RunRecord b, RunRecord c)
        {
            var bm = ShareMap(b); var cm = ShareMap(c);
            var keys = new HashSet<int>(); var order = new List<int>();
            foreach (var k in bm.Keys) if (keys.Add(k)) order.Add(k);
            foreach (var k in cm.Keys) if (keys.Add(k)) order.Add(k);
            var list = new List<(int, float, float)>();
            foreach (var k in order)
            {
                bm.TryGetValue(k, out var bv); cm.TryGetValue(k, out var cv);
                list.Add((k, bv, cv));
            }
            list.Sort((a, z) => Mathf.Max(z.Item2, z.Item3).CompareTo(Mathf.Max(a.Item2, a.Item3)));
            return list;
        }

        private static Dictionary<int, float> ShareMap(RunRecord r)
        {
            var m = new Dictionary<int, float>();
            if (r == null || r.Total <= 0) return m;
            for (int i = 0; i < r.TypeFlags.Count && i < r.TypeAmounts.Count; i++)
                if (r.TypeAmounts[i] > 0) m[r.TypeFlags[i]] = (float)(r.TypeAmounts[i] / r.Total);
            return m;
        }

        private static string ColorHex(int flag)
        {
            var c = ColorForFlag(flag);
            return $"{(int)(c.r * 255):X2}{(int)(c.g * 255):X2}{(int)(c.b * 255):X2}";
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

        /// <summary>Right column: the selected character's full skill + gear list, colouring entries
        /// that were added (green) or changed (yellow) vs the baseline, plus removed ones (red).</summary>
        private float DrawLoadout(StageCompare.CompareResult cmp, float x, float y, float w, float lh)
        {
            var cur = cmp.CurrentSnap;
            var bas = cmp.BaselineSnap;

            // ---- skills ----
            GUI.Label(new Rect(x, y, w, lh), Loc.G("skill_changes"), _dim); y += lh;
            var baseSkill = new Dictionary<string, SkillEntry>();
            if (bas != null) foreach (var s in bas.Skills) baseSkill[s.Key != 0 ? "k" + s.Key : s.Name] = s;
            if (cur == null || cur.Skills.Count == 0) { GUI.Label(new Rect(x, y, w, lh), "<color=#8a93a0>—</color>", _tiny); y += lh; }
            else foreach (var s in cur.Skills)
            {
                string id = s.Key != 0 ? "k" + s.Key : s.Name;
                bool inBase = baseSkill.TryGetValue(id, out var b);
                string txt, mark;
                if (!inBase) { mark = "<color=#5fd07c>＋</color>"; txt = $"{s.Name} {Loc.G("lv")}{s.Level}"; }
                else if (b.Level != s.Level) { mark = "<color=#e7c25a>~</color>"; txt = $"{s.Name} {Loc.G("lv")}<color=#e7c25a>{b.Level}→{s.Level}</color>"; }
                else { mark = "<color=#8a93a0>·</color>"; txt = $"{s.Name} {Loc.G("lv")}{s.Level}"; }
                GUI.Label(new Rect(x, y, w, lh), $"{mark} {txt}", _tiny); y += lh;
            }
            // removed skills
            if (cur != null) { var curIds = new HashSet<string>(); foreach (var s in cur.Skills) curIds.Add(s.Key != 0 ? "k" + s.Key : s.Name);
                if (bas != null) foreach (var s in bas.Skills) if (!curIds.Contains(s.Key != 0 ? "k" + s.Key : s.Name)) { GUI.Label(new Rect(x, y, w, lh), $"<color=#ef6a5a>− {s.Name} {Loc.G("lv")}{s.Level}</color>", _tiny); y += lh; } }

            y += lh * 0.4f;
            // ---- gear ----
            GUI.Label(new Rect(x, y, w, lh), Loc.G("gear_changes"), _dim); y += lh;
            var baseGear = new Dictionary<string, GearItem>();
            if (bas != null) foreach (var g in bas.Equipment) baseGear[string.IsNullOrEmpty(g.Slot) ? g.Name : g.Slot] = g;
            if (cur == null || cur.Equipment.Count == 0) { GUI.Label(new Rect(x, y, w, lh), "<color=#8a93a0>—</color>", _tiny); y += lh; }
            else foreach (var g in cur.Equipment)
            {
                string id = string.IsNullOrEmpty(g.Slot) ? g.Name : g.Slot;
                bool inBase = baseGear.TryGetValue(id, out var b);
                string mark = !inBase ? "<color=#5fd07c>＋</color>" : (!GearSame(b, g) ? "<color=#e7c25a>~</color>" : "<color=#8a93a0>·</color>");
                GUI.Label(new Rect(x, y, w, lh * 1.4f), $"{mark} {GearStr(g)}", _tiny); y += lh * 1.4f;
            }
            if (cur != null) { var curIds = new HashSet<string>(); foreach (var g in cur.Equipment) curIds.Add(string.IsNullOrEmpty(g.Slot) ? g.Name : g.Slot);
                if (bas != null) foreach (var g in bas.Equipment) if (!curIds.Contains(string.IsNullOrEmpty(g.Slot) ? g.Name : g.Slot)) { GUI.Label(new Rect(x, y, w, lh * 1.4f), $"<color=#ef6a5a>− {GearStr(g)}</color>", _tiny); y += lh * 1.4f; } }
            return y;
        }

        private static bool GearSame(GearItem a, GearItem b)
        {
            if (a == null || b == null) return false;
            if (a.Name != b.Name || a.Affixes.Count != b.Affixes.Count) return false;
            for (int i = 0; i < a.Affixes.Count; i++)
                if (a.Affixes[i].Name != b.Affixes[i].Name || Mathf.Abs((float)(a.Affixes[i].Value - b.Affixes[i].Value)) > 1e-4f) return false;
            return true;
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

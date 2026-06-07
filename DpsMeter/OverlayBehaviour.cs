using System;
using System.Collections.Generic;
using TaskbarHero;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>
    /// IMGUI overlay. Injected MonoBehaviour (note the IntPtr ctor required by
    /// Il2CppInterop). Shows headline numbers, a per-wave-tagged DPS curve with
    /// Y-axis scale and wave markers, and a colored damage-type distribution.
    /// Accumulates across a whole stage (resets only at the NONE stage boundary),
    /// auto-saves each finished stage, and lets you browse past runs with the arrows.
    /// </summary>
    public class OverlayBehaviour : MonoBehaviour
    {
        public OverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 8f;
        private const int GraphSeconds = 60;
        private const float SampleInterval = 0.5f;
        private const int GraphCapacity = (int)(GraphSeconds / SampleInterval);
        private const float HoldSeconds = 3f;   // freeze the meter after this much idle

        private float EffectiveNow(float now) => Mathf.Min(now, Plugin.Tracker.LastDamageTime + HoldSeconds);
        private bool IsFrozen(float now) => (now - Plugin.Tracker.LastDamageTime) > HoldSeconds;

        private Rect _rect = new Rect(24, 24, 320, 0);
        private bool _visible = true;
        private float _opacity = 0.78f;

        private Vector2 _dragOffset;
        private bool _dragging;
        private bool _placed;

        // clickable regions (set during OnGUI, hit-tested in Update via InputCompat)
        private Rect _resetRect, _prevRect, _nextRect, _handleRect;

        private Texture2D _white, _bgTex;
        private float _bgAlphaBaked = -1f;
        private GUIStyle _title, _big, _label, _dim, _tiny, _btn, _box;
        private bool _stylesReady;

        private readonly List<Sample> _history = new List<Sample>(GraphCapacity + 4);
        private float _nextSampleTime;
        private int _currentWave;

        // per-wave timing + active/idle accounting (stage-compare)
        private readonly List<float> _waveDurations = new List<float>();
        private float _waveStartTime = -1f;
        private float _activeSec, _idleSec;
        private float _prevDmgTime = -1f;

        // stage-boundary detection
        private StageManager _stage;
        private int _lastState = -999;
        private float _nextFindTime;
        private float _nextProbe;

        // review mode
        private List<RunRecord> _runs = new List<RunRecord>();
        private bool _runsLoaded;
        private int _reviewIndex = -1;   // -1 = live

        void Awake()
        {
            _opacity = Mathf.Clamp01(Plugin.Opacity.Value);
            _visible = Plugin.StartVisible.Value;
            _rect.width = Mathf.Max(280, Plugin.PanelWidth.Value);
        }

        void Start() => PlaceDefault();

        private void PlaceDefault()
        {
            float px = Plugin.PosX.Value, py = Plugin.PosY.Value;
            if (px < 0 || py < 0) { _rect.x = Mathf.Max(24, Screen.width - _rect.width - 24); _rect.y = Mathf.Max(24, Screen.height - 470f); } // bottom-right
            else { _rect.x = px; _rect.y = py; }
            _placed = true;
        }

        void Update()
        {
            try
            {
                PollStageState();

                InputCompat.Poll();
                if (InputCompat.TogglePressed()) _visible = !_visible;
                if (_visible)
                {
                    if (InputCompat.OpacityUpPressed()) SetOpacity(_opacity + 0.1f);
                    if (InputCompat.OpacityDownPressed()) SetOpacity(_opacity - 0.1f);
                    HandlePointer();
                }
                else if (_dragging) { _dragging = false; }

                if (Plugin.DebugDamage != null && Plugin.DebugDamage.Value)
                {
                    float ut = Time.unscaledTime;
                    if (ut >= _nextProbe)
                    {
                        _nextProbe = ut + 1f;
                        Plugin.Logger.LogInfo($"[input probe] {InputCompat.Probe()}  resetRect={_resetRect}");
                    }
                }

                // sample live DPS; freeze (stop growing) while idle/paused
                float now = Time.time;
                if (now >= _nextSampleTime)
                {
                    _nextSampleTime = now + SampleInterval;

                    // active vs idle: did any damage land since the previous tick?
                    // Idle deliberately includes "frozen" stretches — that downtime (running
                    // between packs) is exactly the "跑路" time we want; the _currentWave gate
                    // stops counting once the stage ends (NONE resets it to 0).
                    float dmgTime = Plugin.Tracker.LastDamageTime;
                    if (_currentWave >= 1 && _prevDmgTime >= 0f)
                    {
                        if (dmgTime > _prevDmgTime) _activeSec += SampleInterval;
                        else _idleSec += SampleInterval;
                    }
                    _prevDmgTime = dmgTime;

                    if (!IsFrozen(now))
                    {
                        float live = Plugin.Tracker.GetSnapshot(EffectiveNow(now)).LiveDps;
                        _history.Add(new Sample { Dps = live, Wave = _currentWave });
                        while (_history.Count > GraphCapacity) _history.RemoveAt(0);
                    }
                }
            }
            catch { }
        }

        // Pointer handling via polled input (IMGUI events don't fire under the new
        // Input System). Hit-tests the rects recorded by OnGUI.
        private void HandlePointer()
        {
            Vector2 m = InputCompat.MouseGuiPos();

            if (InputCompat.MousePressed())
            {
                if (_resetRect.Contains(m)) { ResetMeter(); return; }
                if (_prevRect.Contains(m)) { NavOlder(); return; }
                if (_nextRect.Contains(m)) { NavNewer(); return; }
                // drag from anywhere on the panel (buttons handled above)
                if (_rect.Contains(m)) { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            }

            if (_dragging)
            {
                if (Plugin.DebugDamage != null && Plugin.DebugDamage.Value)
                    Plugin.Logger.LogInfo($"[drag] m={m} rect=({_rect.x:0},{_rect.y:0}) {InputCompat.Probe()}");
                if (InputCompat.MouseHeld())
                {
                    _rect.x = m.x - _dragOffset.x;
                    _rect.y = m.y - _dragOffset.y;
                }
                if (InputCompat.MouseReleased())
                {
                    _dragging = false;
                    Plugin.PosX.Value = _rect.x;
                    Plugin.PosY.Value = _rect.y;
                }
            }
        }

        private void ResetMeter()
        {
            Plugin.Tracker.StartEncounter(Time.time);
            _history.Clear();
            _currentWave = 0;
            _reviewIndex = -1;
            _waveDurations.Clear();
            _waveStartTime = -1f;
            _activeSec = _idleSec = 0f;
            _prevDmgTime = -1f;
        }

        private void PollStageState()
        {
            if (_stage == null)
            {
                if (Time.time < _nextFindTime) return;
                _nextFindTime = Time.time + 1f;
                _stage = UnityEngine.Object.FindObjectOfType<StageManager>();
                if (_stage == null) return;
            }

            int state;
            try { state = (int)_stage.stageState; }
            catch { _stage = null; return; }

            if (state == _lastState) return;
            _lastState = state;

            // Within a stage the state cycles MONSTERSPAWN->BATTLE->REORGANIZATION per
            // wave; NONE appears only when a whole stage finishes / the next reloads.
            // So: accumulate across waves, save + reset only at the NONE boundary.
            var es = (EStageState)state;
            if (es == EStageState.NONE)
            {
                // close out the wave that was in progress before saving
                if (_currentWave >= 1 && _waveStartTime >= 0)
                {
                    float wd = Time.time - _waveStartTime;
                    if (wd > 0.05f) _waveDurations.Add(wd);
                }
                SaveCurrentRun();
                Plugin.Tracker.StartEncounter(Time.time);
                Plugin.TakenTracker.StartEncounter(Time.time);
                _history.Clear();
                _currentWave = 0;
                Plugin.CurrentWave = 0;
                _waveDurations.Clear();
                _waveStartTime = -1f;
                _activeSec = _idleSec = 0f;
                _prevDmgTime = -1f;
            }
            else if (es == EStageState.MONSTERSPAWN)
            {
                // close the previous wave's timing, then start the next
                if (_currentWave >= 1 && _waveStartTime >= 0)
                {
                    float wd = Time.time - _waveStartTime;
                    if (wd > 0.05f) _waveDurations.Add(wd);
                }
                _currentWave++;
                _waveStartTime = Time.time;
                Plugin.CurrentWave = _currentWave;
            }
        }

        private void SaveCurrentRun()
        {
            try
            {
                var s = Plugin.Tracker.GetSnapshot(Time.time);
                if (s.Hits <= 0 || s.Total <= 0) return;
                var r = new RunRecord
                {
                    Title = DateTime.Now.ToString("MM/dd HH:mm:ss"),
                    Total = s.Total,
                    Duration = s.DurationSeconds,
                    Peak = s.PeakDps,
                    Avg = s.AvgDps,
                    CritRate = s.CritRate,
                    CritShare = s.CritDamageShare,
                    Waves = _currentWave,
                    StageId = CharacterReader.CurrentStageId(),
                    ActiveSeconds = _activeSec,
                    IdleSeconds = _idleSec,
                };
                foreach (var p in s.ByType) { r.TypeFlags.Add(p.Flag); r.TypeAmounts.Add(p.Amount); }
                foreach (var smp in _history) r.Samples.Add(smp);
                r.WaveDurations.AddRange(_waveDurations);
                r.Party.AddRange(CharacterReader.CaptureParty());

                // fold in the damage-taken (defense) side of the same encounter
                var ts = Plugin.TakenTracker.GetSnapshot(Time.time);
                r.TakenTotal = ts.Total;
                r.TakenPeak = ts.PeakDtps;
                r.TakenAvg = ts.AvgDtps;
                r.TakenBiggestHit = ts.BiggestHit;
                r.TakenCritRate = ts.CritRate;
                r.TakenHits = ts.Hits;
                foreach (var p in ts.ByAttribute) { r.TakenAttrValues.Add(p.Key); r.TakenAttrAmounts.Add(p.Amount); }
                foreach (var p in ts.ByType) { r.TakenTypeFlags.Add(p.Key); r.TakenTypeAmounts.Add(p.Amount); }

                RunStore.Save(r);
                _runsLoaded = false;   // refresh list next time review opens
            }
            catch { }
        }

        private void SetOpacity(float v) { _opacity = Mathf.Clamp01(v); Plugin.Opacity.Value = _opacity; }

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
            _big = new GUIStyle { fontSize = fs + 9, fontStyle = FontStyle.Bold };
            _big.normal.textColor = Color.white;
            _label = new GUIStyle { fontSize = fs };
            _label.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            _dim = new GUIStyle { fontSize = fs - 1 };
            _dim.normal.textColor = new Color(0.78f, 0.84f, 0.95f);
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4) };
            _tiny.normal.textColor = new Color(0.7f, 0.75f, 0.85f);
            _btn = new GUIStyle(GUI.skin.button) { fontSize = fs - 2, fontStyle = FontStyle.Bold };
            _box = new GUIStyle();
            _box.normal.background = _bgTex;
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

                int fs = Plugin.FontSize.Value;
                float lh = fs + 7;
                float x = _rect.x, w = _rect.width;
                float ix = x + Pad, iw = w - Pad * 2;
                float now = Time.time;

                // ---- gather the view: live or a saved run ----
                bool reviewing = _reviewIndex >= 0;
                if (reviewing && !_runsLoaded) { _runs = RunStore.LoadAll(); _runsLoaded = true; if (_reviewIndex >= _runs.Count) _reviewIndex = _runs.Count - 1; }
                if (reviewing && (_runs.Count == 0)) { _reviewIndex = -1; reviewing = false; }

                string title; float headline, peak, avg, crit, critShare, dur; double total; int waves;
                List<DpsTracker.TypePart> parts; List<Sample> samples;

                if (reviewing)
                {
                    var r = _runs[_reviewIndex];
                    title = $"<color=#7FB2FF>{Loc.G("review")}</color> {_reviewIndex + 1}/{_runs.Count}  {r.Title}";
                    headline = r.Avg; peak = r.Peak; avg = r.Avg; crit = r.CritRate; critShare = r.CritShare;
                    dur = r.Duration; total = r.Total; waves = r.Waves;
                    parts = BuildParts(r.TypeFlags, r.TypeAmounts, r.Total);
                    samples = r.Samples;
                }
                else
                {
                    var s = Plugin.Tracker.GetSnapshot(EffectiveNow(now));
                    string dot = !IsFrozen(now) ? "<color=#7CFC7C>●</color>" : "<color=#FFC857>‖</color>";
                    title = $"{dot} {Loc.G("dps_title")}  <size=11>{Loc.G("wave_short")}{_currentWave}</size>";
                    headline = s.LiveDps; peak = s.PeakDps; avg = s.AvgDps; crit = s.CritRate; critShare = s.CritDamageShare;
                    dur = s.DurationSeconds; total = s.Total; waves = _currentWave;
                    parts = s.ByType; samples = _history;
                }

                // ---- layout / sizing ----
                float graphH = 64f;
                float barH = 16f;
                int legendRows = Mathf.CeilToInt(Mathf.Min(parts.Count, 6) / 2f);
                float height = Pad + lh /*header*/ + lh /*nav*/ + (fs + 12) /*big*/
                    + lh + lh /*peak/avg,total/dur*/ + lh /*crit*/
                    + 6 + graphH + 14 /*graph + x labels*/ + 6 + barH
                    + (legendRows > 0 ? lh * legendRows : 0) + Pad;
                _rect.height = height;
                // keep the panel on-screen (resolution can change between sessions,
                // which would otherwise leave a saved position off the bottom edge)
                _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - _rect.width));
                _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - _rect.height));
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;

                // header + Reset (clicks handled in Update via recorded rects)
                _handleRect = new Rect(x, _rect.y, w - 64, lh + Pad);
                GUI.Label(new Rect(ix, cy, iw - 56, lh), title, _title);
                _resetRect = new Rect(x + w - 56, cy - 1, 50, lh + 2);
                GUI.Button(_resetRect, Loc.G("reset"), _btn);
                cy += lh;

                // nav row: ◀  (live/review)  ▶
                _prevRect = new Rect(ix, cy, 30, lh);
                _nextRect = new Rect(ix + 34, cy, 30, lh);
                GUI.Button(_prevRect, "◀", _btn);
                GUI.Button(_nextRect, "▶", _btn);
                GUI.Label(new Rect(ix + 70, cy, iw - 70, lh), reviewing ? Loc.G("review_hint") : Loc.G("live_hint"), _dim);
                cy += lh;

                // headline
                GUI.Label(new Rect(ix, cy, iw, fs + 12), Fmt(headline) + (reviewing ? "  <size=11>" + Loc.G("review_tag") + "</size>" : ""), _big);
                cy += fs + 12;

                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("peak")} {Fmt(peak)}    {Loc.G("avg")} {Fmt(avg)}", _label); cy += lh;
                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("total_dealt")} {Fmt(total)}    {Loc.G("duration")} {dur:0.0}s    {waves}{Loc.G("wave_short")}", _label); cy += lh;
                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("crit")} {crit * 100f:0.#}%    {Loc.G("crit_share")} {critShare * 100f:0.#}%", _label); cy += lh;

                cy += 6;
                DrawGraph(ix, cy, iw, graphH, samples);
                cy += graphH + 14;

                cy += 0;
                DrawDistribution(ix, cy, iw, barH, parts, total, lh);
            }
            catch { }
        }

        private void NavOlder()
        {
            if (!_runsLoaded) { _runs = RunStore.LoadAll(); _runsLoaded = true; }
            if (_runs.Count == 0) return;
            if (_reviewIndex < 0) _reviewIndex = _runs.Count - 1;   // jump to newest saved
            else if (_reviewIndex > 0) _reviewIndex--;
        }

        private void NavNewer()
        {
            if (_reviewIndex < 0) return;
            _reviewIndex++;
            if (_reviewIndex >= _runs.Count) _reviewIndex = -1;     // back to live
        }

        private static List<DpsTracker.TypePart> BuildParts(List<int> flags, List<double> amts, double total)
        {
            var parts = new List<DpsTracker.TypePart>();
            if (total <= 0) return parts;
            for (int i = 0; i < flags.Count && i < amts.Count; i++)
            {
                if (amts[i] <= 0) continue;
                parts.Add(new DpsTracker.TypePart { Flag = flags[i], Name = DpsTracker.DecodeName(flags[i]), Amount = amts[i], Share = (float)(amts[i] / total) });
            }
            parts.Sort((a, b) => b.Amount.CompareTo(a.Amount));
            return parts;
        }

        private void DrawGraph(float x, float y, float w, float h, List<Sample> samples)
        {
            DrawRect(x, y, w, h, new Color(0f, 0f, 0f, 1f));
            DrawRect(x, y + h - 1, w, 1, new Color(1f, 1f, 1f, 0.25f));   // baseline

            int n = samples != null ? samples.Count : 0;
            float max = 1f;
            for (int i = 0; i < n; i++) if (samples[i].Dps > max) max = samples[i].Dps;

            // Y-axis gridlines + labels (max, half)
            DrawRect(x, y + h * 0.5f, w, 1, new Color(1f, 1f, 1f, 0.10f));
            GUI.Label(new Rect(x + 2, y - 1, 70, 14), Fmt(max), _tiny);
            GUI.Label(new Rect(x + 2, y + h * 0.5f - 1, 70, 14), Fmt(max * 0.5f), _tiny);

            if (n < 1) return;
            float colW = w / GraphCapacity;
            int lastWave = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                float val = samples[i].Dps;
                float t = Mathf.Clamp01(val / max);
                float bh = t * (h - 2);
                float cx = x + i * colW;
                var col = new Color(0.25f + 0.7f * t, 0.6f - 0.35f * t, 0.95f - 0.55f * t, 0.9f);
                DrawRect(cx, y + h - bh - 1, Mathf.Max(1f, colW), bh, col);

                // wave divider + number on X axis
                if (samples[i].Wave != lastWave)
                {
                    lastWave = samples[i].Wave;
                    if (i > 0) DrawRect(cx, y, 1, h, new Color(1f, 1f, 1f, 0.22f));
                    GUI.Label(new Rect(cx + 1, y + h + 1, 28, 12), lastWave.ToString(), _tiny);
                }
            }
        }

        private void DrawDistribution(float x, float y, float w, float h, List<DpsTracker.TypePart> parts, double total, float lh)
        {
            DrawRect(x, y, w, h, new Color(0f, 0f, 0f, 1f));
            if (parts == null || parts.Count == 0 || total <= 0) return;

            float cx = x;
            foreach (var p in parts) { float segW = p.Share * w; DrawRect(cx, y, segW, h, ColorForFlag(p.Flag)); cx += segW; }

            float ly = y + h + 3;
            float colW = w / 2f;
            int shown = 0, col = 0;
            foreach (var p in parts)
            {
                if (shown >= 6) break;
                float lx = x + col * colW;
                DrawRect(lx, ly + 3, 10, 10, ColorForFlag(p.Flag));
                GUI.Label(new Rect(lx + 14, ly, colW - 14, lh), $"{Loc.Name(p.Name)} {p.Share * 100f:0.#}%", _dim);
                shown++; col++;
                if (col >= 2) { col = 0; ly += lh; }
            }
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

        private static string Fmt(double v)
        {
            if (v >= 1e9) return (v / 1e9).ToString("0.##") + "B";
            if (v >= 1e6) return (v / 1e6).ToString("0.##") + "M";
            if (v >= 1e3) return (v / 1e3).ToString("0.##") + "K";
            return v.ToString("0");
        }
    }
}

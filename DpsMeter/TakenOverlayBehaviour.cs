using System;
using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>
    /// IMGUI overlay for damage the player's heroes TAKE. A live-only sibling of
    /// <see cref="OverlayBehaviour"/> (no saved-run review): headline DTPS, peak/avg,
    /// total/duration/biggest-hit, hits/incoming-crit, a DTPS curve, and two
    /// distribution bars (element attribute + damage type). Its own toggle key and
    /// draggable position. Wave numbers are read from the shared Plugin.CurrentWave.
    /// </summary>
    public class TakenOverlayBehaviour : MonoBehaviour
    {
        public TakenOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 8f;
        private const int GraphSeconds = 60;
        private const float SampleInterval = 0.5f;
        private const int GraphCapacity = (int)(GraphSeconds / SampleInterval);
        private const float HoldSeconds = 3f;

        private float EffectiveNow(float now) => Mathf.Min(now, Plugin.TakenTracker.LastDamageTime + HoldSeconds);
        private bool IsFrozen(float now) => (now - Plugin.TakenTracker.LastDamageTime) > HoldSeconds;

        private Rect _rect = new Rect(24, 24, 320, 0);
        private bool _visible = true;
        private float _opacity = 0.78f;

        private Vector2 _dragOffset;
        private bool _dragging;
        private bool _placed;

        private Rect _resetRect, _handleRect;

        private Texture2D _white, _bgTex;
        private float _bgAlphaBaked = -1f;
        private GUIStyle _title, _big, _label, _dim, _tiny, _btn, _box;
        private bool _stylesReady;

        private readonly List<Sample> _history = new List<Sample>(GraphCapacity + 4);
        private float _nextSampleTime;
        private int _lastSeenWave;

        void Awake()
        {
            _opacity = Mathf.Clamp01(Plugin.Opacity.Value);
            _visible = Plugin.TakenStartVisible.Value;
            _rect.width = Mathf.Max(280, Plugin.TakenPanelWidth.Value);
        }

        void Start() => PlaceDefault();

        private void PlaceDefault()
        {
            float px = Plugin.TakenPosX.Value, py = Plugin.TakenPosY.Value;
            // default: bottom-left (DPS panel defaults to bottom-right)
            if (px < 0 || py < 0) { _rect.x = 24; _rect.y = Mathf.Max(24, Screen.height - 470f); }
            else { _rect.x = px; _rect.y = py; }
            _placed = true;
        }

        void Update()
        {
            try
            {
                if (InputCompat.TogglePressed(Plugin.TakenToggleKey)) _visible = !_visible;
                if (_visible)
                {
                    // opacity shares the DPS panel's PageUp/PageDown control; just track the value
                    _opacity = Mathf.Clamp01(Plugin.Opacity.Value);
                    HandlePointer();
                }
                else if (_dragging) { _dragging = false; }

                // clear our graph history when the shared wave counter resets (new stage)
                int wave = Plugin.CurrentWave;
                if (wave < _lastSeenWave) _history.Clear();
                _lastSeenWave = wave;

                float now = Time.time;
                if (now >= _nextSampleTime)
                {
                    _nextSampleTime = now + SampleInterval;
                    if (!IsFrozen(now))
                    {
                        float live = Plugin.TakenTracker.GetSnapshot(EffectiveNow(now)).LiveDtps;
                        _history.Add(new Sample { Dps = live, Wave = wave });
                        while (_history.Count > GraphCapacity) _history.RemoveAt(0);
                    }
                }
            }
            catch { }
        }

        private void HandlePointer()
        {
            Vector2 m = InputCompat.MouseGuiPos();

            if (InputCompat.MousePressed())
            {
                if (_resetRect.Contains(m)) { ResetMeter(); return; }
                if (_rect.Contains(m)) { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            }

            if (_dragging)
            {
                if (InputCompat.MouseHeld())
                {
                    _rect.x = m.x - _dragOffset.x;
                    _rect.y = m.y - _dragOffset.y;
                }
                if (InputCompat.MouseReleased())
                {
                    _dragging = false;
                    Plugin.TakenPosX.Value = _rect.x;
                    Plugin.TakenPosY.Value = _rect.y;
                }
            }
        }

        private void ResetMeter()
        {
            Plugin.TakenTracker.StartEncounter(Time.time);
            _history.Clear();
        }

        // ---------------- rendering ----------------

        private void EnsureAssets()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_bgTex == null || !Mathf.Approximately(_bgAlphaBaked, _opacity))
            {
                if (_bgTex == null) _bgTex = new Texture2D(1, 1);
                _bgTex.SetPixel(0, 0, new Color(0.08f, 0.04f, 0.05f, _opacity));   // faint red-tinted bg
                _bgTex.Apply();
                _bgAlphaBaked = _opacity;
                if (_box != null) _box.normal.background = _bgTex;
            }
            if (_stylesReady) return;
            int fs = Plugin.FontSize.Value;
            _title = new GUIStyle { fontSize = fs, fontStyle = FontStyle.Bold, richText = true };
            _title.normal.textColor = new Color(1f, 0.55f, 0.45f);
            _big = new GUIStyle { fontSize = fs + 9, fontStyle = FontStyle.Bold };
            _big.normal.textColor = Color.white;
            _label = new GUIStyle { fontSize = fs };
            _label.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            _dim = new GUIStyle { fontSize = fs - 1 };
            _dim.normal.textColor = new Color(0.95f, 0.84f, 0.78f);
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4) };
            _tiny.normal.textColor = new Color(0.85f, 0.75f, 0.7f);
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

                var s = Plugin.TakenTracker.GetSnapshot(EffectiveNow(now));
                string dot = !IsFrozen(now) ? "<color=#FF6B5C>●</color>" : "<color=#FFC857>‖</color>";
                string title = $"{dot} {Loc.G("taken_title")}  <size=11>{Loc.G("wave_short")}{Plugin.CurrentWave}</size>";

                // ---- layout / sizing ----
                float graphH = 64f;
                float barH = 14f;
                int attrRows = Mathf.CeilToInt(Mathf.Min(s.ByAttribute.Count, 6) / 2f);
                // NOTE: incoming (monster) damage carries no EDamageType (always None),
                // so the type bar is meaningless for the taken panel — only the element
                // attribute breakdown (Physical/Fire/Cold/...) is shown.
                float height = Pad + lh /*header*/ + (fs + 12) /*big*/
                    + lh + lh + lh /*peak/avg, total/dur, biggest/hits/crit*/
                    + 6 + graphH + 14 /*graph + x labels*/
                    + 6 + 12 /*attr label*/ + barH + (attrRows > 0 ? lh * attrRows : 0)
                    + Pad;
                _rect.height = height;
                _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Screen.width - _rect.width));
                _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Screen.height - _rect.height));
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;

                // header + Reset
                _handleRect = new Rect(x, _rect.y, w - 64, lh + Pad);
                GUI.Label(new Rect(ix, cy, iw - 56, lh), title, _title);
                _resetRect = new Rect(x + w - 56, cy - 1, 50, lh + 2);
                GUI.Button(_resetRect, Loc.G("reset"), _btn);
                cy += lh;

                // headline DTPS
                GUI.Label(new Rect(ix, cy, iw, fs + 12), Fmt(s.LiveDtps) + "  <size=11>" + Loc.G("per_sec_taken") + "</size>", _big);
                cy += fs + 12;

                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("peak")} {Fmt(s.PeakDtps)}    {Loc.G("avg")} {Fmt(s.AvgDtps)}", _label); cy += lh;
                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("total_taken")} {Fmt(s.Total)}    {Loc.G("duration")} {s.DurationSeconds:0.0}s", _label); cy += lh;
                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("biggest_hit")} {Fmt(s.BiggestHit)}    {Loc.G("hits")} {s.Hits}    {Loc.G("incoming_crit")} {s.CritRate * 100f:0.#}%", _label); cy += lh;

                cy += 6;
                DrawGraph(ix, cy, iw, graphH, _history);
                cy += graphH + 14;

                // element attribute distribution (the meaningful breakdown for incoming damage)
                cy += 6;
                GUI.Label(new Rect(ix, cy, iw, 12), Loc.G("element_dist"), _tiny); cy += 12;
                cy = DrawDistribution(ix, cy, iw, barH, s.ByAttribute, s.Total, lh, isAttribute: true);
            }
            catch { }
        }

        private void DrawGraph(float x, float y, float w, float h, List<Sample> samples)
        {
            DrawRect(x, y, w, h, new Color(0f, 0f, 0f, 0.35f));
            DrawRect(x, y + h - 1, w, 1, new Color(1f, 1f, 1f, 0.25f));

            int n = samples != null ? samples.Count : 0;
            float max = 1f;
            for (int i = 0; i < n; i++) if (samples[i].Dps > max) max = samples[i].Dps;

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
                // red-hot gradient: hotter = more damage taken
                var col = new Color(0.95f, 0.55f - 0.35f * t, 0.45f - 0.35f * t, 0.9f);
                DrawRect(cx, y + h - bh - 1, Mathf.Max(1f, colW), bh, col);

                if (samples[i].Wave != lastWave)
                {
                    lastWave = samples[i].Wave;
                    if (i > 0) DrawRect(cx, y, 1, h, new Color(1f, 1f, 1f, 0.22f));
                    GUI.Label(new Rect(cx + 1, y + h + 1, 28, 12), lastWave.ToString(), _tiny);
                }
            }
        }

        /// <summary>Draws a stacked bar + a 2-column legend; returns the new cursor Y.</summary>
        private float DrawDistribution(float x, float y, float w, float h, List<DamageTakenTracker.Part> parts, double total, float lh, bool isAttribute)
        {
            DrawRect(x, y, w, h, new Color(0f, 0f, 0f, 0.35f));
            if (parts == null || parts.Count == 0 || total <= 0) return y + h;

            float cx = x;
            foreach (var p in parts) { float segW = p.Share * w; DrawRect(cx, y, segW, h, ColorFor(p.Key, isAttribute)); cx += segW; }

            float ly = y + h + 3;
            float colW = w / 2f;
            int shown = 0, col = 0;
            foreach (var p in parts)
            {
                if (shown >= 6) break;
                float lx = x + col * colW;
                DrawRect(lx, ly + 3, 10, 10, ColorFor(p.Key, isAttribute));
                string label = Loc.Name(p.Name);
                GUI.Label(new Rect(lx + 14, ly, colW - 14, lh), $"{label} {p.Share * 100f:0.#}%", _dim);
                shown++; col++;
                if (col >= 2) { col = 0; ly += lh; }
            }
            int rows = Mathf.CeilToInt(Mathf.Min(parts.Count, 6) / 2f);
            return y + h + (rows > 0 ? lh * rows : 0);
        }

        private static Color ColorFor(int key, bool isAttribute)
            => isAttribute ? ColorForAttribute(key) : ColorForType(key);

        private static Color ColorForAttribute(int value)
        {
            switch (value)
            {
                case 0: return new Color(0.70f, 0.72f, 0.78f); // Physical - grey
                case 1: return new Color(0.95f, 0.42f, 0.24f); // Fire - orange-red
                case 2: return new Color(0.35f, 0.78f, 0.95f); // Cold - cyan
                case 3: return new Color(0.97f, 0.86f, 0.30f); // Lightning - yellow
                case 4: return new Color(0.66f, 0.40f, 0.90f); // Chaos - purple
                case 5: return new Color(0.85f, 0.85f, 0.95f); // AllElement
                default: return new Color(0.55f, 0.55f, 0.55f); // None / unknown
            }
        }

        private static Color ColorForType(int flag)
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
            foreach (var bit in bits) if ((flag & bit) != 0) { var c = ColorForType(bit); r += c.r; g += c.g; b += c.b; cnt++; }
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

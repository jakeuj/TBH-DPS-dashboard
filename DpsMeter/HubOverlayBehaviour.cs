using System;
using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay (F1): the Control Center / 中控台. Lists every panel registered in
    /// <see cref="PanelRegistry"/> as a toggle button (lit when the panel is visible, dim when hidden)
    /// and shows a tiny live summary (DPS · session time · boxes opened). Flipping a button calls the
    /// owning overlay's Set delegate, so panels can be shown/hidden from one place. Drag/close/scale
    /// behave exactly like the other overlays; auto-hides while a game menu is open.</summary>
    public class HubOverlayBehaviour : MonoBehaviour
    {
        public HubOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 10f;
        private Rect _rect = new Rect(24, 80, 260, 0);
        private bool _visible, _placed;
        private float _wantX, _wantY;
        private Vector2 _dragOffset; private bool _dragging;

        private Texture2D _white, _bgTex;
        private GUIStyle _title, _label, _dim, _tiny, _btn, _box, _tagR;
        private bool _stylesReady;

        private Rect _closeRect, _handleRect;
        private readonly List<Rect> _panelRects = new List<Rect>();
        private float _scale = 1f;

        private Rect ScaledRect() => new Rect(_rect.x, _rect.y, _rect.width * _scale, _rect.height * _scale);

        void Awake()
        {
            _rect.width = Mathf.Max(220, Plugin.HubPanelWidth.Value);
            _visible = Plugin.HubStartVisible.Value;
        }
        void Start() => PlaceDefault();

        private void PlaceDefault()
        {
            float px = Plugin.HubPosX.Value, py = Plugin.HubPosY.Value;
            if (px < 0 || py < 0) { _rect.x = 24f; _rect.y = 80f; }
            else { _rect.x = px; _rect.y = py; }
            _wantX = _rect.x; _wantY = _rect.y; _placed = true;
        }

        void Update()
        {
            try
            {
                InputCompat.SetPanel(5, _visible && !GameUiState.MenuOpen(), ScaledRect());
                if (InputCompat.KeyPressed(Plugin.HubToggleKey)) _visible = !_visible;
                if (_visible) HandlePointer();
                else if (_dragging) _dragging = false;
            }
            catch { }
        }

        private void HandlePointer()
        {
            if (GameUiState.MenuOpen()) { if (_dragging) { _dragging = false; InputCompat.ReleaseDrag(5); } return; }
            Vector2 m = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);
            if (InputCompat.MousePressed())
            {
                if (_closeRect.Contains(m)) { _visible = false; return; }
                // panel toggle buttons (rebuilt in OnGUI, tested in registry order)
                var panels = PanelRegistry.Panels;
                int n = Mathf.Min(_panelRects.Count, panels.Count);
                for (int i = 0; i < n; i++)
                {
                    if (_panelRects[i].Contains(m))
                    {
                        try { var e = panels[i]; e.Set(!e.Get()); } catch { }
                        return;
                    }
                }
                if (_handleRect.Contains(m) && InputCompat.ClaimDrag(5)) { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            }
            if (_dragging)
            {
                if (!InputCompat.OwnsDrag(5)) { _dragging = false; return; }
                if (InputCompat.MouseHeld()) { _rect.x = m.x - _dragOffset.x; _rect.y = m.y - _dragOffset.y; }
                if (InputCompat.MouseReleased()) { _dragging = false; InputCompat.ReleaseDrag(5); _wantX = _rect.x; _wantY = _rect.y; Plugin.HubPosX.Value = _rect.x; Plugin.HubPosY.Value = _rect.y; }
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
            _tagR = new GUIStyle { fontSize = Mathf.Max(9, fs - 4), richText = true, alignment = TextAnchor.MiddleRight }; _tagR.normal.textColor = new Color(0.7f, 0.75f, 0.85f);
            _btn = new GUIStyle(GUI.skin.button) { fontSize = fs - 2, fontStyle = FontStyle.Bold, richText = true };
            _box = new GUIStyle(); _box.normal.background = _bgTex;
            _stylesReady = true;
        }

        private void DrawRect(float x, float y, float w, float h, Color c) { var p = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = p; }

        void OnGUI()
        {
            if (!_visible || GameUiState.MenuOpen()) return;
            GUI.depth = -8;
            var prevM = GUI.matrix;
            try
            {
                EnsureAssets();
                if (!_placed) PlaceDefault();
                int fs = Plugin.FontSize.Value; float lh = fs + 6;
                float x = _rect.x, ix = x + Pad, w = _rect.width, iw = w - Pad * 2;

                var panels = PanelRegistry.Panels;

                // height: title + summary + separator gap + one row per panel + padding
                float h = Pad + lh /*title*/ + lh /*summary*/ + lh * 0.4f /*separator*/ + lh * Mathf.Max(panels.Count, 1) + Pad;
                _rect.height = h;
                _scale = UiScale.Fit(_rect.width, _rect.height);
                if (!_dragging) { _rect.x = Mathf.Clamp(_wantX, 0f, Mathf.Max(0f, Screen.width - _rect.width * _scale)); _rect.y = Mathf.Clamp(_wantY, 0f, Mathf.Max(0f, Screen.height - _rect.height * _scale)); }
                x = _rect.x; ix = x + Pad;
                GUI.matrix = UiScale.Matrix(_rect.x, _rect.y, _scale);
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;
                // title bar (whole row is the drag handle)
                _handleRect = new Rect(x, _rect.y, w, lh);
                GUI.Label(new Rect(ix, cy, iw - 26, lh), Loc.G("hub_title"), _title);
                _closeRect = new Rect(x + w - 26, cy - 2, 22, lh); GUI.Button(_closeRect, "✕", _btn);
                cy += lh;

                // summary: DPS <val>   時長 <m:ss>   寶箱 <n>
                string dps = "—", dur = "—"; int boxes = 0;
                try
                {
                    if (Plugin.Tracker != null)
                    {
                        var snap = Plugin.Tracker.GetSnapshot(UnityEngine.Time.time);
                        dps = snap.LiveDps >= 1000f ? (snap.LiveDps / 1000f).ToString("0.#") + "k" : snap.LiveDps.ToString("0");
                        int secs = Mathf.Max(0, (int)snap.DurationSeconds);
                        dur = (secs / 60) + ":" + (secs % 60).ToString("00");
                    }
                }
                catch { }
                try { boxes = BoxTracker.Events.Count; } catch { }
                GUI.Label(new Rect(ix, cy, iw, lh),
                    $"<color=#aeb6c2>DPS <color=#eaf3ee>{dps}</color>　{Loc.G("duration")} <color=#eaf3ee>{dur}</color>　{Loc.G("boxes")} <color=#eaf3ee>{boxes}</color></color>", _label);
                cy += lh;

                // separator
                DrawRect(ix, cy + lh * 0.2f, iw, 1, new Color(1, 1, 1, 0.12f));
                cy += lh * 0.4f;

                // one full-width toggle button per registered panel
                _panelRects.Clear();
                for (int i = 0; i < panels.Count; i++)
                {
                    var e = panels[i];
                    bool on = false; string name; string tag;
                    try { on = e.Get(); } catch { }
                    try { name = e.Name(); } catch { name = e.Id ?? "?"; }
                    try { tag = e.Hotkey.ToString(); } catch { tag = ""; }

                    var r = new Rect(ix, cy, iw, lh - 2);
                    _panelRects.Add(r);
                    GUI.Button(r, GUIContent.none, _btn);
                    // status dot + name (bright when on, dim when off)
                    string dot = on ? "<color=#7fffa0>●</color>" : "<color=#5a6470>●</color>";
                    string nameCol = on ? "#eaf3ee" : "#8a93a0";
                    GUI.Label(new Rect(ix + 6, cy, iw - 6 - 56, lh - 2), $"{dot} <color={nameCol}>{name}</color>", _label);
                    GUI.Label(new Rect(ix + iw - 60, cy, 56, lh - 2), $"<size=11><color=#9fb4cc>{tag}</color></size>", _tagR);
                    cy += lh;
                }
                if (panels.Count == 0)
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#8a93a0>{Loc.G("no_runs")}</color>", _tiny);
            }
            catch { }
            finally { GUI.matrix = prevM; }
        }
    }
}

using System;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay: shows the Steam Community Market price of the item under the native in-game
    /// tooltip. The hovered item is read by HeroProbe.PollHoveredItem (HeroProbe.HoveredKey/Grade/IsGear);
    /// the price comes from PriceStore (our cron-built prices.json on jsDelivr). It also shows the 24h
    /// change (波動) from prevCents once the cron has a day of history. Normally non-interactive and shown
    /// only on hover; pressing the adjust key (default F4) pins it visible and draggable so the user can
    /// place it, with the position saved to config. Toggled on/off from the F1 control center.</summary>
    public class PricePeekBehaviour : MonoBehaviour
    {
        public PricePeekBehaviour(IntPtr ptr) : base(ptr) { }

        private const int Slot = 8;        // InputCompat panel slot (drag capture in adjust mode)
        private const float Pad = 8f;
        private const float Width = 240f;
        private Rect _rect = new Rect(0, 0, Width, 0);
        private Texture2D _white, _bgTex;
        private GUIStyle _title, _label, _dim, _tiny, _box;
        private bool _stylesReady;
        private float _scale = 1f;
        private bool _adjust;              // position-adjust (drag) mode
        private bool _dragging; private Vector2 _dragOffset;

        private Rect ScaledRect() => new Rect(_rect.x, _rect.y, _rect.width * _scale, _rect.height * _scale);
        private bool Enabled => Plugin.PricePeekEnabled == null || Plugin.PricePeekEnabled.Value;

        void Update()
        {
            try
            {
                if (Enabled) PriceStore.EnsureLoaded();
                if (InputCompat.KeyPressed(Plugin.PriceAdjustKey)) _adjust = !_adjust && Enabled;
                bool capture = _adjust && Enabled;
                InputCompat.SetPanel(Slot, capture, ScaledRect());
                if (capture) HandleDrag();
                else if (_dragging) { _dragging = false; InputCompat.ReleaseDrag(Slot); }
            }
            catch { }
        }

        private void HandleDrag()
        {
            Vector2 m = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);
            if (InputCompat.MousePressed() && _rect.Contains(m))
            { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            if (_dragging)
            {
                if (InputCompat.MouseHeld()) { _rect.x = m.x - _dragOffset.x; _rect.y = m.y - _dragOffset.y; }
                if (InputCompat.MouseReleased())
                { _dragging = false; Plugin.PricePosX.Value = _rect.x; Plugin.PricePosY.Value = _rect.y; }
            }
        }

        private void EnsureAssets()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_bgTex == null) { _bgTex = new Texture2D(1, 1); _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.92f)); _bgTex.Apply(); if (_box != null) _box.normal.background = _bgTex; }
            if (_stylesReady) return;
            int fs = Plugin.FontSize.Value;
            _title = new GUIStyle { fontSize = fs, fontStyle = FontStyle.Bold, richText = true }; _title.normal.textColor = new Color(1f, 0.86f, 0.35f);
            _label = new GUIStyle { fontSize = fs, richText = true }; _label.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            _dim = new GUIStyle { fontSize = fs - 2, richText = true }; _dim.normal.textColor = new Color(0.78f, 0.84f, 0.95f);
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4), richText = true }; _tiny.normal.textColor = new Color(0.62f, 0.68f, 0.78f);
            _box = new GUIStyle(); _box.normal.background = _bgTex;
            _stylesReady = true;
        }

        void OnGUI()
        {
            if (!Enabled) return;
            int key = HeroProbe.HoveredKey;
            if (key == 0 && !_adjust) return;   // hover-only unless in adjust mode
            GUI.depth = -20;
            var prevM = GUI.matrix;
            try
            {
                EnsureAssets();
                int fs = Plugin.FontSize.Value; float lh = fs + 6;

                string localized, steamName; bool haveQuote = false; int cents = 0, qty = 0, prevCents = -1;
                if (key != 0)
                {
                    localized = ItemNameStore.Get(key);
                    string en = ItemNameStore.GetEn(key);
                    if (string.IsNullOrEmpty(localized)) localized = en;
                    if (string.IsNullOrEmpty(localized)) localized = "#" + key;
                    steamName = (HeroProbe.HoveredIsGear && !string.IsNullOrEmpty(HeroProbe.HoveredGrade)) ? $"{en} ({HeroProbe.HoveredGrade}) A" : en;
                    haveQuote = PriceStore.TryGet(steamName, out cents, out qty, out prevCents);
                }
                else { localized = Loc.G("price_panel"); steamName = ""; }   // adjust-mode placeholder

                float h = Pad + lh * 3f + Pad;
                _scale = UiScale.User;
                _rect.width = Width; _rect.height = h;

                if (!_dragging)
                {
                    float ox, oy;
                    if (Plugin.PricePosX != null && Plugin.PricePosX.Value >= 0f && Plugin.PricePosY.Value >= 0f)
                    { ox = Plugin.PricePosX.Value; oy = Plugin.PricePosY.Value; }
                    else { ox = Screen.width * 0.285f; oy = Screen.height * 0.735f; }   // auto: under the tooltip
                    ox = Mathf.Clamp(ox, 0f, Mathf.Max(0f, Screen.width - _rect.width * _scale));
                    oy = Mathf.Clamp(oy, 0f, Mathf.Max(0f, Screen.height - _rect.height * _scale));
                    _rect.x = ox; _rect.y = oy;
                }

                float ix = _rect.x + Pad, iw = _rect.width - Pad * 2;
                GUI.matrix = UiScale.Matrix(_rect.x, _rect.y, _scale);
                GUI.Box(_rect, GUIContent.none, _box);
                if (_adjust) DrawBorder(_rect, new Color(1f, 0.86f, 0.35f, 0.9f));

                float cy = _rect.y + Pad;
                GUI.Label(new Rect(ix, cy, iw, lh), localized, _title); cy += lh;

                if (_adjust && key == 0)
                {
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#9fb4cc>{Loc.G("price_drag_hint")}</color>", _dim); cy += lh;
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#9aa3b0>{Plugin.PriceAdjustKey} {Loc.G("price_drag_done")}</color>", _tiny);
                }
                else if (PriceStore.State == PriceStore.St.Loading)
                    GUI.Label(new Rect(ix, cy, iw, lh), "<color=#9fb4cc>Steam 報價載入中…</color>", _dim);
                else if (PriceStore.State == PriceStore.St.Error)
                    GUI.Label(new Rect(ix, cy, iw, lh), "<color=#ef6a5a>報價讀取失敗</color>", _dim);
                else if (haveQuote)
                {
                    string trend = "";
                    if (prevCents > 0)
                    {
                        double pct = (cents - prevCents) * 100.0 / prevCents;
                        string col = pct > 0.05 ? "#5fd07c" : pct < -0.05 ? "#ef6a5a" : "#9aa3b0";
                        string arrow = pct > 0.05 ? "▲" : pct < -0.05 ? "▼" : "→";
                        trend = $"   <color={col}>{arrow}{Math.Abs(pct):0.#}%</color>";
                    }
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#5fd07c>Steam {PriceStore.Format(cents)}</color>{trend}", _label); cy += lh;
                    GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#9aa3b0>在售 {qty}{(prevCents > 0 ? "   <size=10>24h 波動</size>" : "")}</color>", _tiny);
                }
                else
                {
                    GUI.Label(new Rect(ix, cy, iw, lh), "<color=#8a93a0>查無 Steam 報價</color>", _dim); cy += lh;
                    if (!string.IsNullOrEmpty(steamName)) GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#5a626e>{steamName}</color>", _tiny);
                }
            }
            catch { }
            finally { GUI.matrix = prevM; }
        }

        private void DrawBorder(Rect r, Color c)
        {
            var p = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), _white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1, r.width, 1), _white);
            GUI.DrawTexture(new Rect(r.x, r.y, 1, r.height), _white);
            GUI.DrawTexture(new Rect(r.xMax - 1, r.y, 1, r.height), _white);
            GUI.color = p;
        }

        void Awake()
        {
            PanelRegistry.Register("price", 8, "$", () => Loc.G("price_panel"), KeyCode.None,
                () => Plugin.PricePeekEnabled == null || Plugin.PricePeekEnabled.Value,
                v => { if (Plugin.PricePeekEnabled != null) Plugin.PricePeekEnabled.Value = v; });
        }
    }
}

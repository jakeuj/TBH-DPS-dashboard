using System;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>Detects whether the game is showing a blocking menu / full-screen UI (inventory/hero,
    /// stash, portal, cube, rune, settings, shop, mailbox, popups…) so the overlays can auto-hide and
    /// let clicks pass through. Read-only over the game's UIManager; result cached per frame.</summary>
    public static class GameUiState
    {
        private static TaskbarHero.UIManager _ui;
        private static float _nextFind;
        private static int _cachedFrame = -1;
        private static bool _cachedOpen;
        private static float _nextDiag;
        private static System.Reflection.PropertyInfo _tabProp;   // the settable EMainTab property (current tab)
        private static bool _tabResolved;

        /// <summary>The current main-tab value as an int (0 = None = no menu). Resolves the EMainTab
        /// property by TYPE, not name, so obfuscation renames across game updates (bcka→bckb→…) don't
        /// break it. Returns 0 if it can't be found.</summary>
        private static int CurrentTab(object ui)
        {
            if (!_tabResolved)
            {
                _tabResolved = true;
                try
                {
                    foreach (var p in ui.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                        if (p.CanRead && p.CanWrite && p.PropertyType.Name == "EMainTab") { _tabProp = p; break; }
                    Plugin.Logger?.LogInfo("[ui] tab property = " + (_tabProp != null ? _tabProp.Name : "NOT FOUND"));
                }
                catch { }
            }
            if (_tabProp == null) return 0;
            try { return Convert.ToInt32(_tabProp.GetValue(ui)); } catch { return 0; }
        }

        private static object Ui()
        {
            if (_ui != null) return _ui;
            if (Time.time < _nextFind) return null;
            _nextFind = Time.time + 1f;
            try { _ui = UnityEngine.Object.FindObjectOfType<TaskbarHero.UIManager>(); }
            catch { _ui = null; }
            return _ui;
        }

        /// <summary>True if any blocking game menu/UI is currently visible. Cached per frame.</summary>
        public static bool MenuOpen()
        {
            int f = Time.frameCount;
            if (f == _cachedFrame) return _cachedOpen;
            _cachedFrame = f;
            _cachedOpen = Compute();
            return _cachedOpen;
        }

        private static bool Compute()
        {
            try
            {
                var ui = Ui();
                if (ui == null) return false;

                // main tab bar on a tab other than None (Hero/Stash/Portal/Cube) -> a full menu is open.
                // (GameObject.activeInHierarchy is NOT reliable — panels stay active and toggle via
                //  CanvasGroup; the EMainTab value is. Resolved by type so renames don't break it.)
                return CurrentTab(ui) != 0;
            }
            catch { return false; }
        }

        /// <summary>One-line diagnostic of the UI state (gated by the caller). Helps verify which signal
        /// fires for TAB / each menu during in-game testing.</summary>
        public static void Diag()
        {
            if (Time.time < _nextDiag) return;
            _nextDiag = Time.time + 1f;
            try
            {
                var ui = Ui();
                if (ui == null) { Plugin.Logger?.LogInfo("[ui] UIManager not found"); return; }
                var sb = new System.Text.StringBuilder("[ui] ");
                sb.Append("tab=").Append(CurrentTab(ui)).Append(' ');
                sb.Append("=> open=").Append(MenuOpen());
                Plugin.Logger?.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[ui] diag: " + e.Message); }
        }
    }
}

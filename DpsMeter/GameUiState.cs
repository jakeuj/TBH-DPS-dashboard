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
                // (GameObject.activeInHierarchy is NOT reliable here — panels stay active and toggle via
                //  CanvasGroup, so they all read "active" even when hidden. bcka==None is reliable.)
                try { var tab = Refl.Get(ui, "bcka"); if (tab != null && Convert.ToInt32(tab) != 0) return true; }
                catch { }

                // TODO: add the verified popup/settings flag (hbl/bcjz/bcjy) once Diag() confirms which
                // one tracks settings/shop/popups without false-positives during normal gameplay.
                return false;
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
                try { sb.Append("tab=").Append(Refl.Get(ui, "bcka")).Append(' '); } catch { }
                try { sb.Append("hbl=").Append(Refl.Get(ui, "hbl")).Append(' '); } catch { }
                try { sb.Append("bcjz=").Append(Refl.Get(ui, "bcjz")).Append(' '); } catch { }
                try { var l = Refl.Get(ui, "bcjy"); sb.Append("bcjy=").Append(l != null ? Refl.Get(l, "Count") : null).Append(' '); } catch { }
                try { var l = Refl.Get(ui, "bcjx"); sb.Append("bcjx=").Append(l != null ? Refl.Get(l, "Count") : null).Append(' '); } catch { }
                sb.Append("=> open=").Append(MenuOpen());
                Plugin.Logger?.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[ui] diag: " + e.Message); }
        }
    }
}

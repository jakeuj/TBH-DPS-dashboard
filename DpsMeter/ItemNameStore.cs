using System;
using System.Collections.Generic;

namespace TbhDpsMeter
{
    /// <summary>Localized item names, keyed by the in-game ItemKey, shipped from the wiki's items.json
    /// (trimmed to the supported languages). This is the STABLE source for gear names: the in-memory
    /// item-name lookup (tf.ipp) breaks on every game-update obfuscation pass, but ItemKey↔name is
    /// fixed data. Embedded in the DLL — offline, no extra files.</summary>
    internal static class ItemNameStore
    {
        // id (string) -> { langCode -> name }
        private static Dictionary<string, object> _map;

        private static Dictionary<string, object> Map()
        {
            if (_map != null) return _map;
            _map = new Dictionary<string, object>();
            try
            {
                var asm = typeof(ItemNameStore).Assembly;
                string name = null;
                foreach (var n in asm.GetManifestResourceNames())
                    if (n.EndsWith("item_names.json", StringComparison.OrdinalIgnoreCase)) { name = n; break; }
                if (name != null)
                    using (var s = asm.GetManifestResourceStream(name))
                    using (var r = new System.IO.StreamReader(s))
                        _map = Json.Obj(Json.Parse(r.ReadToEnd())) ?? new Dictionary<string, object>();
                Plugin.Logger?.LogInfo($"[items] loaded {_map.Count} item names from embedded data");
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ItemNameStore: " + e.Message); }
            return _map;
        }

        /// <summary>Localized name for an item key in the current game language; "" if unknown.</summary>
        public static string Get(int itemKey)
        {
            if (itemKey <= 0) return "";
            var names = Json.Obj(Json.Get(Map(), itemKey.ToString()));
            if (names == null) return "";
            string lang = Loc.WikiLangCode();
            string s = Json.Str(Json.Get(names, lang));
            if (!string.IsNullOrEmpty(s)) return s;
            return Json.Str(Json.Get(names, "en-US")) ?? "";   // fall back to English
        }
    }
}

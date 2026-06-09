using System;
using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>One toggleable overlay panel, as seen by the F1 control center.
    /// Get/Set read and write the owning overlay's private visibility flag via captured delegates,
    /// so the hub can flip a panel on/off without the overlays knowing about each other.</summary>
    public sealed class PanelEntry
    {
        public string Id;
        public int Order;
        public string Icon;         // single BMP glyph shown in the hub's icon row (emoji won't render in IMGUI)
        public Func<string> Name;   // localized; resolved each frame so language switches live
        public KeyCode Hotkey;      // display only (the overlay still owns the actual key)
        public Func<bool> Get;
        public Action<bool> Set;
    }

    /// <summary>Static registry the overlays populate in Awake() and the F1 hub reads from.
    /// New panels become controllable simply by calling Register — no changes to the hub.</summary>
    public static class PanelRegistry
    {
        public static readonly List<PanelEntry> Panels = new List<PanelEntry>();

        public static void Register(string id, int order, string icon, Func<string> name, KeyCode hotkey, Func<bool> get, Action<bool> set)
        {
            // Awake can run more than once (scene reloads / re-injection); replace in place by id.
            for (int i = 0; i < Panels.Count; i++)
            {
                if (Panels[i].Id == id)
                {
                    Panels[i].Order = order; Panels[i].Icon = icon; Panels[i].Name = name; Panels[i].Hotkey = hotkey;
                    Panels[i].Get = get; Panels[i].Set = set;
                    return;
                }
            }
            Panels.Add(new PanelEntry { Id = id, Order = order, Icon = icon, Name = name, Hotkey = hotkey, Get = get, Set = set });
            Panels.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}

using System;
using TaskbarHero;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>Reads the player's stage id, stats, equipped gear, and active skills from the live
    /// game objects (IL2CPP). Everything is wrapped in try/catch — any read failure degrades to
    /// empty data rather than crashing. Obfuscated member access is finalized via in-game verification.</summary>
    public static class CharacterReader
    {
        private static StageManager _stage;
        private static float _nextFind;

        private static StageManager Stage()
        {
            if (_stage != null) return _stage;
            if (Time.time < _nextFind) return null;
            _nextFind = Time.time + 1f;
            try { _stage = UnityEngine.Object.FindObjectOfType<StageManager>(); } catch { _stage = null; }
            return _stage;
        }

        /// <summary>Stable stage identity (e.g. "3-6"). Empty string if it cannot be read.</summary>
        public static string CurrentStageId()
        {
            try { return StageProbe.ReadStageId(Stage()); }
            catch (Exception e) { Plugin.Logger?.LogWarning("CurrentStageId: " + e.Message); return ""; }
        }

        /// <summary>Capture every party member's loadout. One CharacterSnapshot per hero.</summary>
        public static System.Collections.Generic.List<CharacterSnapshot> CaptureParty()
        {
            var list = new System.Collections.Generic.List<CharacterSnapshot>();
            try
            {
                var heroes = HeroProbe.FindParty();
                var saveGear = SaveGearReader.ReadParty();   // gear from the decrypted save, keyed by heroKey
                bool diag = Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value;
                foreach (var hero in heroes)
                {
                    if (hero == null) continue;
                    var snap = CaptureOne(hero, diag);
                    diag = false;   // only dump diagnostics once
                    // attach gear from the save by heroKey (snap.Character is the HeroKey string)
                    if (int.TryParse(snap.Character, out int hk) && saveGear.TryGetValue(hk, out var gl))
                    {
                        foreach (var g in gl)
                        {
                            string nm = HeroProbe.ResolveItemName(g.ItemKey, g.Uid);   // -> localized name
                            if (!string.IsNullOrEmpty(nm)) g.Name = nm;
                            snap.Equipment.Add(g);
                        }
                    }
                    snap.Captured = snap.HasAny;
                    if (snap.Captured) list.Add(snap);
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("CharacterReader.CaptureParty: " + e.Message); }
            return list;
        }

        private static CharacterSnapshot CaptureOne(TaskbarHero.Hero hero, bool diag)
        {
            var snap = new CharacterSnapshot();
            try
            {
                if (diag) HeroProbe.Diagnose(hero);
                HeroProbe.ReadIdentity(hero, snap);
                HeroProbe.ReadStats(hero, snap);
                // gear comes from the decrypted save (see CaptureParty); ReadGear (ACTk) is unused
                HeroProbe.ReadSkills(hero, snap);
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("CharacterReader.CaptureOne: " + e.Message); }
            return snap;
        }
    }
}

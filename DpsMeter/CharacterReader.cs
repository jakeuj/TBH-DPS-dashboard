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
                bool diag = Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value;
                foreach (var hero in heroes)
                {
                    if (hero == null) continue;
                    var snap = CaptureOne(hero, diag);
                    diag = false;   // only dump diagnostics once
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
                snap.Character = HeroProbe.ReadCharacterId(hero);
                HeroProbe.ReadStats(hero, snap);
                HeroProbe.ReadGear(hero, snap);
                HeroProbe.ReadSkills(hero, snap);
                snap.Captured = snap.HasAny;
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("CharacterReader.CaptureOne: " + e.Message); }
            return snap;
        }
    }
}

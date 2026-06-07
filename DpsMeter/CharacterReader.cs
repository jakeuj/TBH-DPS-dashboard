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

        /// <summary>Capture the player's loadout. Returns a snapshot with Captured=false if nothing could be read.</summary>
        public static CharacterSnapshot Capture()
        {
            var snap = new CharacterSnapshot();
            try
            {
                var hero = HeroProbe.FindHero();
                if (hero == null) return snap;
                if (Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value) HeroProbe.Diagnose(hero);
                HeroProbe.ReadStats(hero, snap);
                HeroProbe.ReadGear(hero, snap);
                HeroProbe.ReadSkills(hero, snap);
                snap.Captured = snap.HasAny;
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("CharacterReader.Capture: " + e.Message); }
            return snap;
        }
    }
}

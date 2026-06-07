using System.Collections.Generic;

namespace TbhDpsMeter
{
    /// <summary>One graph sample: live DPS value tagged with the wave it belongs to.</summary>
    public struct Sample
    {
        public float Dps;
        public int Wave;
    }

    /// <summary>A single character stat at capture time (e.g. "attack" -> 1240).</summary>
    public struct StatEntry
    {
        public string Key;
        public double Value;
        public StatEntry(string key, double value) { Key = key; Value = value; }
    }

    /// <summary>One affix/mod on an equipped item (e.g. "Fire" -> 45).</summary>
    public struct Affix
    {
        public string Name;
        public double Value;
        public Affix(string name, double value) { Name = name; Value = value; }
    }

    /// <summary>One equipped item with its affixes/mods.</summary>
    public class GearItem
    {
        public string Slot = "";
        public string Name = "";
        public readonly List<Affix> Affixes = new List<Affix>();
    }

    /// <summary>One active skill with its level.</summary>
    public struct SkillEntry
    {
        public string Name;
        public int Level;
        public SkillEntry(string name, int level) { Name = name; Level = level; }
    }

    /// <summary>Character loadout snapshot taken when a stage run freezes.
    /// Any sub-list may be empty if that data could not be read (graceful degradation).</summary>
    public class CharacterSnapshot
    {
        /// <summary>True if a capture was attempted and at least some data was read.</summary>
        public bool Captured;
        /// <summary>Stable per-character identity (HeroKey) used to match across runs. "" = unknown.</summary>
        public string Character = "";
        /// <summary>Localized display name (class/hero name) for the character tab. Falls back to Character.</summary>
        public string CharacterName = "";
        public readonly List<StatEntry> Stats = new List<StatEntry>();
        public readonly List<GearItem> Equipment = new List<GearItem>();
        public readonly List<SkillEntry> Skills = new List<SkillEntry>();

        public bool HasAny => Stats.Count > 0 || Equipment.Count > 0 || Skills.Count > 0;
    }

    /// <summary>A finished stage's stats + DPS curve + character snapshot, persisted for later review/compare.</summary>
    public class RunRecord
    {
        public string Title = "";
        public double Total;
        public float Duration, Peak, Avg, CritRate, CritShare;
        public int Waves;
        public readonly List<int> TypeFlags = new List<int>();
        public readonly List<double> TypeAmounts = new List<double>();
        public readonly List<Sample> Samples = new List<Sample>();

        // --- damage-taken (defense) side of the same encounter ---
        public double TakenTotal;
        public float TakenPeak, TakenAvg, TakenBiggestHit, TakenCritRate;
        public long TakenHits;
        public readonly List<int> TakenAttrValues = new List<int>();
        public readonly List<double> TakenAttrAmounts = new List<double>();
        public readonly List<int> TakenTypeFlags = new List<int>();
        public readonly List<double> TakenTypeAmounts = new List<double>();

        // --- stage-compare additions (v2) ---
        /// <summary>Stable stage identity (e.g. "3-6") for grouping runs of the same stage.</summary>
        public string StageId = "";
        /// <summary>Per-wave durations in seconds (index 0 = wave 1).</summary>
        public readonly List<float> WaveDurations = new List<float>();
        /// <summary>Seconds spent actively dealing damage.</summary>
        public float ActiveSeconds;
        /// <summary>Seconds with no outgoing damage (moving / running between packs).</summary>
        public float IdleSeconds;
        /// <summary>Per-character loadout snapshots (whole party) at the moment this run finished.
        /// Empty for old records that had no snapshot.</summary>
        public readonly List<CharacterSnapshot> Party = new List<CharacterSnapshot>();
    }
}

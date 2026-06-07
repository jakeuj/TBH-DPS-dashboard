using System;
using System.Collections.Generic;

namespace TbhDpsMeter
{
    /// <summary>Pure (no Unity / no BepInEx) logic for grouping stage runs, picking a baseline,
    /// and diffing a run against the baseline. Unit-tested in TrackerTests.</summary>
    public static class StageCompare
    {
        // ---------- grouping ----------

        /// <summary>Group runs by StageId, preserving input order within each group.
        /// Runs with an empty StageId are grouped under "" (uncategorized).</summary>
        public static Dictionary<string, List<RunRecord>> GroupByStage(IEnumerable<RunRecord> runs)
        {
            var map = new Dictionary<string, List<RunRecord>>();
            if (runs == null) return map;
            foreach (var r in runs)
            {
                if (r == null) continue;
                string key = r.StageId ?? "";
                if (!map.TryGetValue(key, out var list)) { list = new List<RunRecord>(); map[key] = list; }
                list.Add(r);
            }
            return map;
        }

        /// <summary>Pick the baseline run of a group: the one whose Title matches pinnedTitle (if any),
        /// otherwise the fastest valid clear (smallest Duration with Total &gt; 0). Returns null for an empty group.</summary>
        public static RunRecord PickBaseline(List<RunRecord> group, string pinnedTitle = null)
        {
            if (group == null || group.Count == 0) return null;
            if (!string.IsNullOrEmpty(pinnedTitle))
                foreach (var r in group)
                    if (r != null && r.Title == pinnedTitle) return r;

            RunRecord best = null;
            foreach (var r in group)
            {
                if (r == null || r.Total <= 0 || r.Duration <= 0) continue;
                if (best == null || r.Duration < best.Duration) best = r;
            }
            return best ?? group[0];
        }

        // ---------- diff model ----------

        public enum ChangeKind { Added, Removed, Changed }

        public struct MetricDelta
        {
            public string Key;
            public double Baseline;
            public double Current;
            public double Delta => Current - Baseline;
            /// <summary>Signed percent change vs baseline (0 if baseline is 0).</summary>
            public double PercentDelta => Math.Abs(Baseline) > 1e-9 ? (Current - Baseline) / Baseline * 100.0 : 0.0;
        }

        public class WaveDelta { public int Wave; public float Baseline; public float Current; public float Delta => Current - Baseline; }

        public class GearChange
        {
            public ChangeKind Kind;
            public GearItem Baseline;   // null for Added
            public GearItem Current;    // null for Removed
            public string Key = "";     // slot or name used for matching
        }

        public class SkillChange
        {
            public ChangeKind Kind;
            public string Name = "";
            public int BaselineLevel;   // 0 for Added
            public int CurrentLevel;    // 0 for Removed
        }

        public class CompareResult
        {
            public RunRecord Baseline;
            public RunRecord Current;
            public bool IsBaseline;     // Current IS the baseline run
            public readonly List<MetricDelta> Metrics = new List<MetricDelta>();
            public readonly List<MetricDelta> Stats = new List<MetricDelta>();
            public readonly List<WaveDelta> Waves = new List<WaveDelta>();
            public readonly List<GearChange> Gear = new List<GearChange>();
            public readonly List<SkillChange> Skills = new List<SkillChange>();
        }

        // ---------- compare ----------

        public static CompareResult Compare(RunRecord baseline, RunRecord current)
        {
            var res = new CompareResult { Baseline = baseline, Current = current, IsBaseline = ReferenceEquals(baseline, current) };
            if (baseline == null || current == null) return res;

            void M(string key, double b, double c) => res.Metrics.Add(new MetricDelta { Key = key, Baseline = b, Current = c });
            M("duration", baseline.Duration, current.Duration);
            M("active", baseline.ActiveSeconds, current.ActiveSeconds);
            M("idle", baseline.IdleSeconds, current.IdleSeconds);
            M("avg", baseline.Avg, current.Avg);
            M("peak", baseline.Peak, current.Peak);
            M("crit", baseline.CritRate, current.CritRate);
            M("total", baseline.Total, current.Total);

            DiffStats(baseline.Snapshot, current.Snapshot, res.Stats);
            DiffWaves(baseline.WaveDurations, current.WaveDurations, res.Waves);
            DiffGear(baseline.Snapshot, current.Snapshot, res.Gear);
            DiffSkills(baseline.Snapshot, current.Snapshot, res.Skills);
            return res;
        }

        private static void DiffStats(CharacterSnapshot b, CharacterSnapshot c, List<MetricDelta> outList)
        {
            var bm = StatMap(b); var cm = StatMap(c);
            var keys = new List<string>();
            var seen = new HashSet<string>();
            if (b != null) foreach (var s in b.Stats) if (seen.Add(s.Key)) keys.Add(s.Key);
            if (c != null) foreach (var s in c.Stats) if (seen.Add(s.Key)) keys.Add(s.Key);
            foreach (var k in keys)
            {
                bm.TryGetValue(k, out var bv); cm.TryGetValue(k, out var cv);
                outList.Add(new MetricDelta { Key = k, Baseline = bv, Current = cv });
            }
        }

        private static Dictionary<string, double> StatMap(CharacterSnapshot s)
        {
            var m = new Dictionary<string, double>();
            if (s != null) foreach (var e in s.Stats) m[e.Key] = e.Value;
            return m;
        }

        private static void DiffWaves(List<float> b, List<float> c, List<WaveDelta> outList)
        {
            int n = Math.Max(b?.Count ?? 0, c?.Count ?? 0);
            for (int i = 0; i < n; i++)
            {
                float bv = (b != null && i < b.Count) ? b[i] : 0f;
                float cv = (c != null && i < c.Count) ? c[i] : 0f;
                outList.Add(new WaveDelta { Wave = i + 1, Baseline = bv, Current = cv });
            }
        }

        private static void DiffGear(CharacterSnapshot b, CharacterSnapshot c, List<GearChange> outList)
        {
            // match by slot when available, otherwise by name
            var bm = GearMap(b, out bool bBySlot);
            var cm = GearMap(c, out bool cBySlot);
            bool bySlot = bBySlot && cBySlot;
            if (!bySlot) { bm = GearMapByName(b); cm = GearMapByName(c); }

            var keys = new List<string>();
            var seen = new HashSet<string>();
            foreach (var k in bm.Keys) if (seen.Add(k)) keys.Add(k);
            foreach (var k in cm.Keys) if (seen.Add(k)) keys.Add(k);

            foreach (var k in keys)
            {
                bool hb = bm.TryGetValue(k, out var bg);
                bool hc = cm.TryGetValue(k, out var cg);
                if (hb && !hc) outList.Add(new GearChange { Kind = ChangeKind.Removed, Baseline = bg, Key = k });
                else if (!hb && hc) outList.Add(new GearChange { Kind = ChangeKind.Added, Current = cg, Key = k });
                else if (hb && hc && !GearEqual(bg, cg)) outList.Add(new GearChange { Kind = ChangeKind.Changed, Baseline = bg, Current = cg, Key = k });
            }
        }

        private static Dictionary<string, GearItem> GearMap(CharacterSnapshot s, out bool allHaveSlot)
        {
            var m = new Dictionary<string, GearItem>();
            allHaveSlot = true;
            if (s == null) { allHaveSlot = false; return m; }
            foreach (var g in s.Equipment)
            {
                if (string.IsNullOrEmpty(g.Slot)) { allHaveSlot = false; continue; }
                m[g.Slot] = g;
            }
            if (s.Equipment.Count == 0) allHaveSlot = false;
            return m;
        }

        private static Dictionary<string, GearItem> GearMapByName(CharacterSnapshot s)
        {
            var m = new Dictionary<string, GearItem>();
            if (s == null) return m;
            foreach (var g in s.Equipment) if (!string.IsNullOrEmpty(g.Name)) m[g.Name] = g;
            return m;
        }

        private static bool GearEqual(GearItem a, GearItem b)
        {
            if (a.Name != b.Name) return false;
            if (a.Affixes.Count != b.Affixes.Count) return false;
            // compare as sorted multisets so duplicate affix names / order don't cause false diffs
            var sa = new List<Affix>(a.Affixes); var sb = new List<Affix>(b.Affixes);
            Comparison<Affix> cmp = (x, y) =>
            {
                int c = string.CompareOrdinal(x.Name, y.Name);
                return c != 0 ? c : x.Value.CompareTo(y.Value);
            };
            sa.Sort(cmp); sb.Sort(cmp);
            for (int i = 0; i < sa.Count; i++)
                if (sa[i].Name != sb[i].Name || Math.Abs(sa[i].Value - sb[i].Value) > 1e-6) return false;
            return true;
        }

        private static void DiffSkills(CharacterSnapshot b, CharacterSnapshot c, List<SkillChange> outList)
        {
            var bm = SkillMap(b); var cm = SkillMap(c);
            var keys = new List<string>();
            var seen = new HashSet<string>();
            foreach (var k in bm.Keys) if (seen.Add(k)) keys.Add(k);
            foreach (var k in cm.Keys) if (seen.Add(k)) keys.Add(k);
            foreach (var k in keys)
            {
                bool hb = bm.TryGetValue(k, out var bl);
                bool hc = cm.TryGetValue(k, out var cl);
                if (hb && !hc) outList.Add(new SkillChange { Kind = ChangeKind.Removed, Name = k, BaselineLevel = bl });
                else if (!hb && hc) outList.Add(new SkillChange { Kind = ChangeKind.Added, Name = k, CurrentLevel = cl });
                else if (hb && hc && bl != cl) outList.Add(new SkillChange { Kind = ChangeKind.Changed, Name = k, BaselineLevel = bl, CurrentLevel = cl });
            }
        }

        private static Dictionary<string, int> SkillMap(CharacterSnapshot s)
        {
            var m = new Dictionary<string, int>();
            if (s != null) foreach (var e in s.Skills) m[e.Name] = e.Level;
            return m;
        }
    }
}

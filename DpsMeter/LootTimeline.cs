using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TbhDpsMeter
{
    /// <summary>Lifetime time-bucketed tally of box opens for the loot-heatmap panel (day × hour).
    /// It does NOT record opens itself — it passively observes <see cref="BoxOpenStats"/>.Log each
    /// frame and folds new events into its own buckets, so it survives the F4 log's 1000-entry cap
    /// and persists across sessions independently of F4. Pure data + simple TSV IO.</summary>
    public sealed class LootTimeline
    {
        /// <summary>Grade index (EGradeType) at/above which a drop counts as "good loot":
        /// 3 = legendary (傳說) and above.</summary>
        public const int GoodThreshold = 3;

        public static string Dir;
        private static string LogFile => string.IsNullOrEmpty(Dir) ? null : Path.Combine(Dir, "timeline.tsv");

        // key = "yyyy-MM-dd|HH" -> per-grade counts (length 10)
        private readonly Dictionary<string, long[]> _buckets = new Dictionary<string, long[]>();
        private int _lastVersion;
        private bool _dirty;
        private float _nextSave;

        private static string DayOf(DateTime t) => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        private static string Key(string day, int hour) => day + "|" + hour.ToString("00");

        private long[] Bucket(string day, int hour)
        {
            string k = Key(day, hour);
            if (!_buckets.TryGetValue(k, out var a)) { a = new long[10]; _buckets[k] = a; }
            return a;
        }

        /// <summary>Fold in any opens recorded since the last call. Version increases once per Add, and
        /// new events are always the tail of Log, so the last (Version-_lastVersion) entries are new.</summary>
        public void Observe(BoxOpenStats stats)
        {
            if (stats == null) return;
            int v = stats.Version;
            int delta = v - _lastVersion;
            _lastVersion = v;
            if (delta <= 0) return;
            int n = Math.Min(delta, stats.Log.Count);
            for (int i = stats.Log.Count - n; i < stats.Log.Count; i++)
            {
                var e = stats.Log[i];
                if (e == null) continue;
                int g = e.Grade; if (g < 0 || g >= 10) g = 0;
                Bucket(DayOf(e.Time), e.Time.Hour)[g]++;
            }
            _dirty = true;
        }

        // ---- queries (metric: 0 = opens, 1 = good loot) ----
        public long Opens(string day, int hour) => _buckets.TryGetValue(Key(day, hour), out var a) ? Sum(a, 0) : 0;
        public long GoodLoot(string day, int hour) => _buckets.TryGetValue(Key(day, hour), out var a) ? Sum(a, GoodThreshold) : 0;
        public long Value(string day, int hour, int metric) => metric == 1 ? GoodLoot(day, hour) : Opens(day, hour);

        public long DayTotal(string day, int metric)
        {
            long s = 0; for (int h = 0; h < 24; h++) s += Value(day, h, metric); return s;
        }

        public long Total(int metric)
        {
            long s = 0;
            foreach (var a in _buckets.Values) s += Sum(a, metric == 1 ? GoodThreshold : 0);
            return s;
        }

        /// <summary>Distinct days that have data, newest first.</summary>
        public List<string> Days()
        {
            var set = new HashSet<string>();
            foreach (var k in _buckets.Keys) { int bar = k.IndexOf('|'); if (bar > 0) set.Add(k.Substring(0, bar)); }
            var list = new List<string>(set);
            list.Sort(StringComparer.Ordinal);   // yyyy-MM-dd sorts chronologically
            list.Reverse();                       // newest first
            return list;
        }

        private static long Sum(long[] a, int from) { long s = 0; for (int i = from; i < a.Length; i++) s += a[i]; return s; }

        // ---- persistence: timeline.tsv, one line per (day,hour): day \t HH \t g0..g9 ----
        public void Load()
        {
            try
            {
                _buckets.Clear();
                var f = LogFile;
                if (string.IsNullOrEmpty(f) || !File.Exists(f)) return;
                foreach (var line in File.ReadAllLines(f))
                {
                    if (string.IsNullOrEmpty(line)) continue;
                    var p = line.Split('\t');
                    if (p.Length < 3) continue;
                    if (!int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour)) continue;
                    var a = new long[10];
                    int grades = Math.Min(10, p.Length - 2);
                    for (int g = 0; g < grades; g++) long.TryParse(p[2 + g], NumberStyles.Integer, CultureInfo.InvariantCulture, out a[g]);
                    _buckets[Key(p[0], hour)] = a;
                }
            }
            catch { }
        }

        /// <summary>Rewrite the file if anything changed, throttled to avoid per-frame IO.</summary>
        public void SaveIfDirty(float now)
        {
            if (!_dirty || now < _nextSave) return;
            _nextSave = now + 5f;
            _dirty = false;
            try
            {
                var f = LogFile;
                if (string.IsNullOrEmpty(f)) return;
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                foreach (var kv in _buckets)
                {
                    int bar = kv.Key.IndexOf('|');
                    if (bar <= 0) continue;
                    sb.Append(kv.Key.Substring(0, bar)).Append('\t').Append(kv.Key.Substring(bar + 1));
                    for (int g = 0; g < 10; g++) sb.Append('\t').Append(kv.Value[g]);
                    sb.Append('\n');
                }
                File.WriteAllText(f, sb.ToString());
            }
            catch { }
        }
    }
}

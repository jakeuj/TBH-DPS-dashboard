using System;
using System.Collections.Generic;
using System.Text;

namespace TbhDpsMeter
{
    /// <summary>Lifetime tally of opened-item grades per box kind, plus a capped time-ordered log.
    /// Pure (no IL2CPP) so it is unit-tested in TrackerTests.</summary>
    public sealed class BoxOpenStats
    {
        public const int KindCount = 4;     // Normal, Boss, ActBoss, Unknown
        public const int MaxLog = 1000;

        private readonly long[,] _counts = new long[KindCount, BoxGrade.Count];
        public readonly List<BoxOpenEvent> Log = new List<BoxOpenEvent>();
        public int Version;

        public long Count(int kind, int grade) => InRange(kind, grade) ? _counts[kind, grade] : 0;

        public long KindTotal(int kind)
        {
            if (kind < 0 || kind >= KindCount) return 0;
            long s = 0; for (int g = 0; g < BoxGrade.Count; g++) s += _counts[kind, g]; return s;
        }

        public long GradeTotal(int grade)
        {
            if (grade < 0 || grade >= BoxGrade.Count) return 0;
            long s = 0; for (int k = 0; k < KindCount; k++) s += _counts[k, grade]; return s;
        }

        public long Total() { long s = 0; foreach (var v in _counts) s += v; return s; }

        /// <summary>Percent of this kind's opens that were this grade (0..100).</summary>
        public double Percent(int kind, int grade)
        {
            long t = KindTotal(kind);
            return t > 0 ? 100.0 * Count(kind, grade) / t : 0.0;
        }

        public void Add(BoxOpenEvent e)
        {
            if (e == null) return;
            int k = (e.Kind >= 0 && e.Kind < KindCount) ? e.Kind : (int)BoxKind.Unknown;
            int g = (e.Grade >= 0 && e.Grade < BoxGrade.Count) ? e.Grade : 0;
            _counts[k, g]++;
            Log.Add(e);
            while (Log.Count > MaxLog) Log.RemoveAt(0);
            Version++;
        }

        public void Clear() { Array.Clear(_counts, 0, _counts.Length); Log.Clear(); Version++; }

        private static bool InRange(int k, int g) => k >= 0 && k < KindCount && g >= 0 && g < BoxGrade.Count;

        // ---- counts.txt: authoritative lifetime matrix, one "kind grade count" line per non-zero cell ----
        public string SerializeCounts()
        {
            var sb = new StringBuilder();
            for (int k = 0; k < KindCount; k++)
                for (int g = 0; g < BoxGrade.Count; g++)
                    if (_counts[k, g] > 0) sb.Append(k).Append(' ').Append(g).Append(' ').Append(_counts[k, g]).Append('\n');
            return sb.ToString();
        }

        public void LoadCounts(string text)
        {
            Array.Clear(_counts, 0, _counts.Length);
            if (string.IsNullOrEmpty(text)) return;
            foreach (var line in text.Split('\n'))
            {
                var p = line.Split(' ');
                if (p.Length != 3) continue;
                if (int.TryParse(p[0], out int k) && int.TryParse(p[1], out int g) && long.TryParse(p[2], out long c) && InRange(k, g))
                    _counts[k, g] = c;
            }
        }

        // ---- log lines: TSV "ticks \t kind \t grade \t stage \t name"; tabs/newlines stripped from fields ----
        public static string SerializeEvent(BoxOpenEvent e)
            => e.Time.Ticks + "\t" + e.Kind + "\t" + e.Grade + "\t" + San(e.Stage) + "\t" + San(e.Name);

        public static BoxOpenEvent ParseEvent(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var p = line.Split('\t');
            if (p.Length < 5) return null;
            if (!long.TryParse(p[0], out long ticks) || !int.TryParse(p[1], out int k) || !int.TryParse(p[2], out int g)) return null;
            return new BoxOpenEvent { Time = new DateTime(ticks), Kind = k, Grade = g, Stage = p[3], Name = p[4] };
        }

        private static string San(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}

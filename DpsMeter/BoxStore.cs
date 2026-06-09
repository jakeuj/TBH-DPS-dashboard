using System;
using System.Collections.Generic;
using System.IO;

namespace TbhDpsMeter
{
    /// <summary>Persists F5 box pickups as a TSV log under <see cref="Dir"/> (set by Plugin to
    /// BepInEx/config/dpsmeter_boxlog). Pure file IO so it is unit-tested with a temp dir.</summary>
    public static class BoxStore
    {
        public static string Dir;   // injected at runtime; tests set a temp path

        private static string LogFile => Path.Combine(Dir, "log.txt");

        public static void Append(BoxEvent e)
        {
            try
            {
                if (string.IsNullOrEmpty(Dir) || e == null) return;
                Directory.CreateDirectory(Dir);
                File.AppendAllText(LogFile, Serialize(e) + "\n");
            }
            catch { }
        }

        public static List<BoxEvent> LoadAll(int cap)
        {
            var list = new List<BoxEvent>();
            try
            {
                if (string.IsNullOrEmpty(Dir) || !File.Exists(LogFile)) return list;
                foreach (var line in File.ReadAllLines(LogFile))
                {
                    var e = Parse(line);
                    if (e != null) list.Add(e);
                }
                if (cap > 0 && list.Count > cap) list.RemoveRange(0, list.Count - cap);
            }
            catch { }
            return list;
        }

        public static void Clear()
        {
            try { if (!string.IsNullOrEmpty(Dir) && File.Exists(LogFile)) File.Delete(LogFile); } catch { }
        }

        public static string Serialize(BoxEvent e)
            => e.Time.Ticks + "\t" + San(e.Stage) + "\t" + e.Arg + "\t" + San(e.Type);

        public static BoxEvent Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;
            var p = line.Split('\t');
            if (p.Length < 4) return null;
            if (!long.TryParse(p[0], out long ticks) || !int.TryParse(p[2], out int arg)) return null;
            return new BoxEvent { Time = new DateTime(ticks), Stage = p[1], Arg = arg, Type = p[3] };
        }

        private static string San(string s)
            => string.IsNullOrEmpty(s) ? "" : s.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
    }
}

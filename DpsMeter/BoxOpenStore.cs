using System;
using System.IO;
using System.Text;

namespace TbhDpsMeter
{
    /// <summary>Persists F4 open-box stats under <see cref="Dir"/> (BepInEx/config/dpsmeter_boxopen):
    /// counts.txt is the authoritative lifetime matrix; log.txt mirrors the capped in-memory log.
    /// Pure file IO so it is unit-tested with a temp dir.</summary>
    public static class BoxOpenStore
    {
        public static string Dir;

        private static string CountsFile => Path.Combine(Dir, "counts.txt");
        private static string LogFile => Path.Combine(Dir, "log.txt");

        /// <summary>Fills counts from counts.txt and log entries from log.txt (counts are authoritative;
        /// the log is loaded directly, NOT via Add, so it never double-counts).</summary>
        public static void Load(BoxOpenStats stats)
        {
            try
            {
                if (string.IsNullOrEmpty(Dir) || stats == null) return;
                if (File.Exists(CountsFile)) stats.LoadCounts(File.ReadAllText(CountsFile));
                stats.Log.Clear();
                if (File.Exists(LogFile))
                {
                    foreach (var line in File.ReadAllLines(LogFile))
                    {
                        var e = BoxOpenStats.ParseEvent(line);
                        if (e != null) stats.Log.Add(e);
                    }
                    while (stats.Log.Count > BoxOpenStats.MaxLog) stats.Log.RemoveAt(0);
                }
            }
            catch { }
        }

        /// <summary>Writes both files from current state (counts full, log = current capped Log).</summary>
        public static void Save(BoxOpenStats stats)
        {
            try
            {
                if (string.IsNullOrEmpty(Dir) || stats == null) return;
                Directory.CreateDirectory(Dir);
                File.WriteAllText(CountsFile, stats.SerializeCounts());
                var sb = new StringBuilder();
                foreach (var e in stats.Log) sb.Append(BoxOpenStats.SerializeEvent(e)).Append('\n');
                File.WriteAllText(LogFile, sb.ToString());
            }
            catch { }
        }

        public static void Clear()
        {
            try
            {
                if (string.IsNullOrEmpty(Dir)) return;
                if (File.Exists(CountsFile)) File.Delete(CountsFile);
                if (File.Exists(LogFile)) File.Delete(LogFile);
            }
            catch { }
        }
    }
}

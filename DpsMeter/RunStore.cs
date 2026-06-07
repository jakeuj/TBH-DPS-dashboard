using System;
using System.Collections.Generic;
using System.IO;

namespace TbhDpsMeter
{
    /// <summary>Saves/loads run records as simple text files under BepInEx/config/dpsmeter_runs.
    /// Serialization lives in RunSerializer (pure C#, unit-tested); this class is just file I/O.</summary>
    public static class RunStore
    {
        private const int MaxRuns = 60;
        private static string Dir => Path.Combine(BepInEx.Paths.ConfigPath, "dpsmeter_runs");

        public static void Save(RunRecord r)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string file = Path.Combine(Dir, "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                File.WriteAllText(file, RunSerializer.Serialize(r));
                Prune();
            }
            catch (Exception e) { Plugin.Logger?.LogError("RunStore.Save: " + e.Message); }
        }

        private static void Prune()
        {
            try
            {
                var files = new List<string>(Directory.GetFiles(Dir, "run_*.txt"));
                files.Sort();
                while (files.Count > MaxRuns) { try { File.Delete(files[0]); } catch { } files.RemoveAt(0); }
            }
            catch { }
        }

        /// <summary>Returns runs oldest..newest.</summary>
        public static List<RunRecord> LoadAll()
        {
            var list = new List<RunRecord>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                var files = new List<string>(Directory.GetFiles(Dir, "run_*.txt"));
                files.Sort();
                foreach (var f in files)
                {
                    try { list.Add(RunSerializer.Deserialize(File.ReadAllLines(f))); }
                    catch (Exception e) { Plugin.Logger?.LogError("RunStore.Load one: " + e.Message); }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogError("RunStore.LoadAll: " + e.Message); }
            return list;
        }
    }
}

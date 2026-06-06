using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TbhDpsMeter
{
    /// <summary>One graph sample: live DPS value tagged with the wave it belongs to.</summary>
    public struct Sample
    {
        public float Dps;
        public int Wave;
    }

    /// <summary>A finished stage's stats + DPS curve, persisted to disk for later review.</summary>
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
    }

    /// <summary>Saves/loads run records as simple text files under BepInEx/config/dpsmeter_runs.</summary>
    public static class RunStore
    {
        private const int MaxRuns = 30;
        private static string Dir => Path.Combine(BepInEx.Paths.ConfigPath, "dpsmeter_runs");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Save(RunRecord r)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                string file = Path.Combine(Dir, "run_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".txt");
                var sb = new StringBuilder();
                sb.Append("title=").Append(r.Title).Append('\n');
                sb.Append("total=").Append(r.Total.ToString(Inv)).Append('\n');
                sb.Append("duration=").Append(r.Duration.ToString(Inv)).Append('\n');
                sb.Append("peak=").Append(r.Peak.ToString(Inv)).Append('\n');
                sb.Append("avg=").Append(r.Avg.ToString(Inv)).Append('\n');
                sb.Append("crit=").Append(r.CritRate.ToString(Inv)).Append('\n');
                sb.Append("critshare=").Append(r.CritShare.ToString(Inv)).Append('\n');
                sb.Append("waves=").Append(r.Waves).Append('\n');
                for (int i = 0; i < r.TypeFlags.Count; i++)
                    sb.Append("type=").Append(r.TypeFlags[i]).Append(':').Append(r.TypeAmounts[i].ToString(Inv)).Append('\n');
                var hs = new StringBuilder();
                foreach (var s in r.Samples)
                {
                    if (hs.Length > 0) hs.Append(',');
                    hs.Append(s.Dps.ToString("0.#", Inv)).Append(':').Append(s.Wave);
                }
                sb.Append("hist=").Append(hs).Append('\n');
                // damage-taken side
                sb.Append("taken_total=").Append(r.TakenTotal.ToString(Inv)).Append('\n');
                sb.Append("taken_peak=").Append(r.TakenPeak.ToString(Inv)).Append('\n');
                sb.Append("taken_avg=").Append(r.TakenAvg.ToString(Inv)).Append('\n');
                sb.Append("taken_biggest=").Append(r.TakenBiggestHit.ToString(Inv)).Append('\n');
                sb.Append("taken_crit=").Append(r.TakenCritRate.ToString(Inv)).Append('\n');
                sb.Append("taken_hits=").Append(r.TakenHits).Append('\n');
                for (int i = 0; i < r.TakenAttrValues.Count; i++)
                    sb.Append("taken_attr=").Append(r.TakenAttrValues[i]).Append(':').Append(r.TakenAttrAmounts[i].ToString(Inv)).Append('\n');
                for (int i = 0; i < r.TakenTypeFlags.Count; i++)
                    sb.Append("taken_type=").Append(r.TakenTypeFlags[i]).Append(':').Append(r.TakenTypeAmounts[i].ToString(Inv)).Append('\n');
                File.WriteAllText(file, sb.ToString());
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
                    var r = Parse(File.ReadAllLines(f));
                    if (r != null) list.Add(r);
                }
            }
            catch (Exception e) { Plugin.Logger?.LogError("RunStore.LoadAll: " + e.Message); }
            return list;
        }

        private static RunRecord Parse(string[] lines)
        {
            var r = new RunRecord();
            foreach (var line in lines)
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq);
                string v = line.Substring(eq + 1).TrimEnd('\r');
                switch (k)
                {
                    case "title": r.Title = v; break;
                    case "total": r.Total = D(v); break;
                    case "duration": r.Duration = F(v); break;
                    case "peak": r.Peak = F(v); break;
                    case "avg": r.Avg = F(v); break;
                    case "crit": r.CritRate = F(v); break;
                    case "critshare": r.CritShare = F(v); break;
                    case "waves": r.Waves = (int)D(v); break;
                    case "type":
                        int c = v.IndexOf(':');
                        if (c > 0) { r.TypeFlags.Add((int)D(v.Substring(0, c))); r.TypeAmounts.Add(D(v.Substring(c + 1))); }
                        break;
                    case "hist":
                        if (v.Length > 0)
                            foreach (var p in v.Split(','))
                            {
                                int cc = p.IndexOf(':');
                                if (cc > 0) r.Samples.Add(new Sample { Dps = F(p.Substring(0, cc)), Wave = (int)D(p.Substring(cc + 1)) });
                            }
                        break;
                    case "taken_total": r.TakenTotal = D(v); break;
                    case "taken_peak": r.TakenPeak = F(v); break;
                    case "taken_avg": r.TakenAvg = F(v); break;
                    case "taken_biggest": r.TakenBiggestHit = F(v); break;
                    case "taken_crit": r.TakenCritRate = F(v); break;
                    case "taken_hits": r.TakenHits = (long)D(v); break;
                    case "taken_attr":
                        int ca = v.IndexOf(':');
                        if (ca > 0) { r.TakenAttrValues.Add((int)D(v.Substring(0, ca))); r.TakenAttrAmounts.Add(D(v.Substring(ca + 1))); }
                        break;
                    case "taken_type":
                        int ct = v.IndexOf(':');
                        if (ct > 0) { r.TakenTypeFlags.Add((int)D(v.Substring(0, ct))); r.TakenTypeAmounts.Add(D(v.Substring(ct + 1))); }
                        break;
                }
            }
            return r;
        }

        private static float F(string s) { float.TryParse(s, NumberStyles.Any, Inv, out var f); return f; }
        private static double D(string s) { double.TryParse(s, NumberStyles.Any, Inv, out var d); return d; }
    }
}

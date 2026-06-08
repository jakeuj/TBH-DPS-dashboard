using System.Collections.Generic;

namespace TbhDpsMeter
{
    /// <summary>One ranked stage row: gold/sec and per-hero exp/sec, either measured from the
    /// player's own clears or estimated from the wiki baseline × the player's learned multiplier.</summary>
    public sealed class EfficiencyRow
    {
        public FarmStage Stage;
        public double GoldPerSec;
        public double ExpPerSec;   // per hero (already includes the exp-retention factor)
        public double ClearSec;    // measured median, or estimated from clear rate
        public bool Measured;      // true = from the player's runs; false = estimated
        public int Samples;        // number of trusted measured runs backing this row
        public double ExpRetention = 1;  // 0..1 exp kept at this stage's level for the player's level
    }

    /// <summary>The player's personal calibration learned from their clean runs:
    /// gold/exp multipliers vs the wiki baseline, and HP cleared per second.</summary>
    public sealed class Calibration
    {
        public double MGold = 1;     // measured_gold / wiki.expectedGold
        public double MExp = 1;      // measured_expPerHero / wiki.expectedEXP
        public double ClearRate;     // HP per second (totalHP / clear seconds) — fallback time model
        public bool HasData;         // false until at least one trusted run exists

        // Two-part clear-time model learned from runs: duration ≈ PerWaveSec·waves + SecPerHP·totalHP.
        // The per-wave term is the fixed overhead (spawn/travel) that stops trivial low-HP stages from
        // looking infinitely efficient; the per-HP term is your effective damage throughput.
        public double PerWaveSec;
        public double SecPerHP;
        public bool HasTimeModel;

        /// <summary>Representative party level used for the exp-retention model (latest run). 0 = unknown.</summary>
        public int HeroLevel;

        /// <summary>True when no run matches the current build, so calibration falls back to a previous
        /// build's runs — the player should clear a stage to re-calibrate.</summary>
        public bool Stale;

        /// <summary>How many trusted runs and distinct stages back the current calibration (for display).</summary>
        public int TrustedRuns;
        public int MeasuredStages;
    }

    public enum FarmSortKey { ExpPerSec, GoldPerSec, ClearSec }

    /// <summary>Pure-C# farming efficiency planner. Reconciles the wiki's static per-stage rewards
    /// with the player's measured runs: a constant personal multiplier (≈2.75× in observed data,
    /// identical for gold and per-hero exp) lets us predict every stage from a few clears.
    /// No Unity/BepInEx deps — fully unit-tested.</summary>
    public static class FarmPlanner
    {
        // A measured run is "mislabeled" (e.g. stageid misread) when its reward ratio is wildly
        // off the median — these are rejected from both calibration and the measured rows.
        private const double OutlierFactor = 3.0;

        // Minimum per-wave overhead used when no time model can be fit (rough spawn/travel floor).
        private const double DefaultWaveSec = 1.5;

        private struct RunStat
        {
            public string StageId;
            public double GoldRatio;   // measured gold / wiki expectedGold
            public double ExpRatio;    // measured per-hero exp / wiki expectedEXP
            public double HpRate;      // wiki totalHP / measured duration
            public double GoldPerSec;
            public double ExpPerSec;   // per hero
            public double Duration;
            public double Waves;       // wiki waves for the matched stage
            public double Hp;          // wiki totalHP for the matched stage
            public int StageLevel;     // wiki stage level (for retention)
            public int HeroLevel;      // party level at the time of the run
            public bool ExpValid;
        }

        /// <summary>A stable fingerprint of a run's party loadout (gear names + affixes + skill levels +
        /// character level). Runs with the same signature are the same build, so only matching runs are
        /// used to calibrate — a gear/skill change yields a new signature and old runs stop counting.</summary>
        public static string BuildSignature(RunRecord r)
        {
            if (r == null || r.Party == null || r.Party.Count == 0) return "";
            var heroes = new List<CharacterSnapshot>(r.Party);
            heroes.Sort((x, y) => string.CompareOrdinal(x?.Character ?? "", y?.Character ?? ""));
            var sb = new System.Text.StringBuilder();
            foreach (var h in heroes)
            {
                if (h == null) continue;
                sb.Append('H').Append(h.Character).Append('@').Append(h.Level).Append(';');
                var gear = new List<GearItem>(h.Equipment);
                gear.Sort((x, y) => string.CompareOrdinal(x?.Slot ?? "", y?.Slot ?? ""));
                foreach (var g in gear)
                {
                    if (g == null) continue;
                    sb.Append(g.Slot).Append('=').Append(g.ItemKey != 0 ? ("k" + g.ItemKey) : g.Name);
                    var af = new List<Affix>(g.Affixes);
                    af.Sort((x, y) => string.CompareOrdinal(x.Name ?? "", y.Name ?? ""));
                    foreach (var a in af) sb.Append('[').Append(a.Name).Append(System.Math.Round(a.Value)).Append(']');
                    sb.Append(',');
                }
                var sk = new List<SkillEntry>(h.Skills);
                sk.Sort((x, y) => x.Key != y.Key ? x.Key.CompareTo(y.Key) : string.CompareOrdinal(x.Name ?? "", y.Name ?? ""));
                foreach (var s in sk) sb.Append('s').Append(s.Key != 0 ? ("k" + s.Key) : s.Name).Append('L').Append(s.Level).Append(',');
                sb.Append('|');
            }
            return sb.ToString();
        }

        /// <summary>Choose which runs to calibrate from based on build signatures. Prefer runs matching
        /// <paramref name="currentSig"/>; if none match (e.g. just changed gear and haven't re-cleared),
        /// fall back to the most-recent build's runs and set <paramref name="stale"/>. Runs with no
        /// snapshot (empty signature, legacy records) are only used when no signed runs exist at all.</summary>
        private static List<RunRecord> SelectBuildRuns(List<RunRecord> all, string currentSig, out bool stale)
        {
            stale = false;
            if (all.Count == 0) return all;

            // signature per run (cache alongside)
            var sigs = new List<string>(all.Count);
            string newestSigned = null;
            foreach (var r in all)
            {
                string sg = BuildSignature(r);
                sigs.Add(sg);
                if (!string.IsNullOrEmpty(sg)) newestSigned = sg;   // last non-empty wins (runs are oldest→newest)
            }

            // no fingerprintable runs at all -> use everything (legacy behavior)
            if (newestSigned == null) return all;

            string target = !string.IsNullOrEmpty(currentSig) ? currentSig : newestSigned;
            var match = new List<RunRecord>();
            for (int i = 0; i < all.Count; i++) if (sigs[i] == target) match.Add(all[i]);

            if (match.Count > 0) return match;   // current build has runs (or we defaulted to newest)

            // current build has zero runs -> fall back to the most-recent build, flag stale
            stale = true;
            var fb = new List<RunRecord>();
            for (int i = 0; i < all.Count; i++) if (sigs[i] == newestSigned) fb.Add(all[i]);
            return fb;
        }

        /// <summary>EXP retention 0..1 at a stage level for a hero level — the wiki's "保留經驗值" curve.
        /// Reverse-engineered from the wiki: full near your level, quadratic falloff, then exponential
        /// decay to a 1% floor as the level gap grows. heroLevel/stageLevel ≤ 0 ⇒ no penalty (1).</summary>
        public static double ExpRetention(int heroLevel, int stageLevel)
        {
            if (heroLevel <= 0 || stageLevel <= 0) return 1.0;
            bool over = heroLevel >= stageLevel;
            double r = over ? 0.5 : 0.4;
            double i = System.Math.Log(heroLevel + 1) / 10.0 + 1.0;
            double a = System.Math.Truncate(i * (over ? 2 : 5));   // full-retention band
            double o = System.Math.Truncate(i * (over ? 5 : 6));   // falloff band
            double gap = System.Math.Abs(heroLevel - stageLevel);
            if (gap <= a) return 1.0;
            if (gap <= a + o)
            {
                double x = (gap - a) / o;
                return System.Math.Max(1.0 - (1.0 - r) * x * x, 0.01);
            }
            return System.Math.Max(System.Math.Pow(0.01 / r, (gap - a - o) / System.Math.Max(heroLevel / 3.0, 1.0)) * r, 0.01);
        }

        /// <summary>Compute the ranked rows for every stage. Measured where trusted runs exist,
        /// estimated otherwise. <paramref name="runs"/> may be null/empty (everything estimated;
        /// with no calibration at all, falls back to the wiki's per-HP ordering).</summary>
        public static List<EfficiencyRow> Rank(
            IEnumerable<FarmStage> stages, IEnumerable<RunRecord> runs, out Calibration calib,
            int fallbackHeroLevel = 0, string currentBuildSig = null)
        {
            var stageList = new List<FarmStage>(stages ?? new List<FarmStage>());
            var byId = new Dictionary<string, FarmStage>();
            foreach (var s in stageList) byId[s.StageId] = s;

            var allRuns = new List<RunRecord>(runs ?? new List<RunRecord>());

            // pick which build's runs to calibrate from (see SelectBuildRuns): prefer the current build,
            // else the most recent build (flagged stale so the UI can prompt a re-clear).
            bool stale;
            var selected = SelectBuildRuns(allRuns, currentBuildSig, out stale);

            // 1. collect well-formed run stats matched to a known stage
            var stats = new List<RunStat>();
            {
                foreach (var r in selected)
                {
                    if (r == null || string.IsNullOrEmpty(r.StageId) || r.Duration <= 0.01) continue;
                    if (!byId.TryGetValue(r.StageId, out var st)) continue;
                    if (st.ExpectedGold <= 0 || r.GoldGained <= 0) continue;   // gold must be sane
                    int party = r.Party != null ? r.Party.Count : 0;
                    bool expValid = party > 0 && st.ExpectedEXP > 0 && r.ExpGained > 0;
                    double expPerHero = expValid ? r.ExpGained / party : 0;
                    int heroLevel = r.RepLevel > 0 ? r.RepLevel : fallbackHeroLevel;
                    // de-retention the measured exp so the learned multiplier is the pure personal bonus:
                    // measured = expectedEXP × bonus × retention(heroLv, thisStageLv)
                    double ret = ExpRetention(heroLevel, st.Level);
                    double pureExpRatio = (expValid && ret > 0) ? (expPerHero / st.ExpectedEXP) / ret : 0;
                    stats.Add(new RunStat
                    {
                        StageId = r.StageId,
                        GoldRatio = r.GoldGained / st.ExpectedGold,
                        ExpRatio = pureExpRatio,
                        HpRate = r.Duration > 0 ? st.TotalHP / r.Duration : 0,
                        GoldPerSec = r.GoldGained / r.Duration,
                        ExpPerSec = expValid ? expPerHero / r.Duration : 0,
                        Duration = r.Duration,
                        Waves = st.Waves,
                        Hp = st.TotalHP,
                        StageLevel = st.Level,
                        HeroLevel = heroLevel,
                        ExpValid = expValid,
                    });
                }
            }

            // 2. robust medians, then reject gold-ratio outliers (mislabeled runs)
            calib = new Calibration();
            var trusted = new List<RunStat>();
            calib.Stale = stale && stats.Count > 0;   // only meaningful when we actually have (old) data
            if (stats.Count > 0)
            {
                double medGold = Median(Select(stats, s => s.GoldRatio));
                foreach (var s in stats)
                    if (medGold <= 0 || (s.GoldRatio <= medGold * OutlierFactor && s.GoldRatio >= medGold / OutlierFactor))
                        trusted.Add(s);

                if (trusted.Count > 0)
                {
                    calib.MGold = Median(Select(trusted, s => s.GoldRatio));
                    var expRatios = new List<double>();
                    foreach (var s in trusted) if (s.ExpValid) expRatios.Add(s.ExpRatio);
                    calib.MExp = expRatios.Count > 0 ? Median(expRatios) : calib.MGold;  // exp tracks gold
                    calib.ClearRate = Median(Select(trusted, s => s.HpRate));
                    calib.HasData = calib.ClearRate > 0;
                    calib.HeroLevel = fallbackHeroLevel;
                    foreach (var s in trusted) if (s.HeroLevel > calib.HeroLevel) calib.HeroLevel = s.HeroLevel;
                    FitTimeModel(trusted, calib);
                }
            }

            // 3. group trusted runs by stage for the measured rows
            var trustedByStage = new Dictionary<string, List<RunStat>>();
            foreach (var s in trusted)
            {
                if (!trustedByStage.TryGetValue(s.StageId, out var l)) { l = new List<RunStat>(); trustedByStage[s.StageId] = l; }
                l.Add(s);
            }
            calib.TrustedRuns = trusted.Count;
            calib.MeasuredStages = trustedByStage.Count;

            // 4. build a row per stage
            var rows = new List<EfficiencyRow>();
            foreach (var st in stageList)
            {
                var row = new EfficiencyRow { Stage = st };
                row.ExpRetention = ExpRetention(calib.HeroLevel, st.Level);
                if (trustedByStage.TryGetValue(st.StageId, out var rs) && rs.Count > 0)
                {
                    row.Measured = true;
                    row.Samples = rs.Count;
                    row.ClearSec = Median(Select(rs, s => s.Duration));
                    row.GoldPerSec = Median(Select(rs, s => s.GoldPerSec));
                    var eps = new List<double>();
                    foreach (var s in rs) if (s.ExpValid) eps.Add(s.ExpPerSec);
                    row.ExpPerSec = eps.Count > 0 ? Median(eps) : 0;   // real measured exp already reflects retention
                }
                else if (calib.HasData)
                {
                    row.Measured = false;
                    row.ClearSec = EstimateClearSec(st, calib);
                    if (row.ClearSec > 0)
                    {
                        row.GoldPerSec = st.ExpectedGold * calib.MGold / row.ClearSec;
                        // exp = base × personal bonus × retention(your level vs this stage's level)
                        row.ExpPerSec = st.ExpectedEXP * calib.MExp * row.ExpRetention / row.ClearSec;
                    }
                }
                else
                {
                    // no calibration yet — use the wiki's per-HP figures as a relative proxy so the
                    // ranking is still sensible out of the box (units differ; ordering is what matters)
                    row.Measured = false;
                    row.ClearSec = 0;
                    row.GoldPerSec = st.GoldPerHP;
                    row.ExpPerSec = st.ExpPerHP;
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Estimated clear seconds for a stage not yet farmed: the learned two-part model
        /// (per-wave overhead + per-HP throughput) when available, else HP/clearRate with a per-wave
        /// floor so trivial low-HP stages don't read as near-instant.</summary>
        private static double EstimateClearSec(FarmStage st, Calibration c)
        {
            if (c.HasTimeModel)
            {
                double t = c.PerWaveSec * st.Waves + c.SecPerHP * st.TotalHP;
                if (t > 0) return t;
            }
            double hpTime = c.ClearRate > 0 ? st.TotalHP / c.ClearRate : 0;
            double floor = st.Waves * DefaultWaveSec;
            return System.Math.Max(hpTime, floor);
        }

        /// <summary>Fit duration ≈ a·waves + b·totalHP via 2-predictor least squares (no intercept).
        /// Needs ≥2 trusted runs spanning distinct (waves, HP); rejects degenerate/negative fits.</summary>
        private static void FitTimeModel(List<RunStat> trusted, Calibration c)
        {
            if (trusted.Count < 2) return;
            double sww = 0, swh = 0, shh = 0, swd = 0, shd = 0;
            foreach (var s in trusted)
            {
                sww += s.Waves * s.Waves;
                swh += s.Waves * s.Hp;
                shh += s.Hp * s.Hp;
                swd += s.Waves * s.Duration;
                shd += s.Hp * s.Duration;
            }
            double det = sww * shh - swh * swh;
            if (System.Math.Abs(det) < 1e-6) return;          // waves & HP collinear -> can't separate
            double a = (swd * shh - shd * swh) / det;         // per-wave overhead
            double b = (sww * shd - swh * swd) / det;         // seconds per HP (1/effective dps)
            if (a < 0)                                        // negative overhead is unphysical: drop the
            {                                                 // wave term and refit a pure per-HP model
                a = 0;
                b = shh > 0 ? shd / shh : 0;                  // Σ(HP·duration) / Σ(HP²)
            }
            if (b <= 0 || double.IsNaN(a) || double.IsNaN(b)) return;
            c.PerWaveSec = a;
            c.SecPerHP = b;
            c.HasTimeModel = true;
        }

        /// <summary>Sort rows in place by the chosen key (descending for rates, ascending for time).</summary>
        public static void Sort(List<EfficiencyRow> rows, FarmSortKey key)
        {
            rows.Sort((a, b) =>
            {
                switch (key)
                {
                    case FarmSortKey.GoldPerSec: return b.GoldPerSec.CompareTo(a.GoldPerSec);
                    case FarmSortKey.ClearSec:
                        // unknown (0) clear times sort last
                        double av = a.ClearSec <= 0 ? double.MaxValue : a.ClearSec;
                        double bv = b.ClearSec <= 0 ? double.MaxValue : b.ClearSec;
                        return av.CompareTo(bv);
                    default: return b.ExpPerSec.CompareTo(a.ExpPerSec);
                }
            });
        }

        private static List<double> Select(List<RunStat> src, System.Func<RunStat, double> f)
        {
            var l = new List<double>(src.Count);
            foreach (var s in src) l.Add(f(s));
            return l;
        }

        /// <summary>Median of a list (returns 0 for empty). Does not mutate the input.</summary>
        public static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return 0;
            var copy = new List<double>(values);
            copy.Sort();
            int n = copy.Count;
            return (n & 1) == 1 ? copy[n / 2] : (copy[n / 2 - 1] + copy[n / 2]) / 2.0;
        }
    }
}

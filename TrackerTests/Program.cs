using System;
using System.Linq;
using TbhDpsMeter;

class Tests
{
    static int _fail = 0;
    static void Check(string name, bool cond, object got = null)
    {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name + (cond ? "" : "  (got: " + got + ")"));
        if (!cond) _fail++;
    }
    static bool Near(double a, double b, double eps = 0.01) => Math.Abs(a - b) <= eps;

    static int Main()
    {
        // --- total, hits, crit, type breakdown ---
        var t = new DpsTracker(windowSeconds: 5f);
        t.StartEncounter(0f);
        t.Record(100, false, 1, 0f);   // Melee
        t.Record(200, true, 1, 1f);    // Melee crit
        t.Record(300, false, 2, 2f);   // Projectile
        var s = t.GetSnapshot(2f);
        Check("total = 600", Near(s.Total, 600), s.Total);
        Check("hits = 3", s.Hits == 3, s.Hits);
        Check("duration = 2s", Near(s.DurationSeconds, 2f), s.DurationSeconds);
        Check("avg = 300", Near(s.AvgDps, 300), s.AvgDps);
        Check("critRate = 1/3", Near(s.CritRate, 1.0/3), s.CritRate);
        Check("critDmgShare = 200/600", Near(s.CritDamageShare, 200.0/600), s.CritDamageShare);
        var melee = s.ByType.First(p => p.Name == "Melee");
        var proj = s.ByType.First(p => p.Name == "Projectile");
        Check("melee amount = 300", Near(melee.Amount, 300), melee.Amount);
        Check("melee share = 0.5", Near(melee.Share, 0.5), melee.Share);
        Check("projectile share = 0.5", Near(proj.Share, 0.5), proj.Share);

        // --- sliding window drops old events ---
        var t2 = new DpsTracker(windowSeconds: 5f);
        t2.StartEncounter(0f);
        t2.Record(1000, false, 1, 0f);
        // at t=10, the 1000 hit (at t=0) is outside the 5s window
        Check("live dps decays to 0 after window", Near(t2.LiveDps(10f), 0), t2.LiveDps(10f));

        // --- live dps within window: 500 over min(elapsed,window) ---
        var t3 = new DpsTracker(windowSeconds: 5f);
        t3.StartEncounter(0f);
        t3.Record(500, false, 1, 4f);   // elapsed 4 < window 5 -> divide by 4
        Check("live dps early = 125 (500/4)", Near(t3.LiveDps(4f), 125), t3.LiveDps(4f));
        t3.Record(500, false, 1, 6f);   // elapsed 6 > window 5 -> divide by 5; both hits in window (t>=1)
        Check("live dps steady = 200 (1000/5)", Near(t3.LiveDps(6f), 200), t3.LiveDps(6f));

        // --- no early-start spike: a big first hit at ~0 elapsed must not divide by ~0 ---
        var tspike = new DpsTracker(windowSeconds: 5f);
        tspike.StartEncounter(0f);
        tspike.Record(1000, false, 1, 0.01f);   // 0.01s in
        Check("no early spike (1000/1s floor)", Near(tspike.LiveDps(0.01f), 1000), tspike.LiveDps(0.01f));
        Check("peak not inflated by early hit", tspike.GetSnapshot(0.01f).PeakDps <= 1000f, tspike.GetSnapshot(0.01f).PeakDps);

        // --- peak tracks the max live dps seen ---
        Check("peak >= 200", t3.GetSnapshot(6f).PeakDps >= 200f, t3.GetSnapshot(6f).PeakDps);

        // --- reset clears everything ---
        t3.StartEncounter(100f);
        var s3 = t3.GetSnapshot(100f);
        Check("reset total=0", Near(s3.Total, 0), s3.Total);
        Check("reset hits=0", s3.Hits == 0, s3.Hits);
        Check("reset peak=0", Near(s3.PeakDps, 0), s3.PeakDps);

        // --- zero / negative amounts ignored ---
        var t4 = new DpsTracker();
        t4.StartEncounter(0f);
        t4.Record(0, false, 1, 0f);
        t4.Record(-5, false, 1, 0f);
        Check("zero/neg ignored", t4.GetSnapshot(0f).Hits == 0, t4.GetSnapshot(0f).Hits);

        // --- auto-start when damage arrives before StartEncounter ---
        var t5 = new DpsTracker();
        t5.Record(50, false, 1, 3f);
        Check("auto-start records hit", t5.GetSnapshot(3f).Hits == 1, t5.GetSnapshot(3f).Hits);

        // ================= DamageTakenTracker =================
        // amount, isCritical, damageTypeFlag, attributeValue, now
        var dt = new DamageTakenTracker(windowSeconds: 5f);
        dt.StartEncounter(0f);
        dt.Record(100, false, 1, 0, 0f);   // Melee / Physical
        dt.Record(400, true,  2, 1, 1f);   // Projectile / Fire, monster crit, biggest
        dt.Record(300, false, 2, 1, 2f);   // Projectile / Fire
        var ds = dt.GetSnapshot(2f);
        Check("[taken] total = 800", Near(ds.Total, 800), ds.Total);
        Check("[taken] hits = 3", ds.Hits == 3, ds.Hits);
        Check("[taken] duration = 2s", Near(ds.DurationSeconds, 2f), ds.DurationSeconds);
        Check("[taken] avg = 400", Near(ds.AvgDtps, 400), ds.AvgDtps);
        Check("[taken] biggest hit = 400", Near(ds.BiggestHit, 400), ds.BiggestHit);
        Check("[taken] incoming crit rate = 1/3", Near(ds.CritRate, 1.0/3), ds.CritRate);
        var fire = ds.ByAttribute.First(p => p.Name == "Fire");
        var phys = ds.ByAttribute.First(p => p.Name == "Physical");
        Check("[taken] fire amount = 700", Near(fire.Amount, 700), fire.Amount);
        Check("[taken] fire share = 700/800", Near(fire.Share, 700.0/800), fire.Share);
        Check("[taken] physical share = 100/800", Near(phys.Share, 100.0/800), phys.Share);
        Check("[taken] attr sorted: fire first", ds.ByAttribute[0].Name == "Fire", ds.ByAttribute[0].Name);
        var proj2 = ds.ByType.First(p => p.Name == "Projectile");
        Check("[taken] projectile type amount = 700", Near(proj2.Amount, 700), proj2.Amount);

        // sliding window decays
        var dt2 = new DamageTakenTracker(windowSeconds: 5f);
        dt2.StartEncounter(0f);
        dt2.Record(1000, false, 1, 0, 0f);
        Check("[taken] live dtps decays to 0", Near(dt2.LiveDtps(10f), 0), dt2.LiveDtps(10f));

        // steady-state live dtps: 1000 over 5s window
        var dt3 = new DamageTakenTracker(windowSeconds: 5f);
        dt3.StartEncounter(0f);
        dt3.Record(500, false, 1, 0, 4f);
        Check("[taken] live dtps early = 125 (500/4)", Near(dt3.LiveDtps(4f), 125), dt3.LiveDtps(4f));
        dt3.Record(500, false, 1, 0, 6f);
        Check("[taken] live dtps steady = 200 (1000/5)", Near(dt3.LiveDtps(6f), 200), dt3.LiveDtps(6f));
        Check("[taken] peak >= 200", dt3.GetSnapshot(6f).PeakDtps >= 200f, dt3.GetSnapshot(6f).PeakDtps);

        // reset clears everything
        dt3.StartEncounter(100f);
        var ds3 = dt3.GetSnapshot(100f);
        Check("[taken] reset total=0", Near(ds3.Total, 0), ds3.Total);
        Check("[taken] reset hits=0", ds3.Hits == 0, ds3.Hits);
        Check("[taken] reset biggest=0", Near(ds3.BiggestHit, 0), ds3.BiggestHit);
        Check("[taken] reset peak=0", Near(ds3.PeakDtps, 0), ds3.PeakDtps);

        // zero / negative ignored
        var dt4 = new DamageTakenTracker();
        dt4.StartEncounter(0f);
        dt4.Record(0, false, 1, 0, 0f);
        dt4.Record(-5, false, 1, 0, 0f);
        Check("[taken] zero/neg ignored", dt4.GetSnapshot(0f).Hits == 0, dt4.GetSnapshot(0f).Hits);

        // attribute decode
        Check("[taken] decode attr 3 = Lightning", DamageTakenTracker.DecodeAttribute(3) == "Lightning", DamageTakenTracker.DecodeAttribute(3));

        StageCompareTests();
        SerializerTests();

        Console.WriteLine(_fail == 0 ? "\nALL TESTS PASSED" : $"\n{_fail} TEST(S) FAILED");
        return _fail == 0 ? 0 : 1;
    }

    // ================= StageCompare =================
    static RunRecord Run(string stage, string title, float dur, double total, float avg)
        => new RunRecord { StageId = stage, Title = title, Duration = dur, Total = total, Avg = avg };

    static void StageCompareTests()
    {
        Console.WriteLine("\n-- StageCompare --");
        var r36a = Run("3-6", "a", 80f, 500, 6.1f);
        var r36b = Run("3-6", "b", 72f, 510, 6.7f);   // fastest -> default baseline
        var r36c = Run("3-6", "c", 90f, 480, 5.9f);
        var r41  = Run("4-1", "d", 60f, 300, 5.0f);
        var runs = new System.Collections.Generic.List<RunRecord> { r36a, r36b, r36c, r41 };

        var groups = StageCompare.GroupByStage(runs);
        Check("[cmp] 2 stage groups", groups.Count == 2, groups.Count);
        Check("[cmp] 3-6 has 3 runs", groups["3-6"].Count == 3, groups["3-6"].Count);

        var baseDefault = StageCompare.PickBaseline(groups["3-6"]);
        Check("[cmp] default baseline = fastest (b)", baseDefault.Title == "b", baseDefault.Title);

        var basePinned = StageCompare.PickBaseline(groups["3-6"], "c");
        Check("[cmp] pinned baseline = c", basePinned.Title == "c", basePinned.Title);

        var cmp = StageCompare.Compare(baseDefault, r36a);
        var dur = cmp.Metrics.Find(m => m.Key == "duration");
        Check("[cmp] duration delta = +8", Near(dur.Delta, 8f), dur.Delta);
        var avgm = cmp.Metrics.Find(m => m.Key == "avg");
        Check("[cmp] avg pct ~ -8.96%", Near(avgm.PercentDelta, (6.1 - 6.7) / 6.7 * 100, 0.1), avgm.PercentDelta);
        Check("[cmp] self-compare flags IsBaseline", StageCompare.Compare(baseDefault, baseDefault).IsBaseline, false);

        // wave diffs
        var b = new RunRecord(); b.WaveDurations.AddRange(new[] { 8f, 9f, 10f });
        var c = new RunRecord(); c.WaveDurations.AddRange(new[] { 8.5f, 9f, 13f });
        var wres = StageCompare.Compare(b, c);
        Check("[cmp] 3 wave deltas", wres.Waves.Count == 3, wres.Waves.Count);
        Check("[cmp] wave3 delta = +3", Near(wres.Waves[2].Delta, 3f), wres.Waves[2].Delta);

        // gear & skill & stat diffs (match by slot)
        var bs = new CharacterSnapshot { Captured = true };
        bs.Stats.Add(new StatEntry("attack", 1240));
        bs.Stats.Add(new StatEntry("aspd", 1.45));
        var bow = new GearItem { Slot = "weapon", Name = "FlameBow" }; bow.Affixes.Add(new Affix("Fire", 45));
        bs.Equipment.Add(bow);
        bs.Equipment.Add(new GearItem { Slot = "ring", Name = "Ring" });
        bs.Skills.Add(new SkillEntry("Trap", 5));
        bs.Skills.Add(new SkillEntry("Shot", 3));

        var cs = new CharacterSnapshot { Captured = true };
        cs.Stats.Add(new StatEntry("attack", 1180));
        cs.Stats.Add(new StatEntry("aspd", 1.62));
        var bow2 = new GearItem { Slot = "weapon", Name = "WindBow" }; bow2.Affixes.Add(new Affix("Speed", 18));
        cs.Equipment.Add(bow2);
        cs.Equipment.Add(new GearItem { Slot = "ring", Name = "Ring" });  // unchanged
        cs.Skills.Add(new SkillEntry("Rain", 3));   // added
        cs.Skills.Add(new SkillEntry("Shot", 4));   // level up 3->4
        // Trap removed

        var rb = new RunRecord(); rb.Party.Add(bs);
        var rc = new RunRecord(); rc.Party.Add(cs);
        var dres = StageCompare.Compare(rb, rc);

        var atk = dres.Stats.Find(m => m.Key == "attack");
        Check("[cmp] attack 1240->1180", Near(atk.Baseline, 1240) && Near(atk.Current, 1180), atk.Current);

        Check("[cmp] 1 gear changed (weapon)", dres.Gear.Count == 1 && dres.Gear[0].Kind == StageCompare.ChangeKind.Changed, dres.Gear.Count);
        Check("[cmp] weapon changed key=weapon", dres.Gear[0].Key == "weapon", dres.Gear[0].Key);

        int added = 0, removed = 0, changed = 0;
        foreach (var sc in dres.Skills)
        {
            if (sc.Kind == StageCompare.ChangeKind.Added) added++;
            if (sc.Kind == StageCompare.ChangeKind.Removed) removed++;
            if (sc.Kind == StageCompare.ChangeKind.Changed) changed++;
        }
        Check("[cmp] skills: +1 added (Rain)", added == 1, added);
        Check("[cmp] skills: 1 removed (Trap)", removed == 1, removed);
        Check("[cmp] skills: 1 changed (Shot 3->4)", changed == 1, changed);

        // gear affix order / duplicate-name must not produce a false "Changed"
        var ga = new CharacterSnapshot { Captured = true };
        var gi1 = new GearItem { Slot = "w", Name = "Bow" }; gi1.Affixes.Add(new Affix("Fire", 10)); gi1.Affixes.Add(new Affix("Fire", 20));
        ga.Equipment.Add(gi1);
        var gb = new CharacterSnapshot { Captured = true };
        var gi2 = new GearItem { Slot = "w", Name = "Bow" }; gi2.Affixes.Add(new Affix("Fire", 20)); gi2.Affixes.Add(new Affix("Fire", 10));
        gb.Equipment.Add(gi2);
        var rga = new RunRecord(); rga.Party.Add(ga);
        var rgb = new RunRecord(); rgb.Party.Add(gb);
        var gcmp = StageCompare.Compare(rga, rgb);
        Check("[cmp] reordered dup affixes = no change", gcmp.Gear.Count == 0, gcmp.Gear.Count);
        var gi3 = new GearItem { Slot = "w", Name = "Bow" }; gi3.Affixes.Add(new Affix("Fire", 10)); gi3.Affixes.Add(new Affix("Fire", 30));
        var gc2 = new CharacterSnapshot { Captured = true }; gc2.Equipment.Add(gi3);
        var rgc2 = new RunRecord(); rgc2.Party.Add(gc2);
        var gcmp2 = StageCompare.Compare(rga, rgc2);
        Check("[cmp] real affix change detected", gcmp2.Gear.Count == 1 && gcmp2.Gear[0].Kind == StageCompare.ChangeKind.Changed, gcmp2.Gear.Count);
    }

    // ================= RunSerializer round-trip =================
    static void SerializerTests()
    {
        Console.WriteLine("\n-- RunSerializer --");
        var r = new RunRecord
        {
            Title = "06/07 12:34", StageId = "3-6", Total = 489930, Duration = 72.8f,
            Peak = 15020, Avg = 6730, CritRate = 0.099f, CritShare = 0.149f, Waves = 7,
            ActiveSeconds = 61.0f, IdleSeconds = 11.8f,
        };
        r.TypeFlags.Add(2); r.TypeAmounts.Add(300000);
        r.TypeFlags.Add(1); r.TypeAmounts.Add(189930);
        r.WaveDurations.AddRange(new[] { 8.2f, 9.1f, 10.4f });
        r.Samples.Add(new Sample { Dps = 6500.5f, Wave = 1 });
        r.Samples.Add(new Sample { Dps = 7200f, Wave = 2 });
        r.TakenTotal = 4580; r.TakenPeak = 171; r.TakenAvg = 63; r.TakenBiggestHit = 98; r.TakenCritRate = 0f; r.TakenHits = 59;
        r.TakenAttrValues.Add(1); r.TakenAttrAmounts.Add(3000);
        r.TakenTypeFlags.Add(2); r.TakenTypeAmounts.Add(2000);
        var snap = new CharacterSnapshot { Captured = true };
        snap.Stats.Add(new StatEntry("attack", 1240));
        var g = new GearItem { Slot = "weapon", Name = "Flame Bow" };
        g.Affixes.Add(new Affix("Fire", 45)); g.Affixes.Add(new Affix("Crit", 5.5));
        snap.Equipment.Add(g);
        snap.Skills.Add(new SkillEntry("Arrow Rain", 3));
        snap.Character = "priest";
        r.Party.Add(snap);

        string text = RunSerializer.Serialize(r);
        var r2 = RunSerializer.Deserialize(text.Split('\n'));
        var snap2 = r2.Party.Count > 0 ? r2.Party[0] : null;

        Check("[ser] title", r2.Title == r.Title, r2.Title);
        Check("[ser] stageid", r2.StageId == "3-6", r2.StageId);
        Check("[ser] total", Near(r2.Total, r.Total), r2.Total);
        Check("[ser] active", Near(r2.ActiveSeconds, 61.0), r2.ActiveSeconds);
        Check("[ser] idle", Near(r2.IdleSeconds, 11.8, 0.05), r2.IdleSeconds);
        Check("[ser] 2 type rows", r2.TypeFlags.Count == 2, r2.TypeFlags.Count);
        Check("[ser] wavedur 3", r2.WaveDurations.Count == 3 && Near(r2.WaveDurations[2], 10.4, 0.05), r2.WaveDurations.Count);
        Check("[ser] samples 2", r2.Samples.Count == 2 && r2.Samples[1].Wave == 2, r2.Samples.Count);
        Check("[ser] taken hits", r2.TakenHits == 59, r2.TakenHits);
        Check("[ser] snapshot captured", snap2 != null && snap2.Captured, snap2 != null);
        Check("[ser] character id", snap2.Character == "priest", snap2.Character);
        Check("[ser] snap stat", snap2.Stats.Count == 1 && Near(snap2.Stats[0].Value, 1240), snap2.Stats.Count);
        Check("[ser] snap gear+affixes", snap2.Equipment.Count == 1 && snap2.Equipment[0].Affixes.Count == 2, snap2.Equipment.Count);
        Check("[ser] gear name preserved", snap2.Equipment[0].Name == "Flame Bow", snap2.Equipment[0].Name);
        Check("[ser] gear affix value", Near(snap2.Equipment[0].Affixes[1].Value, 5.5), snap2.Equipment[0].Affixes[1].Value);
        Check("[ser] snap skill+level", snap2.Skills.Count == 1 && snap2.Skills[0].Level == 3, snap2.Skills.Count);

        // v1 backward compat: old file with no version / no new fields
        string v1 = "title=old\ntotal=1000\nduration=30\navg=33\nwaves=5\ntype=1:1000\nhist=100:1,200:2\n";
        var r3 = RunSerializer.Deserialize(v1.Split('\n'));
        Check("[ser] v1 loads title", r3.Title == "old", r3.Title);
        Check("[ser] v1 total", Near(r3.Total, 1000), r3.Total);
        Check("[ser] v1 no stageid", r3.StageId == "", r3.StageId);
        Check("[ser] v1 no party", r3.Party.Count == 0, r3.Party.Count);
        Check("[ser] v1 samples", r3.Samples.Count == 2, r3.Samples.Count);

        // multi-character round-trip + legacy v2 single-snap compat
        var rp = new RunRecord { StageId = "3-6" };
        var pa = new CharacterSnapshot { Captured = true, Character = "hunter" }; pa.Stats.Add(new StatEntry("attack", 400));
        var pb = new CharacterSnapshot { Captured = true, Character = "priest" }; pb.Skills.Add(new SkillEntry("Heal", 7));
        rp.Party.Add(pa); rp.Party.Add(pb);
        var rp2 = RunSerializer.Deserialize(RunSerializer.Serialize(rp).Split('\n'));
        Check("[ser] party 2 chars", rp2.Party.Count == 2, rp2.Party.Count);
        Check("[ser] char ids", rp2.Party[0].Character == "hunter" && rp2.Party[1].Character == "priest", rp2.Party[1].Character);
        Check("[ser] char2 skill", rp2.Party[1].Skills.Count == 1 && rp2.Party[1].Skills[0].Level == 7, rp2.Party[1].Skills.Count);

        string legacy = "version=2\ntitle=old\nsnap=1\nstat=attack:100\nskill=S\t3\n";
        var rl = RunSerializer.Deserialize(legacy.Split('\n'));
        Check("[ser] legacy snap -> 1 party member", rl.Party.Count == 1 && rl.Party[0].Stats.Count == 1, rl.Party.Count);

        // per-character compare: same party, only priest's skill changed
        var bRun = new RunRecord { StageId = "3-6", Duration = 70 };
        var b1 = new CharacterSnapshot { Captured = true, Character = "hunter" }; b1.Skills.Add(new SkillEntry("Shot", 5));
        var b2 = new CharacterSnapshot { Captured = true, Character = "priest" }; b2.Skills.Add(new SkillEntry("Heal", 3));
        bRun.Party.Add(b1); bRun.Party.Add(b2);
        var cRun = new RunRecord { StageId = "3-6", Duration = 80 };
        var c1 = new CharacterSnapshot { Captured = true, Character = "hunter" }; c1.Skills.Add(new SkillEntry("Shot", 5));
        var c2 = new CharacterSnapshot { Captured = true, Character = "priest" }; c2.Skills.Add(new SkillEntry("Heal", 6));
        cRun.Party.Add(c1); cRun.Party.Add(c2);
        Check("[ser] party chars listed", StageCompare.PartyCharacters(bRun, cRun).Count == 2, StageCompare.PartyCharacters(bRun, cRun).Count);
        var hunterCmp = StageCompare.Compare(bRun, cRun, "hunter");
        Check("[cmp] hunter unchanged skills", hunterCmp.Skills.Count == 0, hunterCmp.Skills.Count);
        var priestCmp = StageCompare.Compare(bRun, cRun, "priest");
        Check("[cmp] priest skill changed 3->6", priestCmp.Skills.Count == 1 && priestCmp.Skills[0].CurrentLevel == 6, priestCmp.Skills.Count);
    }
}

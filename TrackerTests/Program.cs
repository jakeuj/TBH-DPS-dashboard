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

        Console.WriteLine(_fail == 0 ? "\nALL TESTS PASSED" : $"\n{_fail} TEST(S) FAILED");
        return _fail == 0 ? 0 : 1;
    }
}

# Box-Open Quality Stats (F4) + F5 Persistence — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an F4 overlay that tallies opened-box item quality (EGradeType) split by box kind (NORMAL/BOSS/ACTBOSS) with a time-ordered log, and make the existing F5 box-pickup log persist across restarts.

**Architecture:** Pure, IL2CPP-free logic (aggregation + serialization) lives in files compiled into both the plugin and `TrackerTests`. IL2CPP glue (Harmony hooks reading `BoxOpenLog`/`StageBox`, and the IMGUI panel) lives in plugin-only files. Two Harmony hooks: a prefix on `StageBox`'s open method (param `EBoxType`) records the "currently opening kind"; a postfix on `BoxOpenLog..ctor(string, EGradeType)` records each dropped item with that kind. Persistence is plain TSV files under `BepInEx/config/`.

**Tech Stack:** C# / BepInEx 6 IL2CPP / HarmonyLib / Il2CppInterop / Unity IMGUI. Tests = `TrackerTests` console runner (`dotnet run`).

---

## File Structure

**Pure logic (added to `TrackerTests/TrackerTests.csproj` + compiled in plugin):**
- Create `DpsMeter/BoxModels.cs` — pure data types: `BoxKind` enum, `BoxGrade` table, `BoxEvent` (moved out of `BoxTracker.cs`), `BoxOpenEvent`.
- Create `DpsMeter/BoxOpenStats.cs` — aggregation matrix `[kind,grade]` + capped log + percentages + (de)serialization.
- Create `DpsMeter/BoxStore.cs` — F5 pickup-log persistence (pure file IO, dir injected).
- Create `DpsMeter/BoxOpenStore.cs` — F4 stats persistence (pure file IO, dir injected).

**IL2CPP glue (plugin only):**
- Create `DpsMeter/BoxOpenTracker.cs` — Harmony hooks + feeds `BoxOpenStats`.
- Create `DpsMeter/BoxOpenOverlayBehaviour.cs` — F4 IMGUI panel.
- Modify `DpsMeter/BoxTracker.cs` — remove moved `BoxEvent`; append to `BoxStore` on pickup.
- Modify `DpsMeter/Plugin.cs` — F4 key/config, register patches+overlay, set store dirs + session start, load persisted data.
- Modify `DpsMeter/BoxOverlayBehaviour.cs` — session-only per-hour; clear button also wipes the persisted file.
- Modify `DpsMeter/Localization.cs` — F4 keys + 10 grade names.

**Note on game types** (already referenced elsewhere under original namespaces): `TaskbarHero.Log.BoxOpenLog`, `TaskbarHero.Data.EGradeType`, `TaskbarHero.UI.StageBox`, `TaskbarHero.EBoxType`.

---

## Task 1: Pure data types (`BoxModels.cs`)

**Files:**
- Create: `DpsMeter/BoxModels.cs`
- Modify: `DpsMeter/BoxTracker.cs` (remove the `BoxEvent` class — now in BoxModels)

- [ ] **Step 1: Create `DpsMeter/BoxModels.cs`**

```csharp
using System;

namespace TbhDpsMeter
{
    /// <summary>Box kind, integer-compatible with the game's EBoxType (0..2); Unknown(3) is our bucket
    /// for opens whose kind context we couldn't capture, so no data is ever dropped.</summary>
    public enum BoxKind { Normal = 0, Boss = 1, ActBoss = 2, Unknown = 3 }

    /// <summary>The 10 item-quality grades, integer-compatible with the game's EGradeType (0..9).</summary>
    public static class BoxGrade
    {
        public const int Count = 10;
        // index == EGradeType int value
        public static readonly string[] Keys =
            { "common","uncommon","rare","legendary","immortal","arcana","beyond","celestial","divine","cosmic" };
        public static string KeyOf(int g) => (g >= 0 && g < Count) ? Keys[g] : "common";
    }

    /// <summary>One recorded box pickup (F5). Moved out of BoxTracker so the pure BoxStore can be unit-tested.</summary>
    public sealed class BoxEvent
    {
        public string Stage;     // e.g. "2-4 HELL"
        public DateTime Time;    // wall-clock moment of pickup
        public int Arg;          // raw value from OnGetBox
        public string Type;      // decoded box name
    }

    /// <summary>One opened item (F4): a single BoxOpenLog line — its grade, the box kind it came from,
    /// the item name, the stage, and when.</summary>
    public sealed class BoxOpenEvent
    {
        public DateTime Time;
        public int Grade;        // EGradeType int (0..9)
        public int Kind;         // BoxKind int (0..3)
        public string Name;      // dropped item name
        public string Stage;     // stage id at open time
    }
}
```

- [ ] **Step 2: Remove the `BoxEvent` class from `BoxTracker.cs`**

Delete lines 8-15 of `DpsMeter/BoxTracker.cs` (the `/// <summary>One recorded box pickup.</summary>` block and the whole `public sealed class BoxEvent { ... }`). Leave the rest of the file unchanged. `BoxTracker` still compiles because `BoxEvent` is now in the same namespace.

- [ ] **Step 3: Build the plugin to confirm it still compiles**

Run: `dotnet build DpsMeter/DpsMeter.csproj -c Release`
Expected: Build succeeded (BoxEvent resolves from BoxModels.cs).

- [ ] **Step 4: Commit**

```bash
git add DpsMeter/BoxModels.cs DpsMeter/BoxTracker.cs
git commit -m "refactor(box): extract pure BoxEvent + box kind/grade types into BoxModels"
```

---

## Task 2: Aggregation core (`BoxOpenStats.cs`)

**Files:**
- Create: `DpsMeter/BoxOpenStats.cs`
- Modify: `TrackerTests/TrackerTests.csproj` (compile the new pure files)
- Test: `TrackerTests/Program.cs`

- [ ] **Step 1: Create `DpsMeter/BoxOpenStats.cs`**

```csharp
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
```

- [ ] **Step 2: Register pure files in the test project**

Modify `TrackerTests/TrackerTests.csproj` — inside the existing `<ItemGroup>` of `<Compile Include=...>` entries, add:

```xml
    <Compile Include="..\DpsMeter\BoxModels.cs" />
    <Compile Include="..\DpsMeter\BoxOpenStats.cs" />
    <Compile Include="..\DpsMeter\BoxStore.cs" />
    <Compile Include="..\DpsMeter\BoxOpenStore.cs" />
```

(BoxStore.cs / BoxOpenStore.cs are created in Tasks 3-4; the csproj listing them before they exist is fine — they're added in the same plan before the test run that needs them. If running Task 2's test before Task 3/4, temporarily omit those two lines, or do Step 2's two store lines after Task 4.)

- [ ] **Step 3: Write failing tests** — append before `return _fail;` in `TrackerTests/Program.cs`

```csharp
        // ===== BoxOpenStats: aggregation + percentages =====
        var bo = new BoxOpenStats();
        // Normal(0): 3 common(0), 1 rare(2). Boss(1): 1 common, 1 legendary(3).
        bo.Add(new BoxOpenEvent { Kind = 0, Grade = 0, Name = "a", Stage = "1-1", Time = DateTime.Now });
        bo.Add(new BoxOpenEvent { Kind = 0, Grade = 0, Name = "b", Stage = "1-1", Time = DateTime.Now });
        bo.Add(new BoxOpenEvent { Kind = 0, Grade = 0, Name = "c", Stage = "1-1", Time = DateTime.Now });
        bo.Add(new BoxOpenEvent { Kind = 0, Grade = 2, Name = "d", Stage = "1-1", Time = DateTime.Now });
        bo.Add(new BoxOpenEvent { Kind = 1, Grade = 0, Name = "e", Stage = "1-1", Time = DateTime.Now });
        bo.Add(new BoxOpenEvent { Kind = 1, Grade = 3, Name = "f", Stage = "1-1", Time = DateTime.Now });
        Check("box total = 6", bo.Total() == 6, bo.Total());
        Check("normal total = 4", bo.KindTotal(0) == 4, bo.KindTotal(0));
        Check("normal common count = 3", bo.Count(0, 0) == 3, bo.Count(0, 0));
        Check("normal rare pct = 25", Near(bo.Percent(0, 2), 25.0), bo.Percent(0, 2));
        Check("boss legendary pct = 50", Near(bo.Percent(1, 3), 50.0), bo.Percent(1, 3));
        Check("grade-total common = 4", bo.GradeTotal(0) == 4, bo.GradeTotal(0));
        Check("unknown kind total = 0", bo.KindTotal(3) == 0, bo.KindTotal(3));
        // out-of-range grade buckets to common(0); out-of-range kind buckets to Unknown(3)
        bo.Add(new BoxOpenEvent { Kind = 9, Grade = 99, Name = "x", Stage = "?", Time = DateTime.Now });
        Check("oob kind -> unknown", bo.KindTotal(3) == 1, bo.KindTotal(3));
        Check("oob grade -> common", bo.Count(3, 0) == 1, bo.Count(3, 0));

        // counts round-trip
        var bo2 = new BoxOpenStats();
        bo2.LoadCounts(bo.SerializeCounts());
        Check("counts round-trip normal common", bo2.Count(0, 0) == 3, bo2.Count(0, 0));
        Check("counts round-trip total", bo2.Total() == bo.Total(), bo2.Total());

        // event round-trip
        var ev0 = new BoxOpenEvent { Kind = 2, Grade = 5, Name = "Sword of\tTabs", Stage = "3-2 HELL", Time = new DateTime(637000000000000000) };
        var ev1 = BoxOpenStats.ParseEvent(BoxOpenStats.SerializeEvent(ev0));
        Check("event round-trip kind", ev1.Kind == 2, ev1.Kind);
        Check("event round-trip grade", ev1.Grade == 5, ev1.Grade);
        Check("event round-trip stage", ev1.Stage == "3-2 HELL", ev1.Stage);
        Check("event round-trip ticks", ev1.Time.Ticks == ev0.Time.Ticks, ev1.Time.Ticks);
        Check("event sanitizes tabs", !ev1.Name.Contains('\t'), ev1.Name);

        // log cap
        var bo3 = new BoxOpenStats();
        for (int i = 0; i < BoxOpenStats.MaxLog + 50; i++)
            bo3.Add(new BoxOpenEvent { Kind = 0, Grade = 0, Name = "n", Stage = "1-1", Time = DateTime.Now });
        Check("log capped at MaxLog", bo3.Log.Count == BoxOpenStats.MaxLog, bo3.Log.Count);
```

- [ ] **Step 4: Run tests — expect FAIL (or build error) because BoxOpenStats/Store not yet all present**

Run: `dotnet run --project TrackerTests`
Expected: compile error or FAIL until Tasks 2-4 files exist. After Task 2 file is created and the two store lines temporarily omitted from csproj, the BoxOpenStats checks should PASS.

- [ ] **Step 5: Run tests — expect PASS for the new box checks**

Run: `dotnet run --project TrackerTests`
Expected: all `box ...` lines print PASS, process exits 0.

- [ ] **Step 6: Commit**

```bash
git add DpsMeter/BoxOpenStats.cs TrackerTests/TrackerTests.csproj TrackerTests/Program.cs
git commit -m "feat(box): BoxOpenStats aggregation matrix + serialization, with tests"
```

---

## Task 3: F5 pickup persistence (`BoxStore.cs`)

**Files:**
- Create: `DpsMeter/BoxStore.cs`
- Test: `TrackerTests/Program.cs`

- [ ] **Step 1: Create `DpsMeter/BoxStore.cs`**

```csharp
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
```

- [ ] **Step 2: Write failing test** — append before `return _fail;` in `TrackerTests/Program.cs`

```csharp
        // ===== BoxStore: pickup persistence round-trip =====
        BoxStore.Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tbh_boxstore_test_" + Guid.NewGuid().ToString("N"));
        BoxStore.Clear();
        BoxStore.Append(new BoxEvent { Time = new DateTime(637000000000000000), Stage = "2-4 HELL", Arg = 910651, Type = "Normal Monster Box Lv65" });
        BoxStore.Append(new BoxEvent { Time = new DateTime(637000000000000001), Stage = "2-4 HELL", Arg = 910999, Type = "Boss Box" });
        var loaded = BoxStore.LoadAll(500);
        Check("boxstore loaded 2", loaded.Count == 2, loaded.Count);
        Check("boxstore stage", loaded[0].Stage == "2-4 HELL", loaded[0].Stage);
        Check("boxstore arg", loaded[1].Arg == 910999, loaded[1].Arg);
        Check("boxstore type", loaded[0].Type == "Normal Monster Box Lv65", loaded[0].Type);
        var capped = BoxStore.LoadAll(1);
        Check("boxstore cap keeps newest", capped.Count == 1 && capped[0].Arg == 910999, capped.Count);
        BoxStore.Clear();
        Check("boxstore clear empties", BoxStore.LoadAll(500).Count == 0, BoxStore.LoadAll(500).Count);
```

- [ ] **Step 3: Run tests — expect PASS**

Run: `dotnet run --project TrackerTests`
Expected: `boxstore ...` lines PASS; exit 0.

- [ ] **Step 4: Commit**

```bash
git add DpsMeter/BoxStore.cs TrackerTests/Program.cs
git commit -m "feat(box): persist F5 pickup log to disk (BoxStore) with tests"
```

---

## Task 4: F4 stats persistence (`BoxOpenStore.cs`)

**Files:**
- Create: `DpsMeter/BoxOpenStore.cs`
- Test: `TrackerTests/Program.cs`

- [ ] **Step 1: Create `DpsMeter/BoxOpenStore.cs`**

```csharp
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
```

- [ ] **Step 2: Write failing test** — append before `return _fail;` in `TrackerTests/Program.cs`

```csharp
        // ===== BoxOpenStore: stats persistence round-trip =====
        BoxOpenStore.Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tbh_boxopen_test_" + Guid.NewGuid().ToString("N"));
        BoxOpenStore.Clear();
        var src = new BoxOpenStats();
        src.Add(new BoxOpenEvent { Kind = 0, Grade = 0, Name = "a", Stage = "1-1", Time = DateTime.Now });
        src.Add(new BoxOpenEvent { Kind = 1, Grade = 3, Name = "b", Stage = "1-1", Time = DateTime.Now });
        BoxOpenStore.Save(src);
        var dst = new BoxOpenStats();
        BoxOpenStore.Load(dst);
        Check("boxopenstore counts restored", dst.Total() == 2 && dst.Count(1, 3) == 1, dst.Total());
        Check("boxopenstore log restored", dst.Log.Count == 2, dst.Log.Count);
        BoxOpenStore.Clear();
        var empty = new BoxOpenStats();
        BoxOpenStore.Load(empty);
        Check("boxopenstore clear empties", empty.Total() == 0, empty.Total());
```

- [ ] **Step 3: Run tests — expect PASS**

Run: `dotnet run --project TrackerTests`
Expected: `boxopenstore ...` lines PASS; exit 0. Re-add the two store `<Compile>` lines to the csproj now if they were omitted in Task 2.

- [ ] **Step 4: Commit**

```bash
git add DpsMeter/BoxOpenStore.cs TrackerTests/Program.cs TrackerTests/TrackerTests.csproj
git commit -m "feat(box): persist F4 open-box stats to disk (BoxOpenStore) with tests"
```

---

## Task 5: IL2CPP hooks (`BoxOpenTracker.cs`)

**Files:**
- Create: `DpsMeter/BoxOpenTracker.cs`

- [ ] **Step 1: Create `DpsMeter/BoxOpenTracker.cs`**

```csharp
using System;
using System.Reflection;
using HarmonyLib;
using TaskbarHero;            // EBoxType
using TaskbarHero.Data;       // EGradeType
using TaskbarHero.Log;        // BoxOpenLog
using TaskbarHero.UI;         // StageBox

namespace TbhDpsMeter
{
    /// <summary>Captures opened-box item quality. A prefix on StageBox's open method (single EBoxType
    /// param) records the kind currently being opened; a postfix on BoxOpenLog..ctor(string, EGradeType)
    /// records each dropped item with that kind. Type names (BoxOpenLog/EGradeType/StageBox/EBoxType) are
    /// readable and survive game updates; the StageBox open method is obfuscated so it is resolved by
    /// signature (instance method, one EBoxType param) rather than by name.</summary>
    public static class BoxOpenTracker
    {
        public static readonly BoxOpenStats Stats = new BoxOpenStats();

        private static int _openingKind = (int)BoxKind.Unknown;
        private static DateTime _lastFlush = DateTime.MinValue;

        /// <summary>Register both hooks. Call once from Plugin.Load with the shared Harmony instance.</summary>
        public static void Install(Harmony harmony)
        {
            // Hook A: StageBox open method(s) taking a single EBoxType -> remember the kind.
            try
            {
                var sb = AccessTools.TypeByName("TaskbarHero.UI.StageBox");
                var ebox = AccessTools.TypeByName("TaskbarHero.EBoxType");
                int hooked = 0;
                if (sb != null && ebox != null)
                {
                    var pre = new HarmonyMethod(typeof(BoxOpenTracker).GetMethod(nameof(OpenPrefix), BindingFlags.NonPublic | BindingFlags.Static));
                    foreach (var m in sb.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        var ps = m.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType == ebox)
                        {
                            try { harmony.Patch(m, prefix: pre); hooked++; }
                            catch (Exception e) { Plugin.Logger?.LogWarning("[boxopen] StageBox patch " + m.Name + ": " + e.Message); }
                        }
                    }
                }
                Plugin.Logger?.LogInfo($"[boxopen] StageBox open hooks: {hooked}");
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[boxopen] StageBox hook failed: " + e.Message); }

            // Hook B: BoxOpenLog..ctor(string, EGradeType) -> record the drop.
            try
            {
                var t = AccessTools.TypeByName("TaskbarHero.Log.BoxOpenLog");
                MethodBase ctor = null;
                if (t != null)
                {
                    foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        var ps = c.GetParameters();
                        if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType.Name == "EGradeType") { ctor = c; break; }
                    }
                }
                if (ctor != null)
                {
                    var post = new HarmonyMethod(typeof(BoxOpenTracker).GetMethod(nameof(CtorPostfix), BindingFlags.NonPublic | BindingFlags.Static));
                    harmony.Patch(ctor, postfix: post);
                    Plugin.Logger?.LogInfo("[boxopen] hooked BoxOpenLog ctor");
                }
                else Plugin.Logger?.LogWarning("[boxopen] BoxOpenLog ctor(string,EGradeType) not found");
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[boxopen] BoxOpenLog hook failed: " + e.Message); }
        }

        // __0 is the EBoxType param (int-compatible 0..2); Unknown if outside range.
        private static void OpenPrefix(int __0)
        {
            _openingKind = (__0 >= 0 && __0 <= 2) ? __0 : (int)BoxKind.Unknown;
        }

        // __0 = item name, __1 = EGradeType (int-compatible 0..9)
        private static void CtorPostfix(string __0, int __1)
        {
            try
            {
                string stage = ""; try { stage = CharacterReader.CurrentStageId(); } catch { }
                Stats.Add(new BoxOpenEvent
                {
                    Time = DateTime.Now,
                    Grade = __1,
                    Kind = _openingKind,
                    Name = __0 ?? "",
                    Stage = stage,
                });
                var now = DateTime.Now;
                if ((now - _lastFlush).TotalSeconds >= 2.0) { _lastFlush = now; BoxOpenStore.Save(Stats); }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[boxopen] ctor postfix: " + e.Message); }
        }

        public static void Flush() { try { BoxOpenStore.Save(Stats); } catch { } }

        public static void ClearAll() { Stats.Clear(); BoxOpenStore.Clear(); }
    }
}
```

Notes for the implementer:
- Harmony patch methods can declare IL2CPP enum params as `int` (`__0`/`__1`) — Il2CppInterop marshals the underlying value. If the build complains that `__1` must be the exact enum type, change the signature to `EGradeType __1` and cast `(int)__1`, and likewise `EBoxType __0` → `(int)__0`.
- If `AccessTools.TypeByName` returns null for `TaskbarHero.EBoxType` (some interop builds prefix `Il2Cpp`), fall back to `typeof(EBoxType)` from the `using TaskbarHero;` import.

- [ ] **Step 2: Build the plugin**

Run: `dotnet build DpsMeter/DpsMeter.csproj -c Release`
Expected: Build succeeded. If a CS error says a `__1`/`__0` param type mismatch, apply the enum-typed signature note above and rebuild.

- [ ] **Step 3: Commit**

```bash
git add DpsMeter/BoxOpenTracker.cs
git commit -m "feat(box): Harmony hooks capturing opened-item grade per box kind"
```

---

## Task 6: Localization keys

**Files:**
- Modify: `DpsMeter/Localization.cs`

- [ ] **Step 1: Add F4 + grade keys**

In `DpsMeter/Localization.cs`, in the same dictionary as the existing `box_*` keys (near line 174), add these entries (5 columns = zh-Hant, en, ja, zh-Hans, es — match existing order):

```csharp
            { "boxopen_title",  new[] { "開箱統計", "Box Opens", "開封統計", "开箱统计", "Aperturas" } },
            { "boxopen_total",  new[] { "開出", "Opened", "開封", "开出", "Abiertas" } },
            { "boxopen_kind",   new[] { "箱種", "Kind", "箱種", "箱种", "Tipo" } },
            { "boxopen_grade",  new[] { "品質", "Grade", "品質", "品质", "Calidad" } },
            { "boxopen_item",   new[] { "物品", "Item", "アイテム", "物品", "Objeto" } },
            { "box_kind_normal",new[] { "一般", "Normal", "通常", "一般", "Normal" } },
            { "box_kind_boss",  new[] { "王箱", "Boss", "ボス", "王箱", "Jefe" } },
            { "box_kind_actboss",new[]{ "首領", "ActBoss", "章ボス", "首领", "ActJefe" } },
            { "box_kind_unknown",new[]{ "未知", "Unknown", "不明", "未知", "Desc." } },
            { "grade_common",   new[] { "普通", "Common", "コモン", "普通", "Común" } },
            { "grade_uncommon", new[] { "非凡", "Uncommon", "アンコモン", "非凡", "Infrecuente" } },
            { "grade_rare",     new[] { "稀有", "Rare", "レア", "稀有", "Raro" } },
            { "grade_legendary",new[] { "傳說", "Legendary", "レジェンダリー", "传说", "Legendario" } },
            { "grade_immortal", new[] { "不朽", "Immortal", "イモータル", "不朽", "Inmortal" } },
            { "grade_arcana",   new[] { "秘法", "Arcana", "アルカナ", "秘法", "Arcana" } },
            { "grade_beyond",   new[] { "超越", "Beyond", "ビヨンド", "超越", "Más Allá" } },
            { "grade_celestial",new[] { "天界", "Celestial", "セレスティアル", "天界", "Celestial" } },
            { "grade_divine",   new[] { "神聖", "Divine", "ディヴァイン", "神圣", "Divino" } },
            { "grade_cosmic",   new[] { "宇宙", "Cosmic", "コズミック", "宇宙", "Cósmico" } },
```

If `Localization.cs` uses a different column order or a different container shape, match what the existing `box_total` entry uses exactly (read lines 170-190 first).

- [ ] **Step 2: Build**

Run: `dotnet build DpsMeter/DpsMeter.csproj -c Release`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DpsMeter/Localization.cs
git commit -m "i18n(box): F4 panel + 10 grade names in 5 languages"
```

---

## Task 7: F4 overlay panel (`BoxOpenOverlayBehaviour.cs`)

**Files:**
- Create: `DpsMeter/BoxOpenOverlayBehaviour.cs`

- [ ] **Step 1: Create the panel**

Model it on `BoxOverlayBehaviour.cs` (drag/scale/auto-hide boilerplate). Use **panel slot 5** for `InputCompat.SetPanel`/drag (slot 4 is F5). Read `BoxOpenTracker.Stats`.

```csharp
using System;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>IMGUI overlay (F4): opened-box quality stats. A grade×kind matrix (count + %) over the
    /// lifetime tally, plus a paged time-ordered open log. Read-only; resize-safe; auto-hides with the
    /// other panels while a game menu is open.</summary>
    public class BoxOpenOverlayBehaviour : MonoBehaviour
    {
        public BoxOpenOverlayBehaviour(IntPtr ptr) : base(ptr) { }

        private const float Pad = 10f;
        private Rect _rect = new Rect(80, 80, 460, 0);
        private bool _visible, _placed;
        private float _wantX, _wantY;
        private Vector2 _dragOffset; private bool _dragging;

        private Texture2D _white, _bgTex;
        private GUIStyle _title, _label, _dim, _tiny, _btn, _box, _col, _cell;
        private bool _stylesReady;
        private Rect _closeRect, _clearRect, _pagePrev, _pageNext;
        private int _page;
        private float _scale = 1f;

        // per-grade row colors (index == grade int)
        private static readonly Color[] GradeColors = {
            new Color(0.78f,0.82f,0.88f), // common
            new Color(0.55f,0.85f,0.55f), // uncommon
            new Color(0.40f,0.65f,1.00f), // rare
            new Color(0.75f,0.50f,0.95f), // legendary
            new Color(1.00f,0.62f,0.28f), // immortal
            new Color(0.95f,0.40f,0.55f), // arcana
            new Color(0.30f,0.85f,0.85f), // beyond
            new Color(0.95f,0.85f,0.45f), // celestial
            new Color(1.00f,0.95f,0.75f), // divine
            new Color(1.00f,1.00f,1.00f), // cosmic
        };

        private static string Hex(Color c) => $"#{(int)(c.r*255):X2}{(int)(c.g*255):X2}{(int)(c.b*255):X2}";

        private Rect ScaledRect() => new Rect(_rect.x, _rect.y, _rect.width * _scale, _rect.height * _scale);

        void Awake() { _rect.width = Mathf.Max(420, Plugin.BoxOpenPanelWidth.Value); _visible = Plugin.BoxOpenStartVisible.Value; }
        void Start() => PlaceDefault();

        private void PlaceDefault()
        {
            float px = Plugin.BoxOpenPosX.Value, py = Plugin.BoxOpenPosY.Value;
            if (px < 0 || py < 0) { _rect.x = Mathf.Max(24, (Screen.width - _rect.width) * 0.5f); _rect.y = 110f; }
            else { _rect.x = px; _rect.y = py; }
            _wantX = _rect.x; _wantY = _rect.y; _placed = true;
        }

        void Update()
        {
            try
            {
                InputCompat.SetPanel(5, _visible && !GameUiState.MenuOpen(), ScaledRect());
                if (InputCompat.KeyPressed(Plugin.BoxOpenToggleKey)) _visible = !_visible;
                if (_visible) HandlePointer();
                else if (_dragging) _dragging = false;
            }
            catch { }
        }

        private void HandlePointer()
        {
            if (GameUiState.MenuOpen()) { if (_dragging) { _dragging = false; InputCompat.ReleaseDrag(5); } return; }
            Vector2 m = UiScale.ToLocal(InputCompat.MouseGuiPos(), _rect.x, _rect.y, _scale);
            if (InputCompat.MousePressed())
            {
                if (_closeRect.Contains(m)) { _visible = false; return; }
                if (_clearRect.Contains(m)) { BoxOpenTracker.ClearAll(); _page = 0; return; }
                if (_pagePrev.Contains(m)) { _page = Mathf.Max(0, _page - 1); return; }
                if (_pageNext.Contains(m)) { _page++; return; }
                if (_rect.Contains(m) && InputCompat.RequestDrag(5)) { _dragging = true; _dragOffset = m - new Vector2(_rect.x, _rect.y); }
            }
            if (_dragging)
            {
                if (InputCompat.MouseHeld()) { _rect.x = m.x - _dragOffset.x; _rect.y = m.y - _dragOffset.y; }
                if (InputCompat.MouseReleased()) { _dragging = false; _wantX = _rect.x; _wantY = _rect.y; Plugin.BoxOpenPosX.Value = _rect.x; Plugin.BoxOpenPosY.Value = _rect.y; }
            }
        }

        private void EnsureAssets()
        {
            if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
            if (_bgTex == null) { _bgTex = new Texture2D(1, 1); _bgTex.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f)); _bgTex.Apply(); if (_box != null) _box.normal.background = _bgTex; }
            if (_stylesReady) return;
            int fs = Plugin.FontSize.Value;
            _title = new GUIStyle { fontSize = fs, fontStyle = FontStyle.Bold, richText = true }; _title.normal.textColor = new Color(1f, 0.86f, 0.35f);
            _label = new GUIStyle { fontSize = fs, richText = true }; _label.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            _dim = new GUIStyle { fontSize = fs - 2, richText = true }; _dim.normal.textColor = new Color(0.78f, 0.84f, 0.95f);
            _tiny = new GUIStyle { fontSize = Mathf.Max(9, fs - 4), richText = true }; _tiny.normal.textColor = new Color(0.7f, 0.75f, 0.85f);
            _col = new GUIStyle { fontSize = fs, richText = true, alignment = TextAnchor.MiddleRight }; _col.normal.textColor = Color.white;
            _cell = new GUIStyle { fontSize = Mathf.Max(9, fs - 3), richText = true, alignment = TextAnchor.MiddleRight }; _cell.normal.textColor = Color.white;
            _btn = new GUIStyle(GUI.skin.button) { fontSize = fs - 2, fontStyle = FontStyle.Bold, richText = true };
            _box = new GUIStyle(); _box.normal.background = _bgTex;
            _stylesReady = true;
        }

        private void DrawRect(float x, float y, float w, float h, Color c) { var p = GUI.color; GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), _white); GUI.color = p; }

        private static readonly int[] KindCols = { (int)BoxKind.Normal, (int)BoxKind.Boss, (int)BoxKind.ActBoss };

        void OnGUI()
        {
            if (!_visible || GameUiState.MenuOpen()) return;
            GUI.depth = -8;
            var prevM = GUI.matrix;
            try
            {
                EnsureAssets();
                if (!_placed) PlaceDefault();
                var st = BoxOpenTracker.Stats;
                int fs = Plugin.FontSize.Value; float lh = fs + 6;
                bool hasUnknown = st.KindTotal((int)BoxKind.Unknown) > 0;

                // visible grade rows = those with any count (hide empty tail)
                int gradeRows = 0; for (int g = 0; g < BoxGrade.Count; g++) if (st.GradeTotal(g) > 0) gradeRows++;
                if (gradeRows == 0) gradeRows = 0;

                // paged log sizing
                int logRowsPerPage = Mathf.Clamp((int)((Screen.height - 320) / lh), 4, 40);
                int pages = Mathf.Max(1, (st.Log.Count + logRowsPerPage - 1) / logRowsPerPage);
                _page = Mathf.Clamp(_page, 0, pages - 1);
                int shownLog = Mathf.Min(logRowsPerPage, st.Log.Count - _page * logRowsPerPage);

                float h = Pad + lh /*title*/ + lh /*matrix header*/ + lh * Mathf.Max(gradeRows, 1)
                        + lh * 0.5f + lh /*log header*/ + lh * Mathf.Max(shownLog, 1) + lh /*footer*/ + Pad;
                _rect.height = h;
                _scale = UiScale.Fit(_rect.width, _rect.height);
                if (!_dragging) { _rect.x = Mathf.Clamp(_wantX, 0f, Mathf.Max(0f, Screen.width - _rect.width * _scale)); _rect.y = Mathf.Clamp(_wantY, 0f, Mathf.Max(0f, Screen.height - _rect.height * _scale)); }
                float x = _rect.x, ix = x + Pad, w = _rect.width, iw = w - Pad * 2;
                GUI.matrix = UiScale.Matrix(_rect.x, _rect.y, _scale);
                GUI.Box(_rect, GUIContent.none, _box);

                float cy = _rect.y + Pad;
                float clearW = Mathf.Max(60f, _btn.CalcSize(new GUIContent(Loc.G("reset_all"))).x + 12f);
                GUI.Label(new Rect(ix, cy, iw, lh), $"{Loc.G("boxopen_title")}  <size=11><color=#9fb4cc>{Loc.G("boxopen_total")} {st.Total()}</color></size>", _title);
                _clearRect = new Rect(x + w - 28 - clearW, cy - 1, clearW, lh); GUI.Button(_clearRect, Loc.G("reset_all"), _btn);
                _closeRect = new Rect(x + w - 26, cy - 2, 22, lh); GUI.Button(_closeRect, "✕", _btn);
                cy += lh;

                // ---- matrix: rows = grade, cols = Normal | Boss | ActBoss | (Unknown) | Total ----
                int nCols = KindCols.Length + (hasUnknown ? 1 : 0) + 1; // + Total
                float gradeColW = iw * 0.26f;
                float cellW = (iw - gradeColW) / nCols;
                // header
                GUI.Label(new Rect(ix, cy, gradeColW, lh), $"<size=11><color=#9fb4cc>{Loc.G("boxopen_grade")}</color></size>", _dim);
                float hx = ix + gradeColW;
                string[] kindKeys = { "box_kind_normal", "box_kind_boss", "box_kind_actboss" };
                for (int c = 0; c < KindCols.Length; c++) { GUI.Label(new Rect(hx, cy, cellW, lh), $"<size=11><color=#9fb4cc>{Loc.G(kindKeys[c])}</color></size>", _cell); hx += cellW; }
                if (hasUnknown) { GUI.Label(new Rect(hx, cy, cellW, lh), $"<size=11><color=#9fb4cc>{Loc.G("box_kind_unknown")}</color></size>", _cell); hx += cellW; }
                GUI.Label(new Rect(hx, cy, cellW, lh), "<size=11><color=#cfd6e0>Σ</color></size>", _cell);
                cy += lh;
                DrawRect(ix, cy - 1, iw, 1, new Color(1, 1, 1, 0.12f));

                // rows
                for (int g = 0; g < BoxGrade.Count; g++)
                {
                    if (st.GradeTotal(g) == 0) continue;
                    string gc = Hex(g < GradeColors.Length ? GradeColors[g] : Color.white);
                    GUI.Label(new Rect(ix, cy, gradeColW, lh), $"<color={gc}>{Loc.G("grade_" + BoxGrade.KeyOf(g))}</color>", _label);
                    float cx = ix + gradeColW;
                    for (int c = 0; c < KindCols.Length; c++) { DrawCell(cx, cy, cellW, lh, st, KindCols[c], g, gc); cx += cellW; }
                    if (hasUnknown) { DrawCell(cx, cy, cellW, lh, st, (int)BoxKind.Unknown, g, gc); cx += cellW; }
                    // total column: count + overall %
                    long gt = st.GradeTotal(g); double gp = st.Total() > 0 ? 100.0 * gt / st.Total() : 0;
                    GUI.Label(new Rect(cx, cy, cellW, lh), $"<color={gc}>{gt}</color> <size=9><color=#9aa3b0>{gp:0.#}%</color></size>", _cell);
                    cy += lh;
                }
                if (gradeRows == 0) { GUI.Label(new Rect(ix, cy, iw, lh), $"<color=#8a93a0>{Loc.G("box_empty")}</color>", _tiny); cy += lh; }

                // ---- log ----
                cy += lh * 0.5f;
                DrawRect(ix, cy + lh - 1, iw, 1, new Color(1, 1, 1, 0.12f));
                GUI.Label(new Rect(ix, cy, iw * 0.22f, lh), $"<size=11><color=#9fb4cc>{Loc.G("time_col")}</color></size>", _dim);
                GUI.Label(new Rect(ix + iw * 0.22f, cy, iw * 0.20f, lh), $"<size=11><color=#9fb4cc>{Loc.G("boxopen_kind")}</color></size>", _dim);
                GUI.Label(new Rect(ix + iw * 0.42f, cy, iw * 0.22f, lh), $"<size=11><color=#9fb4cc>{Loc.G("boxopen_grade")}</color></size>", _dim);
                GUI.Label(new Rect(ix + iw * 0.64f, cy, iw * 0.36f, lh), $"<size=11><color=#9fb4cc>{Loc.G("boxopen_item")}</color></size>", _dim);
                cy += lh;

                int start = _page * logRowsPerPage;
                string[] kindShort = { "box_kind_normal", "box_kind_boss", "box_kind_actboss", "box_kind_unknown" };
                for (int i = 0; i < shownLog; i++)
                {
                    var e = st.Log[st.Log.Count - 1 - (start + i)];
                    if ((i & 1) == 1) DrawRect(ix, cy, iw, lh, new Color(1, 1, 1, 0.03f));
                    string gc = Hex(e.Grade >= 0 && e.Grade < GradeColors.Length ? GradeColors[e.Grade] : Color.white);
                    int kind = (e.Kind >= 0 && e.Kind < kindShort.Length) ? e.Kind : (int)BoxKind.Unknown;
                    GUI.Label(new Rect(ix, cy, iw * 0.22f, lh), $"<color=#aeb6c2>{e.Time:HH:mm:ss}</color>", _tiny);
                    GUI.Label(new Rect(ix + iw * 0.22f, cy, iw * 0.20f, lh), $"<color=#9aa3b0>{Loc.G(kindShort[kind])}</color>", _tiny);
                    GUI.Label(new Rect(ix + iw * 0.42f, cy, iw * 0.22f, lh), $"<color={gc}>{Loc.G("grade_" + BoxGrade.KeyOf(e.Grade))}</color>", _tiny);
                    GUI.Label(new Rect(ix + iw * 0.64f, cy, iw * 0.36f, lh), $"<color=#eaf3ee>{e.Name}</color>", _tiny);
                    cy += lh;
                }
                if (st.Log.Count == 0) { GUI.Label(new Rect(ix, cy - lh, iw, lh), $"<color=#8a93a0>{Loc.G("box_empty")}</color>", _tiny); }

                _pagePrev = new Rect(ix, cy, 26, lh - 2); _pageNext = new Rect(ix + 30, cy, 26, lh - 2);
                GUI.Button(_pagePrev, "◀", _btn); GUI.Button(_pageNext, "▶", _btn);
                GUI.Label(new Rect(ix + 64, cy, iw - 64, lh), $"<size=11><color=#9fb4cc>{_page + 1}/{pages}</color></size>", _dim);
            }
            catch { }
            finally { GUI.matrix = prevM; }
        }

        private void DrawCell(float x, float y, float w, float lh, BoxOpenStats st, int kind, int grade, string gc)
        {
            long c = st.Count(kind, grade);
            if (c == 0) { GUI.Label(new Rect(x, y, w, lh), "<size=9><color=#5a6personally>·</color></size>".Replace("personally",""), _cell); return; }
            double pct = st.Percent(kind, grade);
            GUI.Label(new Rect(x, y, w, lh), $"<color={gc}>{c}</color> <size=9><color=#9aa3b0>{pct:0.#}%</color></size>", _cell);
        }
    }
}
```

Implementer note: the `DrawCell` empty-cell branch above contains an intentionally ugly placeholder string — replace its body with a clean dim dot:

```csharp
            if (c == 0) { GUI.Label(new Rect(x, y, w, lh), "<size=9><color=#5a626e>·</color></size>", _cell); return; }
```

Also confirm `InputCompat.RequestDrag(int)` / `ReleaseDrag(int)` exist with these signatures (used by other panels); if the box panel used a different drag-arbitration call, mirror exactly what `BoxOverlayBehaviour.HandlePointer` does for press/drag.

- [ ] **Step 2: Build**

Run: `dotnet build DpsMeter/DpsMeter.csproj -c Release`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DpsMeter/BoxOpenOverlayBehaviour.cs
git commit -m "feat(box): F4 open-box quality matrix + log overlay"
```

---

## Task 8: Wire into Plugin + F5 session-only per-hour

**Files:**
- Modify: `DpsMeter/Plugin.cs`
- Modify: `DpsMeter/BoxTracker.cs`
- Modify: `DpsMeter/BoxOverlayBehaviour.cs`

- [ ] **Step 1: Plugin — config, key, dirs, patches, overlay, load**

In `DpsMeter/Plugin.cs`:

a) Add fields near the other box config (after line 69):
```csharp
        // box-open (F4) panel config
        public static ConfigEntry<float> BoxOpenPosX;
        public static ConfigEntry<float> BoxOpenPosY;
        public static ConfigEntry<float> BoxOpenPanelWidth;
        public static ConfigEntry<bool> BoxOpenStartVisible;
        private static ConfigEntry<string> _boxOpenToggleKeyName;
        public static KeyCode BoxOpenToggleKey = KeyCode.F4;
        public static readonly DateTime SessionStart = DateTime.Now;
```

b) Bind config near the other box binds (after line 125):
```csharp
            BoxOpenPosX = Config.Bind("BoxOpenUI", "PosX", -1f, "Open-box stats overlay X. -1 = auto.");
            BoxOpenPosY = Config.Bind("BoxOpenUI", "PosY", -1f, "Open-box stats overlay Y. -1 = auto.");
            BoxOpenPanelWidth = Config.Bind("BoxOpenUI", "PanelWidth", 460f, "Open-box stats overlay width in pixels.");
            BoxOpenStartVisible = Config.Bind("BoxOpenUI", "StartVisible", false, "Show the open-box stats overlay on launch.");
            _boxOpenToggleKeyName = Config.Bind("BoxOpenUI", "ToggleKey", "F4", "Key to show/hide the open-box stats overlay (UnityEngine.KeyCode name).");
```

c) Parse the key near the other parses (after line 136):
```csharp
            if (!Enum.TryParse(_boxOpenToggleKeyName.Value, true, out BoxOpenToggleKey))
                BoxOpenToggleKey = KeyCode.F4;
```

d) Set store dirs + load persisted data, right after `Loc.Init(...)` and before overlay creation (e.g. after line 145 `StageProbe.TryHook(harmony);`):
```csharp
            BoxStore.Dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "dpsmeter_boxlog");
            BoxOpenStore.Dir = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "dpsmeter_boxopen");
            try { foreach (var e in BoxStore.LoadAll(500)) BoxTracker.Events.Add(e); } catch { }
            try { BoxOpenStore.Load(BoxOpenTracker.Stats); } catch { }
            BoxOpenTracker.Install(harmony);
```

e) Register + add the overlay component (alongside the others, lines 153 & 161):
```csharp
                ClassInjector.RegisterTypeInIl2Cpp<BoxOpenOverlayBehaviour>();
```
```csharp
                go.AddComponent<BoxOpenOverlayBehaviour>();
```

f) Update the log line (line 162) to mention F4:
```csharp
                Logger.LogInfo("Overlays created. DPS " + ToggleKey + ", taken " + TakenToggleKey + ", compare " + CompareToggleKey + ", farm " + FarmToggleKey + ", box " + BoxToggleKey + ", boxopen " + BoxOpenToggleKey + ".");
```

- [ ] **Step 2: BoxTracker — persist each pickup**

In `DpsMeter/BoxTracker.cs`, inside `OnGetBox`, right after `Events.Add(ev);` (and the cap trim), add:
```csharp
                BoxStore.Append(ev);
```
Place it after `if (Events.Count > 500) Events.RemoveAt(0);` so the appended event matches what's kept in memory.

- [ ] **Step 3: BoxOverlayBehaviour — session-only per-hour + clear wipes file**

In `DpsMeter/BoxOverlayBehaviour.cs`:

Replace the per-hour computation (lines 143-144):
```csharp
                double hours = 0; if (ev.Count >= 2) hours = (ev[ev.Count - 1].Time - ev[0].Time).TotalHours;
                double perHr = hours > 0.0003 ? ev.Count / hours : 0;
```
with session-window logic:
```csharp
                int sessCount = 0; DateTime sFirst = default, sLast = default;
                foreach (var e in ev)
                {
                    if (e.Time < Plugin.SessionStart) continue;
                    if (sessCount == 0) sFirst = e.Time;
                    sLast = e.Time; sessCount++;
                }
                double hours = sessCount >= 2 ? (sLast - sFirst).TotalHours : 0;
                double perHr = hours > 0.0003 ? sessCount / hours : 0;
```

Update the clear-button handler (line 81) to also wipe the persisted file:
```csharp
                if (_clearRect.Contains(m)) { BoxTracker.Events.Clear(); BoxStore.Clear(); BoxTracker.Version++; _page = 0; return; }
```

- [ ] **Step 4: Build the plugin**

Run: `dotnet build DpsMeter/DpsMeter.csproj -c Release`
Expected: Build succeeded.

- [ ] **Step 5: Run the unit tests once more (regression)**

Run: `dotnet run --project TrackerTests`
Expected: exit 0, no FAIL lines.

- [ ] **Step 6: Commit**

```bash
git add DpsMeter/Plugin.cs DpsMeter/BoxTracker.cs DpsMeter/BoxOverlayBehaviour.cs
git commit -m "feat(box): wire F4 overlay + hooks, persist F5 pickups, session-only per-hour"
```

---

## Task 9: Deploy + in-game verification

Follows the memory note "Deploy: restart + verify" — after deploying the DLL, restart the game yourself and confirm it loaded.

- [ ] **Step 1: Deploy the built DLL to the game's BepInEx plugins**

Determine the plugin output + game dir from the csproj (`$(GameDir)` / a post-build copy). If the build already copies to `…/BepInEx/plugins`, skip. Otherwise copy `DpsMeter/bin/Release/net6.0/DpsMeter.dll` (confirm TFM from the build output path) to `D:\SteamLibrary\steamapps\common\TaskbarHero\BepInEx\plugins\`.

- [ ] **Step 2: Restart the game and confirm load**

Launch TaskbarHero. Tail `…/BepInEx/LogOutput.log` and confirm:
- `TBH DPS Meter <version> loaded.`
- `[boxopen] hooked BoxOpenLog ctor`
- `[boxopen] StageBox open hooks: N` with N ≥ 1
- `Overlays created. … boxopen F4.`

If `BoxOpenLog ctor … not found` or `StageBox open hooks: 0`, apply the fallback notes in Task 5 (enum-typed Harmony params / `typeof` fallback for `EBoxType`) and redeploy.

- [ ] **Step 3: Functional check in-game**

- Press **F4** — the open-box panel appears (empty state shows the empty label).
- Open some boxes (normal + a boss/act-boss box). Confirm rows appear with counts and %, in the correct kind column, and the time-ordered log fills in.
- Press **F5** — confirm pickups still logged and per-hour shows a sane this-session number.

- [ ] **Step 4: Persistence check**

- Note the F4 totals and F5 total. Quit the game fully. Relaunch.
- Press F4: lifetime matrix counts are still there. Press F5: pickup history still present.
- Confirm `BepInEx/config/dpsmeter_boxopen/counts.txt` and `dpsmeter_boxlog/log.txt` exist and are non-empty.

- [ ] **Step 5: Version bump + commit + tag**

Bump `Plugin.Version` (e.g. to `0.7.0` — new feature). Update README feature list/screenshots if appropriate (optional). Commit:
```bash
git add -A
git commit -m "feat(box): open-box quality stats (F4) + persistent box logs; v0.7.0"
```

---

## Self-Review

- **Spec coverage:** F4 matrix (kind×grade count+%) → Tasks 2,7. Time-ordered open log → Tasks 2,7. Box-kind attribution via StageBox hook + Unknown fallback → Task 5. BoxOpenLog grade capture → Task 5. F4 persistence → Tasks 4,8. F5 persistence → Tasks 3,8. Session-only per-hour → Task 8. Localization (5 lang + 10 grades) → Task 6. F4 key wiring → Task 8. Out-of-scope (per-stage split, theoretical rates) correctly omitted. ✓
- **Placeholder scan:** The one intentional ugly placeholder in Task 7 `DrawCell` is called out with its exact replacement in the same task. No TODO/TBD remain. ✓
- **Type consistency:** `BoxOpenStats` API (`Count/KindTotal/GradeTotal/Total/Percent/Add/Clear/SerializeCounts/LoadCounts/SerializeEvent/ParseEvent`), `BoxKind`(0-3), `BoxGrade.Count`=10/`KeyOf`, `BoxOpenEvent`/`BoxEvent` fields, `BoxOpenTracker.Stats/Install/Flush/ClearAll`, store `Dir`/`Load`/`Save`/`Append`/`LoadAll`/`Clear`, Plugin `BoxOpen*` config + `SessionStart` + `BoxOpenToggleKey`, panel slot 5 — all consistent across tasks. ✓

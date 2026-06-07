using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaskbarHero;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>Reflection helpers for navigating obfuscated Il2CppInterop wrappers by member name.
    /// Member names come from the IL2CPP RE mapping; reflection keeps us resilient to "which of N
    /// same-signature getters" uncertainty (we try candidates) and to obfuscation churn.</summary>
    internal static class Refl
    {
        private const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>Property getter, else field, else zero-arg method, by name. Null on any failure.</summary>
        public static object Get(object o, string name)
        {
            if (o == null) return null;
            try
            {
                var t = o.GetType();
                var p = t.GetProperty(name, F);
                if (p != null && p.CanRead) return p.GetValue(o);
                var f = t.GetField(name, F);
                if (f != null) return f.GetValue(o);
                var m = t.GetMethod(name, F, null, Type.EmptyTypes, null);
                if (m != null) return m.Invoke(o, null);
            }
            catch { }
            return null;
        }

        /// <summary>Invoke a method by name with the given args (matched by arg count). Null on failure.</summary>
        public static object Call(object o, string name, params object[] args)
        {
            if (o == null) return null;
            try
            {
                int n = args?.Length ?? 0;
                foreach (var m in o.GetType().GetMethods(F))
                {
                    if (m.Name != name) continue;
                    if (m.GetParameters().Length != n) continue;
                    return m.Invoke(o, args);
                }
            }
            catch { }
            return null;
        }

        /// <summary>Enumerate an Il2Cpp list/collection via Count + get_Item(i).</summary>
        public static IEnumerable<object> Enumerate(object list)
        {
            if (list == null) yield break;
            // works for Il2Cpp List/IReadOnlyList (Count + get_Item) and arrays (Length + get_Item/[])
            int count;
            try
            {
                var c = Get(list, "Count") ?? Get(list, "Length");
                if (c == null) yield break;
                count = Convert.ToInt32(c);
            }
            catch { yield break; }
            const int cap = 256;
            if (count > cap) { Plugin.Logger?.LogWarning($"Refl.Enumerate truncated {count}->{cap}"); count = cap; }
            for (int i = 0; i < count; i++)
            {
                object item = null;
                try { item = Call(list, "get_Item", i); } catch { }
                if (item != null) yield return item;
            }
        }

        /// <summary>Enumerate via GetEnumerator/MoveNext/Current — works for IReadOnlyCollection/IEnumerable
        /// that have no indexer (e.g. the equipped-item uid collection).</summary>
        public static IEnumerable<object> EnumerateE(object seq)
        {
            if (seq == null) yield break;
            // Preferred: cast to the Il2Cpp non-generic IEnumerable so Il2CppInterop's own enumerator
            // is used (reflection can't see the explicit interface members on interface wrappers).
            if (seq is Il2CppSystem.Collections.IEnumerable il2cppEnum)
            {
                var e = il2cppEnum.GetEnumerator();
                int g2 = 0;
                while (e != null && g2++ < 512 && e.MoveNext())
                    if (e.Current != null) yield return e.Current;
                yield break;
            }
            object en = InvokeBySuffix(seq, "GetEnumerator");
            if (en == null) { foreach (var x in Enumerate(seq)) yield return x; yield break; }
            int guard = 0;
            while (guard++ < 512)
            {
                object mv = InvokeBySuffix(en, "MoveNext");
                bool moved; try { moved = Convert.ToBoolean(mv); } catch { yield break; }
                if (!moved) yield break;
                object cur = GetBySuffix(en, "Current") ?? InvokeBySuffix(en, "get_Current");
                if (cur != null) yield return cur;
            }
        }

        /// <summary>Invoke a zero-arg method whose name equals or ends with the suffix
        /// (handles explicit interface impls like "...IEnumerable.GetEnumerator").</summary>
        private static object InvokeBySuffix(object o, string suffix)
        {
            if (o == null) return null;
            try
            {
                MethodInfo best = null;
                foreach (var m in o.GetType().GetMethods(F))
                {
                    if (m.GetParameters().Length != 0) continue;
                    if (m.Name == suffix) { best = m; break; }
                    if (best == null && m.Name.EndsWith("." + suffix)) best = m;
                }
                return best?.Invoke(o, null);
            }
            catch { return null; }
        }

        private static object GetBySuffix(object o, string suffix)
        {
            if (o == null) return null;
            try
            {
                foreach (var p in o.GetType().GetProperties(F))
                    if ((p.Name == suffix || p.Name.EndsWith("." + suffix)) && p.CanRead) return p.GetValue(o);
            }
            catch { }
            return null;
        }

        /// <summary>Decrypt an ACTk ObscuredULong (or similar) to a plain value: implicit cast op, else ToString-parse.</summary>
        public static ulong ToUL(object o)
        {
            if (o == null) return 0;
            try
            {
                foreach (var m in o.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static))
                    if (m.Name == "op_Implicit" && m.ReturnType == typeof(ulong))
                        return (ulong)m.Invoke(null, new[] { o });
            }
            catch { }
            try { return ulong.TryParse(o.ToString(), out var u) ? u : 0; } catch { return 0; }
        }

        /// <summary>Enumerate the equipped-item uid collection (IReadOnlyCollection&lt;ObscuredULong&gt;)
        /// via a typed Il2CppInterop cast — reflection can't see its explicit interface members.</summary>
        public static IEnumerable<ulong> EnumerateUids(object uidsObj)
        {
            // C# `as`/`is` can't see Il2Cpp interface implementations — use Il2CppInterop TryCast,
            // which checks the actual Il2Cpp type.
            var baseObj = uidsObj as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            if (baseObj == null) yield break;
            var seq = baseObj.TryCast<Il2CppSystem.Collections.Generic.IEnumerable<CodeStage.AntiCheat.ObscuredTypes.ObscuredULong>>();
            if (seq == null) yield break;
            var e = seq.GetEnumerator();
            var ne = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)(object)e).TryCast<Il2CppSystem.Collections.IEnumerator>();
            int guard = 0;
            while (ne != null && guard++ < 256 && ne.MoveNext())
                yield return ToUL(e.Current);
        }

        /// <summary>Invoke a static method on an Il2Cpp interop type resolved by name (e.g. "ue+ti").</summary>
        public static object CallStatic(string typeName, string method, params object[] args)
        {
            try
            {
                var t = typeof(Hero).Assembly.GetType(typeName);
                if (t == null) return null;
                int n = args?.Length ?? 0;
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    if (m.Name == method && m.GetParameters().Length == n) return m.Invoke(null, args);
            }
            catch { }
            return null;
        }

        public static double ToD(object o) { try { return o == null ? 0 : Convert.ToDouble(o); } catch { return 0; } }
        public static int ToI(object o) { try { return o == null ? 0 : Convert.ToInt32(o); } catch { return 0; } }
        public static string Str(object o) { try { return o?.ToString() ?? ""; } catch { return ""; } }
    }

    /// <summary>Captures the current StageCache via a Harmony postfix on the stage-entry UI method,
    /// so we can read a stable "Act-StageNo" id. Registered from Plugin.</summary>
    internal static class StageProbe
    {
        private static object _lastStageCache;

        public static string ReadStageId(StageManager stage)
        {
            // preferred: the StageCache captured at stage entry
            string id = FromStageCache(_lastStageCache);
            return id;
        }

        private static string FromStageCache(object stageCache)
        {
            if (stageCache == null) return "";
            try
            {
                var info = Refl.Get(stageCache, "becp");   // StageInfoData
                if (info == null) return "";
                var act = Refl.Get(info, "Act");
                var no = Refl.Get(info, "StageNo");
                if (act == null || no == null) return "";
                return $"{Refl.ToI(act)}-{Refl.ToI(no)}";
            }
            catch { return ""; }
        }

        /// <summary>Try to register a postfix on UI_Stage.hqk(StageCache, bool) to capture the active stage.</summary>
        public static void TryHook(Harmony harmony)
        {
            try
            {
                var t = AccessTools.TypeByName("UI_Stage");
                if (t == null) { Plugin.Logger?.LogWarning("StageProbe: UI_Stage not found"); return; }
                MethodInfo target = null;
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    if (m.Name == "hqk") { target = m; break; }
                if (target == null) { Plugin.Logger?.LogWarning("StageProbe: UI_Stage.hqk not found"); return; }
                var post = new HarmonyMethod(typeof(StageProbe).GetMethod(nameof(Captured), BindingFlags.NonPublic | BindingFlags.Static));
                harmony.Patch(target, postfix: post);
                Plugin.Logger?.LogInfo("StageProbe: hooked UI_Stage.hqk");
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("StageProbe.TryHook: " + e.Message); }
        }

        private static void Captured(object[] __args)
        {
            try { if (__args != null && __args.Length > 0 && __args[0] != null) _lastStageCache = __args[0]; }
            catch { }
        }
    }

    /// <summary>Reads hero stats / gear / skills via reflection over the confirmed obfuscated member paths.
    /// Best-effort: fills what it can, logs diagnostics when Debug.LogSnapshot is on.</summary>
    internal static class HeroProbe
    {
        // friendly key per StatType int (subset shown in the compare panel)
        private static readonly (int stat, string key)[] StatMap =
        {
            (1, "attack"), (2, "aspd"), (3, "critrate"), (4, "critdmg"),
            (5, "hp"), (6, "armor"), (7, "mspd"),
        };
        // candidate "get final stat value" getters on the stat container (xe), in confidence order
        private static readonly string[] StatGetters = { "nsc", "kaq", "kar", "kap" };
        private static readonly string[] GearModSlots = { "bdzh", "bdzi", "bdzj", "bdzk", "bdzl" };
        private static readonly string[] SkillLevelGetters = { "jjy", "jjz", "jka", "jkb", "jke", "jkf", "jkg", "jkh", "jki", "jkm" };

        private static Type _statType;
        private static Type StatTypeT => _statType ?? (_statType = typeof(Hero).Assembly.GetType("TaskbarHero.StatType"));

        private static void Log(string s) => Plugin.Logger?.LogInfo("[snap diag] " + s);

        /// <summary>One-shot structural dump to identify the right gear/skill containers in-game.</summary>
        public static void Diagnose(Hero hero)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                Log("cache type = " + (cache?.GetType().FullName ?? "null"));
                // gear container candidates
                foreach (var name in new[] { "jhe", "jgr", "brrd", "brqt", "brrf", "brqr", "brqs" })
                {
                    var v = name.Length == 3 && (name[0] == 'j') ? Refl.Call(cache, name) : Refl.Get(cache, name);
                    if (v == null) v = Refl.Call(cache, name);
                    if (v == null) { continue; }
                    int cnt = -1; try { var c = Refl.Get(v, "Count") ?? Refl.Get(v, "Length"); if (c != null) cnt = Convert.ToInt32(c); } catch { }
                    object first = null; foreach (var it in Refl.Enumerate(v)) { first = it; break; }
                    Log($"cache.{name} -> {v.GetType().FullName} count={cnt} first={(first?.GetType().FullName ?? "-")}");
                    if (first != null)
                    {
                        var info = Refl.Get(first, "brke");
                        Log($"   first.brke={(info?.GetType().FullName ?? "null")} NameKey={Refl.Str(Refl.Get(info, "NameKey"))} ItemKey={Refl.ToI(Refl.Get(info, "ItemKey"))} GEARTYPE={Refl.Str(Refl.Get(info, "GEARTYPE"))}");
                    }
                }
                // skill list candidates
                foreach (var name in new[] { "bchl", "bchk" })
                {
                    var v = Refl.Get(hero, name);
                    if (v == null) { Log($"hero.{name} = null"); continue; }
                    int cnt = -1; try { var c = Refl.Get(v, "Count"); if (c != null) cnt = Convert.ToInt32(c); } catch { }
                    object first = null; foreach (var it in Refl.Enumerate(v)) { first = it; break; }
                    Log($"hero.{name} -> {v.GetType().FullName} count={cnt} first={(first?.GetType().FullName ?? (cnt < 0 ? v.GetType().Name : "-"))}");
                    var sk = first ?? (cnt < 0 ? v : null);
                    if (sk != null)
                    {
                        var sc = Refl.Get(sk, "skillCache");
                        var info = Refl.Get(sc, "behi");
                        Log($"   skillCache={(sc?.GetType().FullName ?? "null")} behi={(info?.GetType().FullName ?? "null")} NameKey={Refl.Str(Refl.Get(info, "SkillNameKey"))} SkillKey={Refl.ToI(Refl.Get(info, "SkillKey"))}");
                    }
                }
                // index-based skills via Unit.gsn(i)
                for (int i = 0; i < 8; i++)
                {
                    var sk = Refl.Call(hero, "gsn", i);
                    if (sk == null) { Log($"gsn({i}) = null"); continue; }
                    var sc = Refl.Get(sk, "skillCache");
                    var info = Refl.Get(sc, "behi");
                    int key = Refl.ToI(Refl.Get(info, "SkillKey"));
                    Log($"gsn({i}) -> {sk.GetType().Name} sc={(sc?.GetType().Name ?? "null")} SkillKey={key} NameKey={Refl.Str(Refl.Get(info, "SkillNameKey"))}");
                    if (sc != null && key != 0)
                        foreach (var g in SkillLevelMembers)
                            Log($"   lvl {g} = {ParseLevel(Refl.Get(sc, g) ?? Refl.Call(sc, g))}");
                }
                // list-field skill containers
                foreach (var fld in new[] { "bchd", "bche" })
                {
                    var v = Refl.Get(hero, fld);
                    if (v == null) { Log($"hero.{fld}=null"); continue; }
                    int cnt = -1; try { var c = Refl.Get(v, "Count"); if (c != null) cnt = Convert.ToInt32(c); } catch { }
                    object first = null; foreach (var it in Refl.Enumerate(v)) { first = it; break; }
                    Log($"hero.{fld} -> {v.GetType().FullName} count={cnt} first={(first?.GetType().FullName ?? "-")}");
                }
                // skill level probing on the real equipped list (bchd)
                foreach (var sk in Refl.Enumerate(Refl.Get(hero, "bchd")))
                {
                    var sc = Refl.Get(sk, "skillCache");
                    int key = Refl.ToI(Refl.Get(Refl.Get(sc, "behi"), "SkillKey"));
                    var sb = new System.Text.StringBuilder($"bchd skill key={key} levels:");
                    foreach (var g in SkillLevelMembers) sb.Append($" {g}={ParseLevel(Refl.Get(sc, g) ?? Refl.Call(sc, g))}");
                    Log(sb.ToString());
                    // also probe int getters on ActiveSkill itself
                    foreach (var g in new[] { "mes", "meu", "mex", "mfb", "mfd" }) Log($"   AS.{g}={ParseLevel(Refl.Call(sk, g))}");
                }
                // gear: step through the uid collection enumerator explicitly
                var cacheG = Refl.Get(hero, "cache");
                var uids = Refl.Call(cacheG, "jgr") ?? Refl.Get(cacheG, "brqt");
                int gi = 0;
                foreach (var uid in Refl.EnumerateUids(uids))
                {
                    object item = Refl.CallStatic("ue+ti", "opd", uid) ?? Refl.CallStatic("ue+ti", "ish", uid);
                    Log($"   uid={uid} opd->{(item?.GetType().Name ?? "null")} slot={Refl.Str(Refl.Call(item, "ips"))} name={Refl.Str(Refl.Get(Refl.Get(item, "brke"), "NameKey"))}");
                    if (++gi >= 4) break;
                }
            }
            catch (Exception e) { Log("Diagnose ex: " + e.Message); }
        }

        public static Hero FindHero()
        {
            try { return UnityEngine.Object.FindObjectOfType<Hero>(); }
            catch { return null; }
        }

        /// <summary>All party heroes currently in the scene.</summary>
        public static System.Collections.Generic.List<Hero> FindParty()
        {
            var list = new System.Collections.Generic.List<Hero>();
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<Hero>();
                if (arr != null) foreach (var h in arr) if (h != null) list.Add(h);
            }
            catch { }
            if (list.Count == 0) { var h = FindHero(); if (h != null) list.Add(h); }
            return list;
        }

        // candidate accessors for a stable per-character class/id (refined via RE + in-game)
        private static readonly string[] ClassMembers = { "befr" };

        /// <summary>Stable per-character id for matching across runs (class key / hero id).
        /// Best-effort until the exact obfuscated member is confirmed in-game.</summary>
        public static string ReadCharacterId(Hero hero)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                // ug.befr -> HeroInfoData; look for a class/character key on it
                var info = Refl.Get(cache, "befr");
                foreach (var k in new[] { "ClassKey", "HeroKey", "CharacterKey", "NameKey", "JOB", "CLASS" })
                {
                    var v = Refl.Get(info, k);
                    string s = Refl.Str(v);
                    if (!string.IsNullOrEmpty(s) && s != "0") return s;
                }
            }
            catch { }
            return "";
        }

        public static void ReadStats(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                var stats = Refl.Get(cache, "beib");          // xe
                if (stats == null) return;
                string getter = PickStatGetter(stats);
                if (getter == null) return;

                foreach (var (stat, key) in StatMap)
                {
                    var arg = EnumVal(stat);
                    if (arg == null) continue;
                    var v = Refl.Call(stats, getter, arg);
                    if (v != null) snap.Stats.Add(new StatEntry(key, Refl.ToD(v)));
                }

                if (Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value)
                {
                    var arg = EnumVal(5); // MaxHp
                    foreach (var g in StatGetters)
                        Plugin.Logger?.LogInfo($"[snap stat getter] {g}(MaxHp) = {Refl.ToD(Refl.Call(stats, g, arg))}");
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ReadStats: " + e.Message); }
        }

        private static string PickStatGetter(object stats)
        {
            var arg = EnumVal(5); // MaxHp should be > 0 for a live hero
            foreach (var g in StatGetters)
            {
                var v = Refl.Call(stats, g, arg);
                if (v != null && Refl.ToD(v) > 0) return g;
            }
            // fall back to the first that simply exists
            foreach (var g in StatGetters)
                if (Refl.Call(stats, g, arg) != null) return g;
            return null;
        }

        private static object EnumVal(int i)
        {
            try { return StatTypeT == null ? null : Enum.ToObject(StatTypeT, i); }
            catch { return null; }
        }

        private static readonly string[] ItemLookups = { "opd", "ish", "isl", "esx" };

        // KNOWN LIMITATION (verified in-game): the equipped-item uid collection
        // (ug.jgr() -> IReadOnlyCollection<ObscuredULong>) can't be enumerated via reflection
        // or Il2CppInterop TryCast — its interface members are explicit impls invisible to both.
        // This degrades to "no gear" safely; a typed-Il2CppInterop pass on `ug` is the follow-up.
        public static void ReadGear(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                // equipped items are referenced by uid (10 ObscuredULong) in jgr()/brqt — no indexer
                object uids = Refl.Call(cache, "jgr") ?? Refl.Get(cache, "brqt");
                if (uids == null) return;
                foreach (var uid in Refl.EnumerateUids(uids))
                {
                    try
                    {
                        if (uid == 0) continue;
                        object item = null;
                        foreach (var fn in ItemLookups) { item = Refl.CallStatic("ue+ti", fn, uid); if (item != null) break; }
                        if (item == null) continue;

                        var g = new GearItem();
                        var info = Refl.Get(item, "brke");          // ItemInfoData
                        g.Name = Refl.Str(Refl.Get(info, "NameKey"));
                        g.Slot = Refl.Str(Refl.Call(item, "ips"));  // EGearType
                        if (string.IsNullOrEmpty(g.Name)) g.Name = "item" + Refl.ToI(Refl.Get(info, "ItemKey"));

                        foreach (var slot in GearModSlots)
                        {
                            var mod = Refl.Get(item, slot);          // GearModData (struct)
                            if (mod == null) continue;
                            string st = Refl.Str(Refl.Call(mod, "iox"));
                            if (string.IsNullOrEmpty(st) || st == "NONE" || st == "None") continue;
                            double val = Refl.ToD(Refl.Call(mod, "ioy"));
                            g.Affixes.Add(new Affix(st, val));
                        }
                        string unique = Refl.Str(Refl.Call(item, "oky"));
                        if (!string.IsNullOrEmpty(unique) && unique != "None") g.Affixes.Add(new Affix(unique, 0));

                        if (!string.IsNullOrEmpty(g.Name)) snap.Equipment.Add(g);
                    }
                    catch { }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ReadGear: " + e.Message); }
        }

        // candidate level accessors on the skill cache (uo): int getters + ObscuredInt fields
        private static readonly string[] SkillLevelMembers =
        { "jjy", "jjz", "jka", "jkb", "jke", "jkf", "jkg", "jkh", "jki", "jkm", "behj", "behk", "behl", "behm" };

        public static void ReadSkills(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                var seen = new HashSet<int>();
                foreach (var sk in EquippedSkills(hero))
                {
                    try
                    {
                        var cache = Refl.Get(sk, "skillCache");      // uo
                        var info = Refl.Get(cache, "behi");          // SkillInfoData
                        int key = Refl.ToI(Refl.Get(info, "SkillKey"));
                        if (key == 0) continue;                      // skip empty/template slots
                        if (!seen.Add(key)) continue;                // de-dup across sources
                        string name = Refl.Str(Refl.Get(info, "SkillNameKey"));
                        if (string.IsNullOrEmpty(name)) name = "skill" + key;
                        snap.Skills.Add(new SkillEntry(name, ReadSkillLevel(sk, cache)));
                    }
                    catch { }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ReadSkills: " + e.Message); }
        }

        /// <summary>Yield candidate ActiveSkill instances from several holders (gsn slots, list fields, getters).</summary>
        private static IEnumerable<object> EquippedSkills(Hero hero)
        {
            for (int i = 0; i < 8; i++) { var sk = Refl.Call(hero, "gsn", i); if (sk != null) yield return sk; }
            foreach (var fld in new[] { "bchd", "bche" })
                foreach (var sk in Refl.Enumerate(Refl.Get(hero, fld))) if (sk != null) yield return sk;
            foreach (var g in new[] { "gsr", "gss" }) { var sk = Refl.Call(hero, g); if (sk != null) yield return sk; }
        }

        private static int ParseLevel(object v)
        {
            int i = Refl.ToI(v);
            if (i != 0) return i;
            try { if (v != null && int.TryParse(v.ToString(), out var p)) return p; } catch { }
            return 0;
        }

        /// <summary>Skill level: ActiveSkill.meu (total level, ≥1 for equipped) with fallbacks to the
        /// skillCache ObscuredInt (behk / jjz). Verified in-game: meu=1/13/5, behk/jjz=0/13/5.</summary>
        private static int ReadSkillLevel(object sk, object cache)
        {
            int lv = ParseLevel(Refl.Call(sk, "meu"));
            if (lv >= 1 && lv <= 99) return lv;
            foreach (var g in new[] { "behk", "jjz" })
            {
                int v = ParseLevel(Refl.Get(cache, g) ?? Refl.Call(cache, g));
                if (v >= 1 && v <= 99) return v;
            }
            return lv;
        }
    }
}

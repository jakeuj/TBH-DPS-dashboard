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

        /// <summary>Resolve a type by full name, first in the game assembly (where obfuscated
        /// types like "ue+ti" live), then across all loaded assemblies (for engine/package types
        /// such as Unity.Localization which are in their own interop assembly).</summary>
        public static Type FindType(string typeName)
        {
            try { var t = typeof(Hero).Assembly.GetType(typeName); if (t != null) return t; } catch { }
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                { try { var t = asm.GetType(typeName); if (t != null) return t; } catch { } }
            }
            catch { }
            return null;
        }

        /// <summary>Invoke a static method on an Il2Cpp interop type resolved by name (e.g. "ue+ti").</summary>
        public static object CallStatic(string typeName, string method, params object[] args)
        {
            try
            {
                var t = FindType(typeName);
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
            // the StageCache captured at stage entry (UI_Stage.hqk)
            return FromStageCache(_lastStageCache);
        }

        private static string FromStageCache(object cache)
        {
            if (cache == null) return "";
            try
            {
                // Resolve fields by TYPE, not obfuscated name — those names shift every game update
                // (brnn↔brno↔…). The label is the string field shaped "N-N" (language-invariant:
                // "關卡 1-1" / "Stage 1-1" / "ステージ 1-1"); the difficulty is the ESTAGEDIFFICULTY enum field.
                string actStage = "", diff = "";
                foreach (var p in cache.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                    if (string.IsNullOrEmpty(actStage) && p.PropertyType == typeof(string))
                    {
                        var got = ExtractActStage(p.GetValue(cache) as string);
                        if (!string.IsNullOrEmpty(got)) actStage = got;
                    }
                    else if (string.IsNullOrEmpty(diff) && p.PropertyType.Name == "ESTAGEDIFFICULTY")
                    {
                        try { diff = p.GetValue(cache)?.ToString() ?? ""; } catch { }
                    }
                }
                if (!string.IsNullOrEmpty(actStage))
                    return string.IsNullOrEmpty(diff) ? actStage : actStage + " " + diff;
                // legacy fallback: older builds exposed becp as a StageInfoData with Act/StageNo
                return FromStageInfo(Refl.Get(cache, "becp"));
            }
            catch { return ""; }
        }

        private static string ExtractActStage(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var m = System.Text.RegularExpressions.Regex.Match(s, @"(\d+)\s*-\s*(\d+)");
            return m.Success ? m.Groups[1].Value + "-" + m.Groups[2].Value : "";
        }

        private static string FromStageInfo(object info)
        {
            if (info == null) return "";
            try
            {
                var act = Refl.Get(info, "Act");
                var no = Refl.Get(info, "StageNo");
                if (act == null || no == null) return "";
                string id = $"{Refl.ToI(act)}-{Refl.ToI(no)}";
                string diff = Refl.Str(Refl.Get(info, "STAGEDIFFICULTY"));   // NORMAL/NIGHTMARE/HELL/TORMENT
                if (!string.IsNullOrEmpty(diff)) id += " " + diff;
                return id;
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
                // localization diagnostics: why are names resolving to English?
                try
                {
                    const string LS = "UnityEngine.Localization.Settings.LocalizationSettings";
                    var sel = Refl.CallStatic(LS, "get_SelectedLocale");
                    Log($"SelectedLocale = '{Refl.Str(sel)}' name='{Refl.Str(Refl.Get(sel, "LocaleName"))}' id='{Refl.Str(Refl.Get(sel, "Identifier"))}'");
                    var avail = Refl.CallStatic(LS, "get_AvailableLocales");
                    var locales = Refl.Get(avail, "Locales");
                    int li = 0;
                    foreach (var loc in Refl.Enumerate(locales))
                    { Log($"  locale[{li++}] = '{Refl.Str(loc)}' id='{Refl.Str(Refl.Get(loc, "Identifier"))}'"); if (li > 8) break; }
                    // try resolving a hero name + a skill name via gbs/gbu
                    var c0 = Refl.Get(hero, "cache"); var hi = Refl.Get(c0, "befr");
                    string hnk = Refl.Str(Refl.Get(hi, "HeroNameKey"));
                    Log($"HeroNameKey='{hnk}' gbs='{Refl.Str(Refl.CallStatic("nm", "gbs", hnk))}' gbu='{Refl.Str(Refl.CallStatic("nm", "gbu", hnk))}'");
                }
                catch (Exception le) { Log("loc diag ex: " + le.Message); }

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

        /// <summary>Resolve a localization key to the in-game display string via the game's
        /// Unity-Localization facade (nm.gbs). Falls back to the key if unavailable.</summary>
        private static bool Resolved(string s, string key)
            => !string.IsNullOrEmpty(s) && s != key && s.IndexOf("No translation", StringComparison.Ordinal) < 0;

        public static string GameLoc(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            // nm.gbu resolves in the game's CURRENT language (verified: HeroName_201 -> 遊俠);
            // nm.gbs returns English. Prefer gbu, fall back to gbs, then the raw key.
            try
            {
                string s = Refl.Str(Refl.CallStatic("nm", "gbu", key));
                if (Resolved(s, key)) return s;
                s = Refl.Str(Refl.CallStatic("nm", "gbs", key));
                if (Resolved(s, key)) return s;
            }
            catch { }
            return key;
        }

        // item names live in a non-default Localization table; try gbt(table, key) candidates.
        private static readonly string[] ItemTables = { "Item", "Items", "ItemTable", "ItemName", "Equipment", "Gear" };

        /// <summary>Localized skill name for a skill key (from the save's equippedSKillKey), via the
        /// localization facade. Empty if it can't be resolved (caller shows the key instead).</summary>
        public static string ResolveSkillName(int key)
        {
            if (key <= 0) return "";
            foreach (var fmt in new[] { "SkillName_" + key, "Skill_" + key, "SkillName" + key, "skill_" + key })
            {
                string s = GameLoc(fmt);
                if (Resolved(s, fmt)) return s;
            }
            return "";
        }

        // ---- rewards: gold / hero exp / boxes ----

        /// <summary>Gold currency key — confirmed from the save's currenySaveDatas (Key 100001 = gold).</summary>
        public const int GoldKey = 100001;

        public static long ReadGold(int key = GoldKey)
        {
            // ue+su.iko(key) -> sv holder; sv.iks() -> Int64 balance. Fall back to ikn(key).
            try
            {
                var sv = Refl.CallStatic("ue+su", "iko", key);
                if (sv != null) { long v = Convert.ToInt64(Refl.Call(sv, "iks") ?? 0L); if (v != 0) return v; }
                return Convert.ToInt64(Refl.CallStatic("ue+su", "ikn", key) ?? 0L);
            }
            catch { return 0; }
        }

        private static System.Reflection.MemberInfo _expMember;
        private static bool _expResolveTried;

        /// <summary>Cumulative hero exp. The obfuscated accessor (jgx/brqv/…) renames every update, so we
        /// AUTO-RESOLVE it by value: find the cache member whose value matches the save's HeroExp for this
        /// hero (live exp ≈ saved, since the save lags only by the current session's gains). Self-healing.</summary>
        public static double ReadHeroExp(Hero hero)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                if (cache == null) return double.NaN;
                if (_expMember != null) return ReadMemberD(cache, _expMember);
                if (_expResolveTried) return double.NaN;

                int hk = ReadHeroKey(hero);
                if (hk == 0 || !SaveGearReader.LastHeroExp.TryGetValue(hk, out double target) || target <= 0)
                    return double.NaN;   // no save value yet to match against; try again next call
                _expResolveTried = true;

                System.Reflection.MemberInfo best = null; double bestDiff = double.MaxValue;
                var t = cache.GetType();
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                    double v = SafeD(() => p.GetValue(cache));
                    if (Match(v, target) && Math.Abs(v - target) < bestDiff) { bestDiff = Math.Abs(v - target); best = p; }
                }
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.GetParameters().Length != 0 || m.Name.StartsWith("get_") || !IsNumericReturn(m.ReturnType)) continue;
                    double v = SafeD(() => m.Invoke(cache, null));
                    if (Match(v, target) && Math.Abs(v - target) < bestDiff) { bestDiff = Math.Abs(v - target); best = m; }
                }
                _expMember = best;
                Plugin.Logger?.LogInfo("[selfcheck] hero exp member -> " + (best != null ? best.Name : "MISSING") + " (target " + target + ")");
                return best != null ? ReadMemberD(cache, best) : double.NaN;
            }
            catch { return double.NaN; }
        }

        // live exp must be >= saved (exp only grows) and within ~1 session's gain of it
        private static bool Match(double v, double target) => v >= target * 0.999 && v <= target * 1.5;

        private static double SafeD(Func<object> f) { try { return Refl.ToD(f()); } catch { return double.NaN; } }

        // only invoke methods that return a number (or an ACTk Obscured numeric) — these are getters,
        // safe to call; avoids invoking arbitrary game logic that could have side effects.
        private static bool IsNumericReturn(Type rt)
        {
            if (rt == typeof(int) || rt == typeof(long) || rt == typeof(float) || rt == typeof(double)
                || rt == typeof(uint) || rt == typeof(ulong) || rt == typeof(short) || rt == typeof(ushort)) return true;
            return rt != null && rt.Name.StartsWith("Obscured", StringComparison.Ordinal);
        }

        private static double ReadMemberD(object obj, System.Reflection.MemberInfo m)
        {
            try
            {
                if (m is System.Reflection.PropertyInfo p) return Refl.ToD(p.GetValue(obj));
                if (m is System.Reflection.MethodInfo mi) return Refl.ToD(mi.Invoke(obj, null));
            }
            catch { }
            return double.NaN;
        }

        // EBoxType: NORMAL=0, BOSS=1, ACTBOSS=2
        public static readonly int[] BoxTypes = { 0, 1, 2 };
        public static readonly string[] BoxTypeNames = { "NORMAL", "BOSS", "ACTBOSS" };

        public static int ReadBoxCount(int boxType)
        {
            try { return Convert.ToInt32(Refl.CallStatic("ue+td", "jej", boxType) ?? 0); }
            catch { return 0; }
        }

        /// <summary>One-time diagnostic: confirm gold key, hero-exp getter, and box counts.</summary>
        public static void DiagRewards(Hero hero)
        {
            try
            {
                long sv100 = 0; try { var sv = Refl.CallStatic("ue+su", "iko", GoldKey); if (sv != null) sv100 = Convert.ToInt64(Refl.Call(sv, "iks") ?? 0L); } catch { }
                Plugin.Logger?.LogInfo($"[reward] gold key={GoldKey} iko.iks={sv100} ikn={Refl.Str(Refl.CallStatic("ue+su", "ikn", GoldKey))} ReadGold()={ReadGold()}");
                var cache = Refl.Get(hero, "cache");
                Plugin.Logger?.LogInfo($"[reward] heroExp jgx()={Refl.ToD(Refl.Call(cache, "jgx"))} brqz={Refl.ToD(Refl.Get(cache, "brqz"))}");
                for (int b = 0; b < 3; b++) Plugin.Logger?.LogInfo($"[reward] box jej({b})={ReadBoxCount(b)}");
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("DiagRewards: " + e.Message); }
        }

        /// <summary>Resolve an item template key to its localized display name via ue.ti.isr(itemKey)
        /// -> ItemInfoData.NameKey -> game localizer. Returns "" if it can't be resolved.</summary>
        // verified in-game: tf.ipp() / tf.brkr return the localized item name (e.g. 精英弓);
        // iqu() returns the owner class name and the others are keys/garbage, so don't use them.
        private static readonly string[] TfNameGetters = { "ipp", "brkr" };
        private static bool _itemNameDiagDone;

        public static string ResolveItemName(int itemKey, ulong uid)
        {
            try
            {
                // The live item (tf) carries the game-resolved localized display name in brkn
                // (brko is the raw NameKey, which the default localizer can't resolve after the game
                // update). Fetch tf by uid via hcb (was opd).
                if (uid != 0)
                {
                    object tf = Refl.CallStatic("ue+ti", "hcb", uid) ?? Refl.CallStatic("ue+ti", "isk", uid);
                    string s = Refl.Str(Refl.Get(tf, "brkn"));
                    if (!string.IsNullOrEmpty(s) && s.IndexOf("No translation", StringComparison.Ordinal) < 0 && !IsNumeric(s))
                        return s;
                }
                return "";
            }
            catch { return ""; }
        }

        private static bool IsNumeric(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>Fill the character's stable id (HeroInfoData.HeroKey) and localized display name
        /// (nm.gbs(HeroNameKey), falling back to the class enum).</summary>
        private static System.Reflection.PropertyInfo _classProp;
        private static bool _classResolved;

        /// <summary>The hero's class (EEquipClassType) read from its cache, resolved BY TYPE so the
        /// obfuscated property name (brql/…) renaming across game updates doesn't break it.
        /// Returns 0 (=All/unknown) on failure.</summary>
        public static int ReadClass(Hero hero)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                if (cache == null) return 0;
                if (!_classResolved)
                {
                    _classResolved = true;
                    foreach (var p in cache.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                        if (p.CanRead && p.GetIndexParameters().Length == 0 && p.PropertyType.Name == "EEquipClassType") { _classProp = p; break; }
                    Plugin.Logger?.LogInfo("[selfcheck] Hero cache class property -> " + (_classProp != null ? _classProp.Name : "MISSING"));
                }
                if (_classProp == null) return 0;
                return Convert.ToInt32(_classProp.GetValue(cache));
            }
            catch { return 0; }
        }

        /// <summary>Hero key derived from class: keys are class*100+1 (Knight1→101, Ranger2→201,
        /// Sorcerer3→301, Priest4→401, …). Robust to obfuscation; the save is keyed by this. 0 = unknown.</summary>
        public static int ReadHeroKey(Hero hero)
        {
            int cls = ReadClass(hero);
            return cls > 0 ? cls * 100 + 1 : 0;
        }

        public static void ReadIdentity(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                int heroKey = ReadHeroKey(hero);
                snap.Character = heroKey != 0 ? heroKey.ToString() : "";
                // name from the stable localization key (HeroName_<key>); follows the in-game language.
                if (heroKey != 0)
                {
                    string name = GameLoc("HeroName_" + heroKey);
                    if (!string.IsNullOrEmpty(name) && name != "HeroName_" + heroKey) snap.CharacterName = name;
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ReadIdentity: " + e.Message); }
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
                // The equipped-uid collection (IReadOnlyCollection<ObscuredULong>) resists reflection
                // enumeration. Try to TryCast it to a concrete Il2Cpp List we can index; log the real
                // runtime type + cast results so we can pin down the right path in-game.
                // The IReadOnlyCollection<ObscuredULong> can't be reflection-enumerated or cast to a
                // concrete List, but it DOES TryCast to the non-generic Il2Cpp IEnumerable (verified
                // in-game), whose enumerator we can drive. ue.ti.opd(uid) -> item (tf).
                object colObj = hero.cache.brqt;
                var col = colObj as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                var seq = col?.TryCast<Il2CppSystem.Collections.IEnumerable>();
                bool dbg0 = Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value;
                if (dbg0)
                {
                    int rc = -1; try { var c = Refl.Get(colObj, "Count"); if (c != null) rc = Convert.ToInt32(c); } catch { }
                    var e0 = seq != null ? seq.GetEnumerator() : null;
                    bool mv0 = false; object cur0 = null;
                    try { if (e0 != null) { mv0 = e0.MoveNext(); cur0 = e0.Current; } } catch (Exception ex0) { Plugin.Logger?.LogInfo("[gear] enum ex: " + ex0.Message); }
                    Plugin.Logger?.LogInfo($"[gear] reflCount={rc} seqNN={seq != null} enumNN={e0 != null} firstMoveNext={mv0} cur0={(cur0 != null ? cur0.GetType().Name : "null")}");
                }
                if (seq == null) return;
                // Collect the raw Current objects FIRST, without converting. ObscuredULong (ACTk)
                // re-encrypts itself on ToString/cast, which writes back into the collection and
                // invalidates the enumerator ("Collection was modified"). So defer conversion +
                // ue.ti.opd() lookups until after enumeration completes.
                var raw = new System.Collections.Generic.List<object>();
                var en = seq.GetEnumerator();
                int guard = 0;
                try { while (en != null && guard++ < 64 && en.MoveNext()) raw.Add(en.Current); }
                catch { }   // if the game still mutates it, use whatever we gathered
                bool dbg = Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value;
                if (dbg) Plugin.Logger?.LogInfo($"[gear] raw uid count = {raw.Count}");
                int gdiag = 0;
                foreach (var rawUid in raw)
                {
                    try
                    {
                        ulong uid = Refl.ToUL(rawUid);
                        object item = Refl.CallStatic("ue+ti", "opd", uid) ?? Refl.CallStatic("ue+ti", "ish", uid);
                        if (dbg && gdiag++ < 4)
                            Plugin.Logger?.LogInfo($"[gear] rawType={rawUid?.GetType().Name} rawStr='{Refl.Str(rawUid)}' uid={uid} opd={(item != null ? item.GetType().Name : "null")} name={(item != null ? Refl.Str(Refl.Get(Refl.Get(item, "brke"), "NameKey")) : "")}");
                        if (uid == 0) continue;
                        if (item == null) continue;

                        var g = new GearItem();
                        var info = Refl.Get(item, "brke");          // ItemInfoData
                        string nameKey = Refl.Str(Refl.Get(info, "NameKey"));
                        g.Name = GameLoc(nameKey);
                        g.Slot = Refl.Str(Refl.Call(item, "ips"));  // EGearType
                        if (string.IsNullOrEmpty(g.Name) || g.Name == nameKey)
                            g.Name = string.IsNullOrEmpty(nameKey) ? "item" + Refl.ToI(Refl.Get(info, "ItemKey")) : nameKey;

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

        private static System.Collections.Generic.List<System.Reflection.PropertyInfo> _skillDicts;
        private static bool _skillDictsResolved;

        /// <summary>Map of equipped skill key -> level, read from the Hero's Dictionary&lt;int,ActiveSkill&gt;
        /// maps. The dictionaries are resolved BY TYPE (value type = ActiveSkill) so obfuscation renames
        /// don't break it; the dict KEY is the skill key, and ActiveSkill.meu() (with int-method fallbacks)
        /// gives the level. Returns an empty map on failure (skills then show without a level).</summary>
        public static System.Collections.Generic.Dictionary<int, int> ReadSkillLevels(Hero hero)
        {
            var map = new System.Collections.Generic.Dictionary<int, int>();
            try
            {
                if (!_skillDictsResolved)
                {
                    _skillDictsResolved = true;
                    _skillDicts = new System.Collections.Generic.List<System.Reflection.PropertyInfo>();
                    foreach (var p in typeof(Hero).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!p.CanRead || p.PropertyType.Name != "Dictionary`2") continue;
                        var ga = p.PropertyType.GetGenericArguments();
                        // active skills (ActiveSkill) AND buffs/heals (ContinuousSkill/RangeBuffSkill) —
                        // match any Dictionary<int, *Skill> so all equipped skill types get a level.
                        if (ga.Length == 2 && ga[1].Name.EndsWith("Skill", StringComparison.Ordinal)) _skillDicts.Add(p);
                    }
                    Plugin.Logger?.LogInfo("[selfcheck] Hero ActiveSkill dicts -> " + _skillDicts.Count);
                }
                foreach (var p in _skillDicts)
                {
                    object dict; try { dict = p.GetValue(hero); } catch { continue; }
                    foreach (var kvp in Refl.EnumerateE(dict))
                    {
                        int key = Refl.ToI(Refl.Get(kvp, "Key"));
                        int lv = SkillLevelOf(Refl.Get(kvp, "Value"));
                        if (key > 0 && lv > 0) map[key] = lv;
                    }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ReadSkillLevels: " + e.Message); }
            return map;
        }

        private static int SkillLevelOf(object activeSkill)
        {
            if (activeSkill == null) return 0;
            // meu = ActiveSkill level; lxd = ContinuousSkill/RangeBuffSkill (buff/heal) level
            foreach (var m in new[] { "meu", "lxd", "mes", "mex", "mfb", "mfd" })
            {
                int v = Refl.ToI(Refl.Call(activeSkill, m));
                if (v >= 1 && v <= 99) return v;
            }
            return 0;
        }

        public static void ReadSkills(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                // post game-update: active skills live in hero.bche (Dictionary<int, ActiveSkill>);
                // continuous skills in hero.bchd. Each skill's skillCache (uo) exposes the localized
                // name (brrl), key (brrn) and level (brro) directly.
                var seen = new HashSet<int>();
                foreach (var holder in new[] { "bche", "bchd" })
                {
                    object src = Refl.Get(hero, holder);
                    var seq = Refl.Get(src, "Values") ?? src;   // bche is a dict (use Values); bchd is a list
                    foreach (var sk in Refl.EnumerateE(seq))
                    {
                        try
                        {
                            var skc = Refl.Get(sk, "skillCache");   // uo
                            if (skc == null) continue;
                            int key = Refl.ToI(Refl.Get(skc, "brrn"));
                            if (key == 0 || !seen.Add(key)) continue;
                            string name = Refl.Str(Refl.Get(skc, "brrl"));   // localized display name
                            // basic-attack skills (20001/30001/40001) have no display name — skip them.
                            if (string.IsNullOrEmpty(name)) continue;
                            snap.Skills.Add(new SkillEntry(name, Refl.ToI(Refl.Get(skc, "brro")), key));
                        }
                        catch { }
                    }
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

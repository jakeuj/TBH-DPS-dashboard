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

        private static string _liveMember;   // cached StageManager member that yields the stage id

        private static bool _stageDumped;

        public static string ReadStageId(StageManager stage)
        {
            // preferred: the StageCache captured at stage entry (UI_Stage.hqk)
            string id = FromStageCache(_lastStageCache);
            if (!string.IsNullOrEmpty(id)) return id;
            // fallback: the entry hook doesn't fire when you replay/farm a stage, so read the
            // stage straight off the live StageManager instead (auto-discovered + cached member).
            id = FromLiveStage(stage);
            if (string.IsNullOrEmpty(id)) DumpStageOnce(stage);
            return id;
        }

        /// <summary>One-time diagnostic: log the StageManager's members + value types so we can
        /// find where the current-stage info actually lives.</summary>
        private static void DumpStageOnce(object stage)
        {
            if (_stageDumped) return;
            _stageDumped = true;
            try
            {
                Plugin.Logger?.LogInfo($"[stage dump] lastCache={(_lastStageCache != null)} stage={(stage != null ? stage.GetType().FullName : "null")}");
                if (stage == null) return;
                const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var t = stage.GetType();
                foreach (var f in t.GetFields(F))
                {
                    object v = null; try { v = f.GetValue(stage); } catch { }
                    Plugin.Logger?.LogInfo($"[stage dump] field {f.Name} : {f.FieldType.Name} = {(v != null ? v.GetType().Name : "null")}");
                }
                foreach (var p in t.GetProperties(F))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    object v = null; try { v = p.GetValue(stage); } catch { }
                    Plugin.Logger?.LogInfo($"[stage dump] prop  {p.Name} : {p.PropertyType.Name} = {(v != null ? v.GetType().Name : "null")}");
                }
                // methods that take or return a Stage/Cache/Info type — candidates to hook on stage start
                foreach (var m in t.GetMethods(F))
                {
                    var ps = m.GetParameters();
                    bool rel = NameLike(m.ReturnType.Name);
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < ps.Length; i++) { if (i > 0) sb.Append(','); sb.Append(ps[i].ParameterType.Name); if (NameLike(ps[i].ParameterType.Name)) rel = true; }
                    if (rel) Plugin.Logger?.LogInfo($"[stage dump] method {m.Name}({sb}) : {m.ReturnType.Name}");
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("[stage dump] ex: " + e.Message); }
        }

        private static bool NameLike(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("Stage", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Cache", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FromStageCache(object stageCache)
        {
            if (stageCache == null) return "";
            return FromStageInfo(Refl.Get(stageCache, "becp"));   // StageCache.becp -> StageInfoData
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

        // v may be a StageCache (has .becp) or a StageInfoData (has Act/StageNo) directly.
        private static string TryStageObj(object v)
        {
            if (v == null) return "";
            string s = FromStageCache(v);
            return !string.IsNullOrEmpty(s) ? s : FromStageInfo(v);
        }

        /// <summary>Scan the live StageManager for a member holding the current stage (cache or info).
        /// Fields first (no getter side effects), then properties. Caches the winning member name.</summary>
        private static string FromLiveStage(object stage)
        {
            if (stage == null) return "";
            const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            try
            {
                if (_liveMember != null)
                {
                    string s = TryStageObj(Refl.Get(stage, _liveMember));
                    if (!string.IsNullOrEmpty(s)) return s;
                    _liveMember = null;   // stale, re-scan
                }
                var t = stage.GetType();
                foreach (var f in t.GetFields(F))
                {
                    object v; try { v = f.GetValue(stage); } catch { continue; }
                    string s = TryStageObj(v);
                    if (!string.IsNullOrEmpty(s)) { _liveMember = f.Name; Plugin.Logger?.LogInfo($"[stage] live id from StageManager.{f.Name} = {s}"); return s; }
                }
                foreach (var p in t.GetProperties(F))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;
                    object v; try { v = p.GetValue(stage); } catch { continue; }
                    string s = TryStageObj(v);
                    if (!string.IsNullOrEmpty(s)) { _liveMember = p.Name; Plugin.Logger?.LogInfo($"[stage] live id from StageManager.{p.Name} = {s}"); return s; }
                }
            }
            catch { }
            return "";
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

                // DIAG: also hook StageManager.iat(Int32) (looks like "enter stage(index)") to see
                // whether it fires on farm-replay and what its argument is.
                var sm = AccessTools.TypeByName("TaskbarHero.StageManager") ?? AccessTools.TypeByName("StageManager");
                if (sm != null)
                {
                    foreach (var m in sm.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        var ps = m.GetParameters();
                        if (m.Name == "iat" && ps.Length == 1 && ps[0].ParameterType == typeof(int))
                        {
                            harmony.Patch(m, postfix: new HarmonyMethod(typeof(StageProbe).GetMethod(nameof(EnterDiag), BindingFlags.NonPublic | BindingFlags.Static)));
                            Plugin.Logger?.LogInfo("StageProbe: hooked StageManager.iat (diag)");
                            break;
                        }
                    }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("StageProbe.TryHook: " + e.Message); }
        }

        private static void Captured(object[] __args)
        {
            try
            {
                bool ok = __args != null && __args.Length > 0 && __args[0] != null;
                Plugin.Logger?.LogInfo($"[stage] hqk fired, arg0={(ok ? __args[0].GetType().Name : "null")}");
                if (ok) _lastStageCache = __args[0];
            }
            catch { }
        }

        private static void EnterDiag(object[] __args, object __result)
        {
            try { Plugin.Logger?.LogInfo($"[stage] iat({(__args != null && __args.Length > 0 ? Refl.Str(__args[0]) : "?")}) -> {Refl.Str(__result)}"); }
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

        public static double ReadHeroExp(Hero hero)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                if (cache == null) return double.NaN;
                // jgx() is the decrypted accessor. Do NOT fall back to the raw backing field brqz:
                // when jgx is momentarily null (scene transition), brqz returns the still-encrypted
                // value (~hundreds of millions of garbage) which corrupted per-run exp deltas.
                var v = Refl.Call(cache, "jgx");
                return v == null ? double.NaN : Refl.ToD(v);
            }
            catch { return double.NaN; }
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
                // The game displays item names from the live item object (tf). We have the plaintext
                // uid from the save, so fetch tf via opd(uid) (single lookup, no ACTk enumeration)
                // and try its name getters — these are what the inventory UI shows.
                object item = uid != 0 ? Refl.CallStatic("ue+ti", "opd", uid) : null;
                bool diag = !_itemNameDiagDone && Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value;
                if (item != null)
                {
                    if (diag)
                    {
                        _itemNameDiagDone = true;
                        var sb = new System.Text.StringBuilder($"[item] key={itemKey} tf gettters:");
                        foreach (var g in TfNameGetters) sb.Append($" {g}='{Refl.Str(Refl.Call(item, g) ?? Refl.Get(item, g))}'");
                        Plugin.Logger?.LogInfo(sb.ToString());
                    }
                    foreach (var g in TfNameGetters)
                    {
                        string s = Refl.Str(Refl.Call(item, g) ?? Refl.Get(item, g));
                        if (Resolved(s, "") && !IsNumeric(s)) return s;
                    }
                }
                // fall back: ItemInfoData.NameKey via localizer
                var info = Refl.CallStatic("ue+ti", "isr", itemKey);   // ItemInfoData
                string nameKey = Refl.Str(Refl.Get(info, "NameKey"));
                if (string.IsNullOrEmpty(nameKey)) return "";
                string name = GameLoc(nameKey);
                if (Resolved(name, nameKey)) return name;
                foreach (var tbl in ItemTables)
                {
                    string s = Refl.Str(Refl.CallStatic("nm", "gbt", tbl, nameKey));
                    if (Resolved(s, nameKey)) return s;
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
        public static void ReadIdentity(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                var info = Refl.Get(cache, "befr");               // HeroInfoData
                int heroKey = Refl.ToI(Refl.Get(info, "HeroKey"));
                snap.Character = heroKey != 0 ? heroKey.ToString() : Refl.Str(Refl.Get(info, "ClassType"));
                string nameKey = Refl.Str(Refl.Get(info, "HeroNameKey"));
                string name = GameLoc(nameKey);
                if (string.IsNullOrEmpty(name) || name == nameKey)
                    name = Refl.Str(Refl.Get(info, "ClassType"));  // e.g. "Priest"/"Hunter"
                snap.CharacterName = name;
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
                        string nameKey = Refl.Str(Refl.Get(info, "SkillNameKey"));
                        string name = GameLoc(nameKey);
                        // basic-attack skills (e.g. 20001/30001/40001) have no display name and aren't
                        // shown in the game's skill list — skip them.
                        if (string.IsNullOrEmpty(nameKey) || string.IsNullOrEmpty(name) || name == nameKey) continue;
                        snap.Skills.Add(new SkillEntry(name, ReadSkillLevel(sk, cache), key));
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

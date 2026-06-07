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

        public static Hero FindHero()
        {
            try
            {
                // prefer the hero list off StageManager if present, else any Hero in scene
                var hero = UnityEngine.Object.FindObjectOfType<Hero>();
                return hero;
            }
            catch { return null; }
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

        public static void ReadGear(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                var cache = Refl.Get(hero, "cache");
                object list = Refl.Call(cache, "jhe") ?? Refl.Get(cache, "brrd") ?? Refl.Call(cache, "jgr");
                if (list == null) return;
                foreach (var item in Refl.Enumerate(list))
                {
                    try
                    {
                        var g = new GearItem();
                        var info = Refl.Get(item, "brke");          // ItemInfoData
                        g.Name = Refl.Str(Refl.Get(info, "NameKey"));
                        g.Slot = Refl.Str(Refl.Get(info, "GEARTYPE"));
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

        public static void ReadSkills(Hero hero, CharacterSnapshot snap)
        {
            try
            {
                var list = Refl.Get(hero, "bchl");   // List<ActiveSkill> on Unit
                if (list == null) return;
                string levelGetter = null;
                foreach (var sk in Refl.Enumerate(list))
                {
                    try
                    {
                        var cache = Refl.Get(sk, "skillCache");      // uo
                        var info = Refl.Get(cache, "behi");          // SkillInfoData
                        string name = Refl.Str(Refl.Get(info, "SkillNameKey"));
                        if (string.IsNullOrEmpty(name)) name = "skill" + Refl.ToI(Refl.Get(info, "SkillKey"));

                        if (levelGetter == null) levelGetter = PickSkillLevelGetter(cache);
                        int lv = levelGetter != null ? Refl.ToI(Refl.Call(cache, levelGetter)) : 0;
                        snap.Skills.Add(new SkillEntry(name, lv));
                    }
                    catch { }
                }

                if (Plugin.DebugSnapshot != null && Plugin.DebugSnapshot.Value)
                {
                    foreach (var sk in Refl.Enumerate(list))
                    {
                        var cache = Refl.Get(sk, "skillCache");
                        foreach (var g in SkillLevelGetters)
                            Plugin.Logger?.LogInfo($"[snap skill getter] {g}() = {Refl.ToI(Refl.Call(cache, g))}");
                        break;
                    }
                }
            }
            catch (Exception e) { Plugin.Logger?.LogWarning("ReadSkills: " + e.Message); }
        }

        private static string PickSkillLevelGetter(object cache)
        {
            // a real skill level is a small positive number; pick the first getter that returns 1..50
            foreach (var g in SkillLevelGetters)
            {
                var v = Refl.Call(cache, g);
                int i = Refl.ToI(v);
                if (i >= 1 && i <= 50) return g;
            }
            return SkillLevelGetters[0];
        }
    }
}

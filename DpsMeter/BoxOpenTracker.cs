using System;
using System.Reflection;
using HarmonyLib;

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
                else Plugin.Logger?.LogWarning("[boxopen] StageBox/EBoxType type not found");
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
        private static void OpenPrefix(TaskbarHero.EBoxType __0)
        {
            int v = (int)__0;
            _openingKind = (v >= 0 && v <= 2) ? v : (int)BoxKind.Unknown;
        }

        // __0 = item name, __1 = EGradeType (int-compatible 0..9)
        private static void CtorPostfix(string __0, TaskbarHero.Data.EGradeType __1)
        {
            try
            {
                string stage = ""; try { stage = CharacterReader.CurrentStageId(); } catch { }
                Stats.Add(new BoxOpenEvent
                {
                    Time = DateTime.Now,
                    Grade = (int)__1,
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

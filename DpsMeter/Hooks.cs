using HarmonyLib;
using TaskbarHero;
using UnityEngine;

namespace TbhDpsMeter
{
    /// <summary>
    /// Intercepts damage dealt to monsters. Monster.ebj(DamageInfo, bool) is the
    /// virtual "take damage" entry on the Unit/Monster hierarchy (confirmed via
    /// metadata: Public_Virtual_Void_DamageInfo_Boolean). We only count hits whose
    /// Attacker is on the hero/player side (Unit.b_isHero).
    /// </summary>
    [HarmonyPatch(typeof(Monster), nameof(Monster.ebj))]
    internal static class Monster_TakeDamage_Patch
    {
        static void Postfix(Monster __instance, DamageInfo a, bool b)
        {
            try
            {
                var attacker = a.Attacker;
                if (attacker == null || !attacker.b_isHero) return;

                float amount = a.OriginDamage;
                if (amount <= 0f) return;

                bool crit = a.IsCritical;
                int type = (int)a.DamageType;

                Plugin.Tracker.Record(amount, crit, type, Time.time);

                if (Plugin.DebugDamage != null && Plugin.DebugDamage.Value)
                    Plugin.LogDamageSample(amount, crit, type);
            }
            catch { /* never let a hook crash the game */ }
        }
    }

    /// <summary>
    /// Intercepts damage dealt TO the player's heroes. Hero.ebj(DamageInfo, bool) is
    /// the same virtual "take damage" entry as Monster.ebj, inherited from the Unit
    /// hierarchy. We count a hit unless its Attacker is itself a hero (so hero-on-hero
    /// / self damage is excluded); environmental / null-attacker damage is counted.
    /// </summary>
    // NOTE: patching Hero.ebj (sibling override of Unit.ebj, same as Monster.ebj)
    // produced a broken MonoMod trampoline -> infinite recursion -> stack overflow
    // (0xc00000fd) when combined with the Monster.ebj patch. We instead hook Hero.gnr,
    // a distinct DamageInfo method, to avoid the conflicting detour.
    [HarmonyPatch(typeof(Hero), "gnr")]
    internal static class Hero_TakeDamage_Patch
    {
        static void Postfix(Hero __instance, DamageInfo a, bool b)
        {
            try
            {
                var attacker = a.Attacker;
                if (attacker != null && attacker.b_isHero) return;   // skip hero/self

                float amount = a.OriginDamage;
                if (amount <= 0f) return;

                bool crit = a.IsCritical;
                int type = (int)a.DamageType;
                int attr = (int)a.DamageAttribute;

                if (Plugin.DebugDamage != null && Plugin.DebugDamage.Value)
                    Plugin.Logger.LogInfo($"[taken gnr] amount={amount:0.#} crit={crit} type={type} attr={attr}");

                Plugin.TakenTracker.Record(amount, crit, type, attr, Time.time);
            }
            catch { /* never let a hook crash the game */ }
        }
    }

    /// <summary>
    /// Drives encounter boundaries off the stage state machine.
    /// EStageState: NONE=0, MONSTERSPAWN=1, BATTLE=2, REORGANIZATION=3.
    /// Each wave cycles MONSTERSPAWN -> BATTLE -> REORGANIZATION, so we reset the
    /// meter when a wave starts spawning and freeze it when reorganization begins.
    /// </summary>
    [HarmonyPatch(typeof(StageManager), "set_stageState")]
    internal static class StageState_Patch
    {
        // il2cpp property setter param is unnamed; bind positionally with __0.
        static void Postfix(EStageState __0)
        {
            try
            {
                // NOTE: this setter is not actually called by the game (the field is
                // assigned directly), so OverlayBehaviour.PollStageState drives the
                // boundaries. Kept for robustness, with matching NONE-only logic.
                if (__0 == EStageState.NONE)
                    Plugin.Tracker.StartEncounter(Time.time);
            }
            catch { }
        }
    }
}

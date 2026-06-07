using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    public enum Lang { ZhHant = 0, En = 1, Ja = 2, ZhHans = 3, Es = 4 }

    /// <summary>Tiny localization table for the overlay UI.
    /// zh-Hant / English / 日本語 / zh-Hans / Español.</summary>
    public static class Loc
    {
        public static Lang Current = Lang.ZhHant;

        public static void Init(string cfg)
        {
            switch ((cfg ?? "Auto").Trim().ToLowerInvariant())
            {
                case "zh": case "zh-hant": case "zh_tw": case "chinese": case "繁體中文": Current = Lang.ZhHant; break;
                case "zh-hans": case "zh_cn": case "zh-cn": case "simplified": case "简体中文": Current = Lang.ZhHans; break;
                case "en": case "english": Current = Lang.En; break;
                case "ja": case "jp": case "japanese": case "日本語": Current = Lang.Ja; break;
                case "es": case "spanish": case "español": case "espanol": Current = Lang.Es; break;
                default: Current = Detect(); break;
            }
        }

        private static Lang Detect()
        {
            try
            {
                switch (Application.systemLanguage)
                {
                    case SystemLanguage.Japanese: return Lang.Ja;
                    case SystemLanguage.ChineseSimplified: return Lang.ZhHans;
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseTraditional: return Lang.ZhHant;
                    case SystemLanguage.Spanish: return Lang.Es;
                    case SystemLanguage.English: return Lang.En;
                }
            }
            catch { }
            return Lang.En;
        }

        // key -> { zh-Hant, English, 日本語, zh-Hans, Español }
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "dps_title",      new[] { "TBH DPS", "TBH DPS", "TBH DPS", "TBH DPS", "TBH DPS" } },
            { "taken_title",    new[] { "受到傷害", "Damage Taken", "被ダメージ", "受到伤害", "Daño recibido" } },
            { "reset",          new[] { "重置", "Reset", "リセット", "重置", "Reiniciar" } },
            { "peak",           new[] { "峰值", "Peak", "ピーク", "峰值", "Pico" } },
            { "avg",            new[] { "平均", "Avg", "平均", "平均", "Prom." } },
            { "total_dealt",    new[] { "總傷", "Total", "合計", "总伤", "Total" } },
            { "total_taken",    new[] { "總承受", "Total", "総被ダメ", "总承受", "Total" } },
            { "duration",       new[] { "時長", "Time", "時間", "时长", "Tiempo" } },
            { "crit",           new[] { "暴擊", "Crit", "会心", "暴击", "Crít." } },
            { "crit_share",     new[] { "暴傷佔", "CritDmg", "会心割合", "暴伤占", "DañoCr" } },
            { "wave_short",     new[] { "波", "W", "波", "波", "Ol" } },
            { "review",         new[] { "回顧", "Review", "履歴", "回顾", "Histor." } },
            { "review_tag",     new[] { "平均", "avg", "平均", "平均", "prom" } },
            { "live_hint",      new[] { "即時統計（◀ 看歷史）", "Live  (◀ history)", "リアルタイム（◀ 履歴）", "实时统计（◀ 看历史）", "En vivo  (◀ historial)" } },
            { "review_hint",    new[] { "瀏覽存檔（▶ 回到即時）", "Saved run  (▶ live)", "保存記録（▶ 現在）", "浏览存档（▶ 回到实时）", "Guardado  (▶ en vivo)" } },
            { "per_sec_taken",  new[] { "每秒承受", "Taken/s", "被ダメ/秒", "每秒承受", "Recib./s" } },
            { "biggest_hit",    new[] { "最大單擊", "Biggest", "最大単発", "最大单击", "Máx." } },
            { "hits",           new[] { "受擊", "Hits", "被弾", "受击", "Golpes" } },
            { "incoming_crit",  new[] { "入站暴擊", "In.Crit", "被会心", "入站暴击", "Cr.recib" } },
            { "element_dist",   new[] { "元素分布", "Elements", "属性分布", "元素分布", "Elementos" } },
            // stage-compare panel
            { "compare_title",  new[] { "關卡比較", "Stage Compare", "ステージ比較", "关卡比较", "Comparar" } },
            { "baseline",       new[] { "基準", "Baseline", "基準", "基准", "Base" } },
            { "this_run",       new[] { "這場", "This", "この回", "这场", "Esta" } },
            { "set_baseline",   new[] { "設為基準", "Set base", "基準に設定", "设为基准", "Fijar base" } },
            { "pinned",         new[] { "已釘選", "Pinned", "固定中", "已钉选", "Fijado" } },
            { "active_time",    new[] { "有效輸出", "Active", "有効出力", "有效输出", "Activo" } },
            { "idle_time",      new[] { "停輸出", "Idle", "停止", "停输出", "Inactivo" } },
            { "per_wave",       new[] { "每波時間", "Wave times", "波別時間", "每波时间", "Por oleada" } },
            { "dmg_dist",       new[] { "傷害分配", "Damage", "ダメージ配分", "伤害分配", "Daño" } },
            { "gear_changes",   new[] { "裝備變更", "Gear", "装備変更", "装备变更", "Equipo" } },
            { "skill_changes",  new[] { "技能變更", "Skills", "スキル変更", "技能变更", "Habilidades" } },
            { "stat_changes",   new[] { "屬性", "Stats", "ステータス", "属性", "Atributos" } },
            { "no_runs",        new[] { "尚無紀錄", "No runs yet", "記録なし", "尚无记录", "Sin datos" } },
            { "uncategorized",  new[] { "未分類", "Other", "未分類", "未分类", "Otros" } },
            { "lv",             new[] { "Lv", "Lv", "Lv", "Lv", "Nv" } },
            { "total_time",     new[] { "總時長", "Total", "総時間", "总时长", "Total" } },
            // common stat keys (StatType names from RE; unknown keys fall back to the raw name)
            { "attack",         new[] { "攻擊", "Attack", "攻撃", "攻击", "Ataque" } },
            { "aspd",           new[] { "攻速", "AtkSpd", "攻速", "攻速", "Vel.Atq" } },
            { "critrate",       new[] { "暴擊率", "CritRate", "会心率", "暴击率", "Crít%" } },
            { "critdmg",        new[] { "暴傷", "CritDmg", "会心ダメ", "暴伤", "DañoCr" } },
            { "hp",             new[] { "生命", "HP", "HP", "生命", "Vida" } },
            // damage types (EDamageType)
            { "Melee",          new[] { "近戰", "Melee", "近接", "近战", "Melé" } },
            { "Projectile",     new[] { "投射", "Projectile", "投射", "投射", "Proyectil" } },
            { "AOE",            new[] { "範圍", "AOE", "範囲", "范围", "Área" } },
            { "Summon",         new[] { "召喚", "Summon", "召喚", "召唤", "Invoc." } },
            { "DOT",            new[] { "持續", "DoT", "継続", "持续", "DoT" } },
            { "Trap",           new[] { "陷阱", "Trap", "罠", "陷阱", "Trampa" } },
            { "None",           new[] { "無", "None", "なし", "无", "Ninguno" } },
            // damage attributes (EDamageAttribute)
            { "Physical",       new[] { "物理", "Physical", "物理", "物理", "Físico" } },
            { "Fire",           new[] { "火", "Fire", "炎", "火", "Fuego" } },
            { "Cold",           new[] { "冰", "Cold", "氷", "冰", "Frío" } },
            { "Lightning",      new[] { "雷", "Lightning", "雷", "雷", "Rayo" } },
            { "Chaos",          new[] { "混沌", "Chaos", "混沌", "混沌", "Caos" } },
            { "AllElement",     new[] { "全元素", "AllElem", "全属性", "全元素", "Todos" } },
        };

        /// <summary>Localized string for a key (falls back to zh-Hant, then the key).</summary>
        public static string G(string key)
        {
            if (Table.TryGetValue(key, out var a))
            {
                int i = (int)Current;
                if (i >= 0 && i < a.Length && !string.IsNullOrEmpty(a[i])) return a[i];
                return a[0];
            }
            return key;
        }

        /// <summary>Localize a (possibly "+"-combined) English type/attribute name.</summary>
        public static string Name(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;
            if (en.IndexOf('+') >= 0)
            {
                var parts = en.Split('+');
                for (int i = 0; i < parts.Length; i++) parts[i] = G(parts[i]);
                return string.Join("+", parts);
            }
            return G(en);
        }
    }
}

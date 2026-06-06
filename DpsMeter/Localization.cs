using System.Collections.Generic;
using UnityEngine;

namespace TbhDpsMeter
{
    public enum Lang { ZhHant = 0, En = 1, Ja = 2 }

    /// <summary>Tiny localization table for the overlay UI. zh-Hant / English / 日本語.</summary>
    public static class Loc
    {
        public static Lang Current = Lang.ZhHant;

        public static void Init(string cfg)
        {
            switch ((cfg ?? "Auto").Trim().ToLowerInvariant())
            {
                case "zh": case "zh-hant": case "zh_tw": case "chinese": case "繁體中文": Current = Lang.ZhHant; break;
                case "en": case "english": Current = Lang.En; break;
                case "ja": case "jp": case "japanese": case "日本語": Current = Lang.Ja; break;
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
                    case SystemLanguage.Chinese:
                    case SystemLanguage.ChineseTraditional:
                    case SystemLanguage.ChineseSimplified: return Lang.ZhHant;
                    case SystemLanguage.English: return Lang.En;
                }
            }
            catch { }
            return Lang.En;
        }

        // key -> { zh-Hant, English, 日本語 }
        private static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "dps_title",      new[] { "TBH DPS", "TBH DPS", "TBH DPS" } },
            { "taken_title",    new[] { "受到傷害", "Damage Taken", "被ダメージ" } },
            { "reset",          new[] { "重置", "Reset", "リセット" } },
            { "peak",           new[] { "峰值", "Peak", "ピーク" } },
            { "avg",            new[] { "平均", "Avg", "平均" } },
            { "total_dealt",    new[] { "總傷", "Total", "合計" } },
            { "total_taken",    new[] { "總承受", "Total", "総被ダメ" } },
            { "duration",       new[] { "時長", "Time", "時間" } },
            { "crit",           new[] { "暴擊", "Crit", "会心" } },
            { "crit_share",     new[] { "暴傷佔", "CritDmg", "会心割合" } },
            { "wave_short",     new[] { "波", "W", "波" } },
            { "review",         new[] { "回顧", "Review", "履歴" } },
            { "review_tag",     new[] { "平均", "avg", "平均" } },
            { "live_hint",      new[] { "即時統計（◀ 看歷史）", "Live  (◀ history)", "リアルタイム（◀ 履歴）" } },
            { "review_hint",    new[] { "瀏覽存檔（▶ 回到即時）", "Saved run  (▶ live)", "保存記録（▶ 現在）" } },
            { "per_sec_taken",  new[] { "每秒承受", "Taken/s", "被ダメ/秒" } },
            { "biggest_hit",    new[] { "最大單擊", "Biggest", "最大単発" } },
            { "hits",           new[] { "受擊", "Hits", "被弾" } },
            { "incoming_crit",  new[] { "入站暴擊", "In.Crit", "被会心" } },
            { "element_dist",   new[] { "元素分布", "Elements", "属性分布" } },
            // damage types (EDamageType)
            { "Melee",          new[] { "近戰", "Melee", "近接" } },
            { "Projectile",     new[] { "投射", "Projectile", "投射" } },
            { "AOE",            new[] { "範圍", "AOE", "範囲" } },
            { "Summon",         new[] { "召喚", "Summon", "召喚" } },
            { "DOT",            new[] { "持續", "DoT", "継続" } },
            { "Trap",           new[] { "陷阱", "Trap", "罠" } },
            { "None",           new[] { "無", "None", "なし" } },
            // damage attributes (EDamageAttribute)
            { "Physical",       new[] { "物理", "Physical", "物理" } },
            { "Fire",           new[] { "火", "Fire", "炎" } },
            { "Cold",           new[] { "冰", "Cold", "氷" } },
            { "Lightning",      new[] { "雷", "Lightning", "雷" } },
            { "Chaos",          new[] { "混沌", "Chaos", "混沌" } },
            { "AllElement",     new[] { "全元素", "AllElem", "全属性" } },
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

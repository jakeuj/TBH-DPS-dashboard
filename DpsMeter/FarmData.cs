using System.Collections.Generic;

namespace TbhDpsMeter
{
    /// <summary>One stage's static data, mirrored from the wiki's farm_stages.json.
    /// `ExpectedEXP` is PER HERO (a 3-hero party earns 3×). Pure data — no engine deps.</summary>
    public sealed class FarmStage
    {
        public int Key;                 // e.g. 1101
        public string Label;            // "1-1"
        public int Act;
        public int StageNo;
        public int Level;
        public string Difficulty;       // NORMAL / NIGHTMARE / HELL / TORMENT
        public Dictionary<string, string> Names = new Dictionary<string, string>();  // langCode -> name
        public int Waves;
        public int MonsterTypes;
        public int Count;
        public double TotalHP;
        public double ExpectedGold;
        public double ExpectedEXP;      // per hero
        public double GoldPerHP;
        public double ExpPerHP;

        /// <summary>Stable id matching RunRecord.StageId, e.g. "2-4 HELL".</summary>
        public string StageId => Label + " " + Difficulty;

        public string LocalizedName(string langCode)
        {
            if (langCode != null && Names.TryGetValue(langCode, out var n) && !string.IsNullOrEmpty(n)) return n;
            if (Names.TryGetValue("en-US", out var en) && !string.IsNullOrEmpty(en)) return en;
            return Label;
        }
    }

    /// <summary>Parses the wiki's farm_stages.json (an array of stage objects) using the
    /// dependency-free <see cref="Json"/> parser. Pure C#, unit-tested.</summary>
    public static class FarmDataLoader
    {
        public static List<FarmStage> Parse(string json)
        {
            var list = new List<FarmStage>();
            var root = Json.Arr(Json.Parse(json));
            if (root == null) return list;
            foreach (var item in root)
            {
                var o = Json.Obj(item);
                if (o == null) continue;
                var s = new FarmStage
                {
                    Key = (int)Json.Long(Json.Get(o, "key")),
                    Label = Json.Str(Json.Get(o, "label")) ?? "",
                    Act = (int)Json.Long(Json.Get(o, "act")),
                    StageNo = (int)Json.Long(Json.Get(o, "stageNo")),
                    Level = (int)Json.Long(Json.Get(o, "level")),
                    Difficulty = Json.Str(Json.Get(o, "difficulty")) ?? "",
                    Waves = (int)Json.Long(Json.Get(o, "waves")),
                    MonsterTypes = (int)Json.Long(Json.Get(o, "monsterTypes")),
                    Count = (int)Json.Long(Json.Get(o, "count")),
                    TotalHP = Json.Num(Json.Get(o, "totalHP")),
                    ExpectedGold = Json.Num(Json.Get(o, "expectedGold")),
                    ExpectedEXP = Json.Num(Json.Get(o, "expectedEXP")),
                    GoldPerHP = Json.Num(Json.Get(o, "goldPerHP")),
                    ExpPerHP = Json.Num(Json.Get(o, "expPerHP")),
                };
                var names = Json.Obj(Json.Get(o, "name"));
                if (names != null)
                    foreach (var kv in names) s.Names[kv.Key] = Json.Str(kv.Value) ?? "";
                list.Add(s);
            }
            return list;
        }
    }
}

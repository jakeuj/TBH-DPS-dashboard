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

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TbhDpsMeter
{
    /// <summary>Tiny dependency-free JSON parser (object→Dictionary&lt;string,object&gt;, array→List&lt;object&gt;,
    /// string→string, number→double, true/false→bool, null→null). Enough to read the decrypted ES3 save.
    /// Pure C#, unit-tested in TrackerTests.</summary>
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            var v = ParseValue(s, ref i);
            return v;
        }

        // typed helpers
        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;
        public static string Str(object o) => o as string;
        public static double Num(object o) => o is double d ? d : (o is long l ? l : 0);
        public static long Long(object o) => o is long l ? l : (o is double d ? (long)d : 0);

        public static object Get(object o, string key)
        {
            var m = o as Dictionary<string, object>;
            if (m != null && m.TryGetValue(key, out var v)) return v;
            return null;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                else break;
            }
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) return null;
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': i += 4; return true;     // true
                case 'f': i += 5; return false;    // false
                case 'n': i += 4; return null;     // null
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var m = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return m; }
            while (i < s.Length)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                var val = ParseValue(s, ref i);
                m[key] = val;
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return m;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (i < s.Length)
            {
                var val = ParseValue(s, ref i);
                list.Add(val);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            if (i < s.Length && s[i] == '"') i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            string tok = s.Substring(start, i - start);
            // preserve 64-bit integer precision (item uids are huge longs that don't fit in double)
            if (tok.IndexOf('.') < 0 && tok.IndexOf('e') < 0 && tok.IndexOf('E') < 0
                && long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                return l;
            double.TryParse(tok, NumberStyles.Any, CultureInfo.InvariantCulture, out var d);
            return d;
        }
    }
}

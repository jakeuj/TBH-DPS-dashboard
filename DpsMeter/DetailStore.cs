using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TbhDpsMeter
{
    /// <summary>Per-item order book from the cron-built detail/&lt;slug&gt;.json on the data branch. Steam's
    /// order book is login-gated, so it comes via tbh-market through our pipeline. Fetched lazily per item
    /// when the price box is PINNED (one request per item, cached). slug = djb2(hash) in base36 — must match
    /// scripts/fetch-market.mjs and the web terminal. All network is async/off the game thread.</summary>
    public static class DetailStore
    {
        public struct Level { public int Price; public int Qty; }
        public sealed class OB
        {
            public int LowSell = -1, HighBuy = -1;
            public Level[] Sell = new Level[0];   // ascending price (Sell[0] = lowest ask)
            public Level[] Buy = new Level[0];     // descending price (Buy[0] = highest bid)
            public int MaxQty = 1;
        }

        private static readonly Dictionary<string, OB> _cache =   // hash -> OB (null entry = fetched, none)
            new Dictionary<string, OB>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _inflight =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        private const string Base = "https://raw.githubusercontent.com/WarmBed/TBH-DPS-dashboard/data/detail/";
        private const string BaseFallback = "https://cdn.jsdelivr.net/gh/WarmBed/TBH-DPS-dashboard@data/detail/";
        private const string Ua = "TBH-DpsMeter-Detail";

        /// <summary>djb2 hash of the Steam hash_name in base36 (matches the pipeline's slug()).</summary>
        public static string Slug(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0";
            uint h = 5381;
            foreach (char c in s) h = (h << 5) + h + c;   // h*33 + c, wraps at uint32 (matches JS >>>0)
            if (h == 0) return "0";
            const string d = "0123456789abcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder();
            while (h > 0) { sb.Insert(0, d[(int)(h % 36)]); h /= 36; }
            return sb.ToString();
        }

        /// <summary>Order book for a Steam hash_name, or null if not fetched yet / unavailable.</summary>
        public static OB Get(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return null;
            lock (_lock) return _cache.TryGetValue(hash, out var ob) ? ob : null;
        }

        /// <summary>Kick off a one-time async fetch for this item's detail. Safe to call every frame.</summary>
        public static void Request(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return;
            lock (_lock)
            {
                if (_cache.ContainsKey(hash) || _inflight.Contains(hash)) return;
                _inflight.Add(hash);
            }
            string slug = Slug(hash);
            Task.Run(async () =>
            {
                OB ob = null;
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        http.DefaultRequestHeaders.Add("User-Agent", Ua);
                        string json;
                        try { json = await http.GetStringAsync(Base + slug + ".json"); }
                        catch { json = await http.GetStringAsync(BaseFallback + slug + ".json"); }
                        var obj = Json.Obj(Json.Get(Json.Parse(json), "orderbook"));
                        if (obj != null) ob = ParseOB(obj);
                    }
                }
                catch (Exception e) { Plugin.Logger?.LogWarning("[detail] " + hash + ": " + e.Message); }
                lock (_lock) { _cache[hash] = ob; _inflight.Remove(hash); }
            });
        }

        private static OB ParseOB(Dictionary<string, object> obj)
        {
            var ob = new OB();
            ob.LowSell = (int)Json.Long(Json.Get(obj, "lowSell"));
            ob.HighBuy = (int)Json.Long(Json.Get(obj, "highBuy"));
            ob.Sell = ParseLevels(Json.Arr(Json.Get(obj, "sell")));
            ob.Buy = ParseLevels(Json.Arr(Json.Get(obj, "buy")));
            int mx = 1;
            foreach (var l in ob.Sell) if (l.Qty > mx) mx = l.Qty;
            foreach (var l in ob.Buy) if (l.Qty > mx) mx = l.Qty;
            ob.MaxQty = mx;
            return ob;
        }

        private static Level[] ParseLevels(List<object> arr)
        {
            if (arr == null) return new Level[0];
            var list = new List<Level>(arr.Count);
            foreach (var e in arr)
            {
                var lv = Json.Obj(e);
                if (lv == null) continue;
                list.Add(new Level { Price = (int)Json.Long(Json.Get(lv, "price")), Qty = (int)Json.Long(Json.Get(lv, "qty")) });
            }
            return list.ToArray();
        }
    }
}

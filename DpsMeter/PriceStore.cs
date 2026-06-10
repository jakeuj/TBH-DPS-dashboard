using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace TbhDpsMeter
{
    /// <summary>Steam Community Market price snapshot for the game's items, fetched lazily from our
    /// cron-built prices.json on jsDelivr. Keyed by the Steam market hash_name (e.g. "Emerald" for
    /// materials, "Chain Boots (Legendary) A" for gear). All network is async/off the game thread and
    /// degrades silently on failure. Loaded once per session; call EnsureLoaded() before lookups.</summary>
    public static class PriceStore
    {
        public enum St { Idle, Loading, Ready, Error }
        public static volatile St State = St.Idle;
        public static string Currency = "$";
        public static long CachedAtMs;   // ms since unix epoch, from prices.json

        /// <summary>Per-item market data from prices.json.</summary>
        public sealed class Info
        {
            public int Cents;          // current lowest sell price (cents)
            public int Qty;            // live listing count
            public int PrevCents = -1; // price ~24h ago (-1 = unknown, before 24h of cron history)
            public int[] Hist;         // sampled price series over the last 7 days (for the sparkline), or null
            public int Vol = -1;       // units sold in the last 24h (-1 = unknown; refreshed ~every 6h)
            public int MedianCents = -1; // median sale price in cents (-1 = unknown)
        }

        // hash_name -> Info. Case-insensitive.
        private static readonly Dictionary<string, Info> _items =
            new Dictionary<string, Info>(StringComparer.OrdinalIgnoreCase);

        private const string Url = "https://cdn.jsdelivr.net/gh/WarmBed/TBH-DPS-dashboard@data/prices.json";
        private const string Ua = "TBH-DpsMeter-Prices";

        /// <summary>Kick off the one-time async fetch. Safe to call every frame.</summary>
        public static void EnsureLoaded()
        {
            if (State != St.Idle) return;
            State = St.Loading;
            Task.Run(async () =>
            {
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                    {
                        http.DefaultRequestHeaders.Add("User-Agent", Ua);
                        string json = await http.GetStringAsync(Url);
                        var o = Json.Parse(json);
                        Currency = Json.Str(Json.Get(o, "currency")) ?? "$";
                        CachedAtMs = Json.Long(Json.Get(o, "cachedAt"));
                        var items = Json.Obj(Json.Get(o, "items"));
                        int n = 0;
                        if (items != null)
                            foreach (var kv in items)
                            {
                                var v = Json.Obj(kv.Value);
                                if (v == null) continue;
                                var info = new Info();
                                info.Cents = (int)Json.Long(Json.Get(v, "lowestCents"));
                                info.Qty = (int)Json.Long(Json.Get(v, "qty"));
                                var pcv = Json.Get(v, "prevCents");
                                info.PrevCents = pcv != null ? (int)Json.Long(pcv) : -1;
                                var volv = Json.Get(v, "vol");
                                info.Vol = volv != null ? (int)Json.Long(volv) : -1;
                                var medv = Json.Get(v, "medianCents");
                                info.MedianCents = medv != null ? (int)Json.Long(medv) : -1;
                                var harr = Json.Arr(Json.Get(v, "hist"));
                                if (harr != null && harr.Count > 0)
                                {
                                    info.Hist = new int[harr.Count];
                                    for (int i = 0; i < harr.Count; i++) info.Hist[i] = (int)Json.Long(harr[i]);
                                }
                                _items[kv.Key] = info;
                                n++;
                            }
                        State = St.Ready;
                        Plugin.Logger?.LogInfo($"[prices] loaded {n} items, currency='{Currency}'");
                    }
                }
                catch (Exception e) { State = St.Error; Plugin.Logger?.LogWarning("[prices] load failed: " + e.Message); }
            });
        }

        /// <summary>Lowest sell price in cents + listings count + price ~24h ago (prevCents = -1 if unknown,
        /// e.g. before the cron has 24h of history) for a Steam hash_name. False if the item isn't listed.</summary>
        public static bool TryGet(string hashName, out int cents, out int qty, out int prevCents)
        {
            cents = 0; qty = 0; prevCents = -1;
            if (string.IsNullOrEmpty(hashName)) return false;
            if (_items.TryGetValue(hashName, out var v)) { cents = v.Cents; qty = v.Qty; prevCents = v.PrevCents; return true; }
            return false;
        }

        /// <summary>The sampled 7-day price series (cents) for a hash_name, or null if none/too short.</summary>
        public static int[] History(string hashName)
        {
            if (string.IsNullOrEmpty(hashName)) return null;
            return _items.TryGetValue(hashName, out var v) ? v.Hist : null;
        }

        /// <summary>Full market record for a hash_name, or null if the item isn't listed.</summary>
        public static Info Get(string hashName)
            => (!string.IsNullOrEmpty(hashName) && _items.TryGetValue(hashName, out var v)) ? v : null;

        /// <summary>Format cents as a currency string, e.g. 1234 -> "$12.34".</summary>
        public static string Format(int cents)
        {
            return Currency + (cents / 100.0).ToString("0.00");
        }
    }
}

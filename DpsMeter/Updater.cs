using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace TbhDpsMeter
{
    /// <summary>Checks the GitHub repo for a newer release and (on user request) downloads the new DLL
    /// to a <c>.pending</c> file. The loaded DLL can't be replaced at runtime, so the preloader patcher
    /// (TBH.Updater.Patcher) applies the pending file on the next launch. All network is async/off the
    /// game thread and degrades silently on failure.</summary>
    public static class Updater
    {
        public enum St { Idle, Checking, Available, Downloading, Downloaded, Error }
        public static volatile St State = St.Idle;
        public static string LatestVersion = "";
        private static string _dllUrl = "";

        private const string Api = "https://api.github.com/repos/WarmBed/TBH-DPS-dashboard/releases/latest";
        private const string Ua = "TBH-DpsMeter-Updater";

        public static void CheckAsync()
        {
            if (State != St.Idle) return;
            State = St.Checking;
            Task.Run(async () =>
            {
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        http.DefaultRequestHeaders.Add("User-Agent", Ua);
                        string json = await http.GetStringAsync(Api);
                        var o = Json.Parse(json);
                        string ver = (Json.Str(Json.Get(o, "tag_name")) ?? "").TrimStart('v', 'V');
                        string url = "";
                        var assets = Json.Arr(Json.Get(o, "assets"));
                        if (assets != null)
                            foreach (var a in assets)
                                if (string.Equals(Json.Str(Json.Get(a, "name")), "TBH.DpsMeter.dll", StringComparison.OrdinalIgnoreCase))
                                { url = Json.Str(Json.Get(a, "browser_download_url")) ?? ""; break; }

                        if (!string.IsNullOrEmpty(ver) && !string.IsNullOrEmpty(url) && IsNewer(ver, Plugin.Version))
                        {
                            LatestVersion = ver; _dllUrl = url; State = St.Available;
                            Plugin.Logger?.LogInfo($"[updater] update available: v{ver}");
                        }
                        else { State = St.Idle; Plugin.Logger?.LogInfo($"[updater] up to date (latest v{ver})"); }
                    }
                }
                catch (Exception e) { State = St.Error; Plugin.Logger?.LogWarning("[updater] check failed: " + e.Message); }
            });
        }

        public static void DownloadAsync()
        {
            if (State != St.Available || string.IsNullOrEmpty(_dllUrl)) return;
            State = St.Downloading;
            Task.Run(async () =>
            {
                try
                {
                    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) })
                    {
                        http.DefaultRequestHeaders.Add("User-Agent", Ua);
                        byte[] bytes = await http.GetByteArrayAsync(_dllUrl);
                        if (bytes == null || bytes.Length < 50 * 1024) { State = St.Error; return; }
                        string pending = System.IO.Path.Combine(BepInEx.Paths.PluginPath, "TBH.DpsMeter.dll.pending");
                        System.IO.File.WriteAllBytes(pending, bytes);
                        State = St.Downloaded;
                        Plugin.Logger?.LogInfo($"[updater] downloaded {bytes.Length} bytes -> {pending} (restart to apply)");
                    }
                }
                catch (Exception e) { State = St.Error; Plugin.Logger?.LogWarning("[updater] download failed: " + e.Message); }
            });
        }

        /// <summary>True if dotted version <paramref name="latest"/> is greater than <paramref name="cur"/>.</summary>
        private static bool IsNewer(string latest, string cur)
        {
            try
            {
                var L = latest.Split('.'); var C = cur.Split('.');
                int n = Math.Max(L.Length, C.Length);
                for (int i = 0; i < n; i++)
                {
                    int li = (i < L.Length && int.TryParse(L[i], out var x)) ? x : 0;
                    int ci = (i < C.Length && int.TryParse(C[i], out var y)) ? y : 0;
                    if (li != ci) return li > ci;
                }
            }
            catch { }
            return false;
        }
    }
}

using System;
using System.IO;
using BepInEx;
using BepInEx.Preloader.Core.Patching;

namespace TbhDpsMeter.Updater
{
    /// <summary>Tiny preloader patcher: runs BEFORE plugins load, so it can apply a pending plugin
    /// update (downloaded by the running plugin last session) by swapping the file on disk — the
    /// loaded DLL is locked at runtime, so this is the only point a one-restart update can take effect.
    /// It patches nothing; Initialize() just does the file swap. Never throws.</summary>
    [PatcherPluginInfo("tbh.dpsmeter.updater", "TBH DPS Meter Updater", "1.0.0")]
    public class UpdaterPatcher : BasePatcher
    {
        public override void Initialize()
        {
            try
            {
                string plugins = Path.Combine(Paths.BepInExRootPath, "plugins");
                string pending = Path.Combine(plugins, "TBH.DpsMeter.dll.pending");
                string target = Path.Combine(plugins, "TBH.DpsMeter.dll");
                if (!File.Exists(pending)) return;
                if (new FileInfo(pending).Length < 50 * 1024)   // sanity: a real DLL is ~1 MB
                {
                    File.Delete(pending);
                    Log.LogWarning("pending update too small; discarded");
                    return;
                }
                File.Copy(pending, target, true);
                File.Delete(pending);
                Log.LogInfo("applied pending TBH.DpsMeter.dll update");
            }
            catch (Exception e) { Log.LogWarning("apply pending failed: " + e.Message); }
        }
    }
}

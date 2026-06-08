# Auto-update (notify + one-click) — Design

## Goal
The user's friend shouldn't have to manually re-download from GitHub after each fix. The plugin checks
for a newer release, shows a **notification in-panel**, and a one-click **download**; the update applies
on the **next game launch**. (Approach C of the discussion.)

## Hard constraint
A loaded DLL is locked — the running `TBH.DpsMeter.dll` can't be overwritten mid-session, and live
reload of a Harmony/IL2CPP plugin isn't viable. So: download to a `.pending` file, and a tiny BepInEx
**preloader patcher** applies it before plugins load on the next launch (one restart to apply).

## Components
1. **Updater** (`DpsMeter/Updater.cs`, in the plugin):
   - On Load (if `AutoCheckUpdate`, default ON), async GET
     `https://api.github.com/repos/WarmBed/TBH-DPS-dashboard/releases/latest` (HttpClient + a
     `User-Agent` header). Parse `tag_name` (e.g. `v0.5.6`) and the **bare DLL asset's**
     `browser_download_url` (asset named `TBH.DpsMeter.dll`).
   - Compare tag to `Plugin.Version`. State machine: Idle → Checking → UpdateAvailable → Downloading →
     Downloaded / Error. Thread-safe flags read by the UI.
   - `Download()`: fetch the DLL to `BepInEx/plugins/TBH.DpsMeter.dll.pending`; sanity-check size
     (> 50 KB) before keeping it. (Optional later: SHA from a `.sha256` asset.)
   - All network on a background Task; never blocks the game. Failures degrade silently to a log line.
2. **UI banner** (in `OverlayBehaviour`, the always-visible F9 panel): a one-line row at the top —
   `🔄 vX.Y.Z available [Download]` → `Downloading…` → `✅ Restart to apply`. The `[Download]` rect is
   hit-tested in the existing pointer handler.
3. **Patcher** (`Patcher/` project → `BepInEx/patchers/TBH.Updater.Patcher.dll`):
   `[PatcherPluginInfo(...)] class : BasePatcher`, override `Initialize()`: if
   `plugins/TBH.DpsMeter.dll.pending` exists, copy it over `plugins/TBH.DpsMeter.dll` and delete the
   pending. No `[TargetAssembly]` (we patch nothing). References BepInEx.Core + Preloader.Core.
4. **Config**: `General/AutoCheckUpdate` (bool, default true).

## Release-flow change
Each release must include a **bare `TBH.DpsMeter.dll`** asset (≈1 MB) so the updater downloads just the
DLL, not the 37 MB zip: `gh release upload vX.Y.Z DpsMeter/bin/Release/TBH.DpsMeter.dll`. The install
zip must also include `BepInEx/patchers/TBH.Updater.Patcher.dll` (one-time install for existing users).

## Security notes
- Only downloads the asset URL from **this repo's** latest release; verifies size (and ideally hash).
- Auto-running author-published code carries supply-chain risk if the repo/release is compromised —
  acceptable for an author-distributed mod, documented in the disclaimer.
- `AutoCheckUpdate` lets a user opt out of network access.

## Out of scope
- Zero-restart live reload (not feasible). Updating BepInEx itself (only the plugin DLL auto-updates).

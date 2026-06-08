---
name: tbh-game-update
description: Use when the TBH DPS Meter plugin breaks after a TaskBarHero game update — stage names garbled ("2-2 3204"), gear shows "item334111", skills/exp/stats blank, or panels stop auto-hiding. Step-by-step runbook to re-map the IL2CPP obfuscation churn and re-release.
---

# TBH DPS Meter — game-update recovery runbook

TaskBarHero is **IL2CPP + obfuscated**. Every game build randomizes **private member names**
(`bcka→bckb`, `brnn→brno`, `jgx→…`). Any code that resolves a member by a hard-coded name breaks.
Type names, enum names, and the **save file's JSON keys are NOT obfuscated** — those are our anchors.

## 0. Confirm it's an update break
- Game dir: `D:\SteamLibrary\steamapps\common\TaskbarHero`. Launch via Steam, never the exe
  (BepInEx loads through the `winhttp.dll` proxy only on a Steam launch):
  `Start-Process "steam://rungameid/3678970"` (PowerShell).
- Read `BepInEx/LogOutput.log` → grep `[selfcheck]`. Each resolver logs OK / a resolved name /
  MISSING. A `MISSING` (or a member that resolved to the wrong thing) pinpoints the break.
- Check a recent run file under `BepInEx/config/dpsmeter_runs/run_*.txt`: garbled `stageid`,
  `gear=item######`, missing `cexp=`/`skill=` levels, `exp=0` → that subsystem's accessor moved.

## 1. The robust resolution patterns (use these — don't hard-code names)
1. **Resolve by TYPE / enum** (stable). Find the property/method whose type is a readable enum:
   - menu tab: settable `EMainTab` property on `UIManager` (`GameUiState.CurrentTab`).
   - stage difficulty: the `ESTAGEDIFFICULTY` property on `ue+StageCache` (`HeroProbe.FromStageCache`).
   - hero class: the `EEquipClassType` property on `Hero.cache` → heroKey = class*100+1
     (Knight1→101, Ranger2→201, Sorcerer3→301, Priest4→401) (`HeroProbe.ReadClass/ReadHeroKey`).
   - skill level: `Dictionary<int, *Skill>` on `Hero` (key=skill key, value level via `meu()`/`lxd()`)
     (`HeroProbe.ReadSkillLevels`).
2. **Source from the decrypted save** (stable JSON keys). `SaveGearReader` reads `SaveFile_Live.es3`
   (AES-128-CBC, PBKDF2-SHA1, salt=IV=first16, 100 iters, password `emuMqG3bLYJ938ZDCfieWJ`):
   `heroKey`, `HeroLevel`, `HeroExp`, `equippedItemIds` (gear), `equippedSKillKey` (skills).
3. **Bundle wiki data** (offline, embedded resources): `item_names.json` (ItemKey→localized name,
   from wiki items.json), `farm_stages.json` (per-stage gold/exp/HP). `ItemNameStore`, `FarmDataStore`.
4. **Value-match** for an obfuscated numeric accessor: find the cache member whose value equals a known
   save value (e.g. exp ≈ save `HeroExp`) (`HeroProbe.ReadHeroExp`). Self-heals on rename.

## 2. Re-map what broke (inspector)
The inspector dumps the CURRENT interop layout (BepInEx regenerates interop on each game update):
```
cd inspector
dotnet build -c Release            # only if interop changed / first time
dotnet bin/Release/net8.0/inspector.dll type "TaskbarHero.UIManager"
dotnet bin/Release/net8.0/inspector.dll type "ue+StageCache"
dotnet bin/Release/net8.0/inspector.dll find Skill
```
Most fixes are already type/value-resolved (section 1) and need NO change. If a NEW thing broke:
- prefer adding a type/enum/value resolver over a new hard-coded name.
- if you must use a name, add it to the relevant resolver with a fallback + a `[selfcheck]` log line.

## 3. Remaining fragile spots (name-based Harmony hooks)
These hook methods by obfuscated NAME — if a hook's `Patched: ...` / `hooked ...` log line is missing
after an update, the method renamed; re-find it by signature in the inspector and update the name:
- `Monster.ebj(DamageInfo,bool)` — damage dealt (`Monster_TakeDamage_Patch`)
- `Hero.gnr(...)` — damage taken (`Hero_TakeDamage_Patch`)
- `StageManager.set_stageState` — wave/stage boundary
- `UI_Stage.hqk(StageCache,bool)` — captures the active stage (`StageProbe.TryHook`)

## 4. Build → deploy → verify (do every time; see [[deploy-restart-verify]])
```
dotnet run --project TrackerTests -c Release        # pure-C# tests (do NOT cover HeroProbe)
dotnet build DpsMeter/DpsMeter.csproj -c Release -nologo 2>&1 | grep -iE "建置成功|建置失敗|error CS"
```
- **CRITICAL**: confirm the build SUCCEEDED — `... | tail -2` hides compile errors and you'll deploy a
  STALE DLL (a "fix that doesn't work" but the code is on disk). Also confirm the output DLL's mtime is
  fresh. `F` is private to `Refl`; in HeroProbe use `BindingFlags.Public | BindingFlags.Instance`.
- Deploy + restart YOURSELF and verify it loaded:
```
taskkill //IM TaskBarHero.exe //F ; sleep 2
cp DpsMeter/bin/Release/TBH.DpsMeter.dll "$GAME/BepInEx/plugins/TBH.DpsMeter.dll"
# (PowerShell) Start-Process "steam://rungameid/3678970"
```
  then poll the log for a fresh `Overlays created` + clean `[selfcheck]` lines, and have the player
  clear ONE stage → check the new run file has correct stageid / gear names / skill levels / exp/cexp.

## 5. Release
- Bump `<Version>` in `DpsMeter/DpsMeter.csproj` AND `Plugin.cs` (keep them equal).
- Stage the DLL into `dist/TBH-DpsMeter/BepInEx/plugins/` (and the patcher into
  `dist/TBH-DpsMeter/BepInEx/patchers/` — `dotnet build Patcher/Patcher.csproj -c Release`), then
  `Compress-Archive dist/TBH-DpsMeter/* dist/TBH-DpsMeter-vX.Y.Z.zip`.
- Turn OFF `LogSnapshot` in the local game config before shipping (it's `false` by default in the bind).
- Commit, `git push origin main`, then create the release with **BOTH** assets — the zip AND the bare
  DLL (the auto-updater downloads the bare DLL, so it MUST be attached or in-game update breaks):
  `gh release create vX.Y.Z dist/TBH-DpsMeter-vX.Y.Z.zip DpsMeter/bin/Release/TBH.DpsMeter.dll --title ... --notes-file ...`
- Existing users auto-update via the in-panel prompt (Updater + TBH.Updater.Patcher); see
  `docs/superpowers/specs/2026-06-08-auto-update-design.md`.

## Memory pointers
[[tbh-wiki-farming-formulas]] · [[deploy-restart-verify]]

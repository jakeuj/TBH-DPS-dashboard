# TBH DPS Meter (TaskBarHero v1.00.09, Unity 6 IL2CPP)

In-game DPS overlay for **TaskBarHero**, built as a BepInEx 6 IL2CPP plugin.

## What it shows

**DPS panel (damage dealt):**
- **Live DPS** (5s sliding window) + **Peak** + **Average**
- **Total damage** + **encounter duration**
- **Damage-type breakdown** (近戰 / 投射 / 範圍 / 召喚 / 持續 / 陷阱, incl. combined flags)
- **Crit rate** + **crit damage share**

**Damage-taken panel (damage received, live-only):**
- **Live DTPS** (per-second damage taken) + **Peak** + **Average**
- **Total taken** + **duration** + **biggest single hit**
- **Hit count** + **incoming (monster) crit rate**
- **Two distribution bars**: element attribute (物理/火/冰/雷/混沌) and damage type
- Damage-taken stats are also folded into the same saved `RunRecord` as the DPS run
  (for future combined attack/defense review).

## Controls
- **F9** — show/hide the DPS panel (configurable: `ToggleKey`)
- **F10** — show/hide the damage-taken panel (configurable: `TakenUI.ToggleKey`)
- **Mouse drag** — move either panel (positions auto-saved independently)
- **PageUp / PageDown** — adjust background opacity live (shared by both panels)

## How it works
- Damage dealt: Harmony postfix on `TaskbarHero.Monster.ebj(DamageInfo, bool)`, filtered to
  player-side hits via `Unit.b_isHero`. Reads `OriginDamage`, `IsCritical`, `DamageType`.
- Damage taken: Harmony postfix on `TaskbarHero.Hero.ebj(DamageInfo, bool)` (same virtual,
  inherited from the Unit hierarchy), counting any hit whose attacker is **not** a hero.
  Reads `OriginDamage`, `IsCritical`, `DamageType`, `DamageAttribute`. DTPS math lives in
  `DamageTakenTracker` (pure C#, unit-tested alongside `DpsTracker`).
- Wave boundaries: polls `StageManager.stageState`
  (`MONSTERSPAWN → BATTLE → REORGANIZATION`). Resets stats each MONSTERSPAWN,
  freezes them at REORGANIZATION. (The property setter is never called by the game,
  so polling is used instead of a setter hook.)
- DPS math lives in `DpsTracker` (pure C#, unit-tested in `../TrackerTests`).

## Build & deploy
```
dotnet build DpsMeter/DpsMeter.csproj -c Release
copy DpsMeter\bin\Release\TBH.DpsMeter.dll  <Game>\BepInEx\plugins\
```
Restart the game **via Steam** (direct-exe launch does not inject the BepInEx
winhttp proxy on this Unity 6 build).

## Config
`<Game>\BepInEx\config\tbh.dpsmeter.cfg`

## Uninstall
Delete `TBH.DpsMeter.dll` from `BepInEx\plugins`. To remove BepInEx entirely,
delete `winhttp.dll`, `doorstop_config.ini`, the `dotnet` and `BepInEx` folders.

## Notes
- The game ships with Anti-Cheat Toolkit (ACTk). This plugin only *reads* damage
  events passively (no value tampering), and the game is single-player.

# Farming Planner (F6) — Design

## Goal
A new panel (hotkey **F6**) that ranks every stage by farming efficiency — **gold/sec** and
**exp/sec (per hero)** side by side — so the player can see which stage is most efficient to farm
*right now, for their own build*. This is the personalized counterpart to the wiki's
`tools/farming` page (which can only show theoretical reward-per-HP because it doesn't know your DPS).

## Key findings (validated against live data)
- The wiki ships a static dataset at `https://taskbarhero.wiki/data/farm_stages.json` — **108 stages**
  (27 stages × 4 difficulties: NORMAL / NIGHTMARE / HELL / TORMENT, across 3 acts).
- Each entry: `key, label, act, stageNo, level, difficulty, name{15 langs}, waves, perWave,
  monsterTypes, count, totalHP, expectedGold, expectedEXP, goldPerHP, expPerHP`.
- **`expectedEXP` is per-hero.** Measured party-sum = 3× (3-hero party).
- The player has a **constant personal multiplier ≈ 2.75×** vs the wiki baseline, and it is the
  **same for gold and per-hero exp, and stable across stages**:
  | stage | gold× | exp/hero× |
  |---|---|---|
  | 2-4 HELL | 2.77 | 2.74 |
  | 2-5 HELL | 2.76 | 2.71 |
- Therefore we can predict **all 108 stages'** real rewards from a few measured runs:
  `real = wiki × M`. No need to have farmed every stage.

## Data source decision: hybrid (measured-first + wiki fill)
- Stage you've cleared (clean run on record) → use **measured** gold/sec & exp/sec (ground truth).
- Stage not cleared → **estimate**: `real_gold = expectedGold × M_gold`, est clear time from your
  clear rate, then `gold/sec = real_gold / estClearSec`. Each row tagged 實測 / 估.

## Calibration (learned from the player's own runs)
- `M_gold = median(measured_gold / wiki.expectedGold)` over matched stages.
- `M_exp  = median(measured_expPerHero / wiki.expectedEXP)`, where
  `measured_expPerHero = run.ExpGained / partyCount`.
- `clearRate = median(wiki.totalHP / run.Duration)` over matched stages — HP cleared per second,
  which naturally folds in armor mitigation at that content tier **and** travel/idle time.
- **Outlier rejection**: drop any run whose ratio deviates wildly (> ~3×) from the median — this
  auto-discards mislabeled runs (e.g. the "1-1 NORMAL" run whose ratio was 2428×/34000×).

## Per-stage efficiency
For each wiki stage `s`, matched to measured runs by `(label, difficulty)`:
- Measured present → `clearSec = median(run.Duration)`, `gold/s = median(run.GoldGained)/clearSec`,
  `exp/s = median(run.ExpGained/partyCount)/clearSec`, source = Measured.
- Else → `clearSec = s.totalHP / clearRate`, `gold/s = (s.expectedGold × M_gold)/clearSec`,
  `exp/s = (s.expectedEXP × M_exp)/clearSec`, source = Estimated.
- If no calibration yet (no clean runs) → fall back to wiki's `goldPerHP`/`expPerHP` ordering and
  mark everything 估 (still gives a sane ranking out of the box).

## UI — F6 panel
- Sortable table: `關名(難度) ∣ 金幣/秒 ∣ 經驗/秒 ∣ 來源 ∣ 估清關秒 ∣ totalHP`.
- Click a column header to toggle the sort key (default: exp/sec desc).
- Difficulty filter chips (NORMAL/NIGHTMARE/HELL/TORMENT). **Default: show all 108.**
- Highlight the player's current stage row.
- Solid black background, GUI.depth consistent with the other panels, drag + clamp on-screen.
- Localized stage names via the `name` map keyed by current game language.

## Components
- `FarmData.cs` (pure C#): `FarmStage` model + `FarmDataLoader.Parse(json)` using existing `Json.cs`.
  `farm_stages.json` shipped as an **EmbeddedResource** inside the DLL (offline, zero extra files).
- `FarmPlanner.cs` (pure C#): calibration + per-stage `EfficiencyRow` computation + sorting.
  Fully unit-testable, no Unity/BepInEx deps.
- `FarmOverlayBehaviour.cs`: the F6 IMGUI panel.
- `Plugin.cs`: register panel, F6 hotkey, config toggle.
- `Localization.cs`: new keys (planner title, columns, source labels).
- **stageid reliability fix** (StageProbe/CharacterReader): robust read; reject empty/■0 runs from
  calibration. Also benefits F11 grouping.
- Tests in `TrackerTests` for `FarmPlanner` (calibration, outlier rejection, measured vs estimated,
  sorting) and `FarmDataLoader` (parse the real json).

## Out of scope (for now)
- Box drops (separate, parked task).
- Auto-refreshing the dataset from the wiki (ship a static snapshot; refresh manually on game patch).

# Damage-Taken Panel — Design

**Date:** 2026-06-06
**Plugin:** TBH DPS Meter (TaskBarHero v1.00.09, Unity 6 IL2CPP, BepInEx 6)

## Goal

Add a second, independent overlay panel that mirrors the DPS panel but tracks
**damage the player's heroes take** (incoming damage) instead of damage dealt.

## Decisions (from brainstorming)

- **Presentation:** independent second draggable panel, own toggle key (default F10). (Option A)
- **Metrics shown:** live/peak/avg DTPS, total taken + duration, biggest single hit,
  hit count + incoming (monster) crit rate, damage-taken curve graph, source distribution. (all)
- **Distribution dimension:** BOTH element attribute AND damage type — two bars. (Option C)
- **History/review:** taken data is saved into the **same** `RunRecord` as the DPS run
  (future combined attack/defense review), but the taken panel itself is **live-only** —
  no ◀▶ review UI for now. (Option C)
- **Tracker architecture:** new dedicated `DamageTakenTracker` mirroring `DpsTracker`;
  the proven `DpsTracker` is left untouched. (Approach ①)

## Data source (verified via inspector)

- Hook point: `TaskbarHero.Hero.ebj(DamageInfo, bool)` — the hero's take-damage entry
  (same virtual as `Monster.ebj`, inherited from the Unit hierarchy).
- `DamageInfo` exposes: `OriginDamage` (float), `IsCritical` (bool),
  `DamageType` (EDamageType, power-of-two flags), `DamageAttribute`
  (EDamageAttribute: Physical=0, Fire=1, Cold=2, Lightning=3, Chaos=4, AllElement=5, None=6),
  `Attacker` (Unit, has `b_isHero`).
- Filter: count a hit **unless** `attacker != null && attacker.b_isHero`
  (so hero-on-hero/self damage is excluded; environmental/null-attacker damage is counted).

## Components

### 1. `DamageTakenTracker` (new, pure C#, unit-tested)

Mirrors `DpsTracker`: sliding-window live DTPS, peak, total, duration, hits.
Adds:
- `_monsterCrits` → incoming crit rate (`IsCritical` share of inbound hits).
- `_biggestHit` → max single `OriginDamage`.
- Two breakdown dictionaries: `_byAttribute` (EDamageAttribute value 0..6) and
  `_byType` (EDamageType flag), each summed.
- `Record(float amount, bool isCritical, int typeFlag, int attribute, float now)`.
- `Snapshot` exposes all of the above + `ByAttribute` and `ByType` part lists
  (sorted largest-share first), reusing `DpsTracker.DecodeName` for type names and a
  new `DecodeAttribute(int)` for attribute names.

### 2. `Hero_TakeDamage_Patch` (new Harmony patch, in Hooks.cs)

`[HarmonyPatch(typeof(Hero), nameof(Hero.ebj))]` Postfix. Reads the DamageInfo fields,
applies the filter above, calls `Plugin.TakenTracker.Record(...)`. Wrapped in try/catch
(never crash the game). Honors the same DebugDamage logging gate (optional).

### 3. `TakenOverlayBehaviour` (new MonoBehaviour, live-only)

Independent draggable panel. Own toggle key + position config, shares `FontSize`.
Layout mirrors `OverlayBehaviour` but **without** the ◀▶ review row / saved-run nav:
- header (live DTPS) + Reset button (resets taken tracker only)
- 峰值 / 平均
- 總承受 / 時長 / 最大單擊
- 受擊次數 / 入站暴擊%
- DTPS curve graph (reuses the same drawing approach; wave markers from shared wave)
- two distribution bars: attribute bar + type bar
  - attribute colors: Physical=grey, Fire=orange-red, Cold=cyan, Lightning=yellow, Chaos=purple
  - type colors reuse `OverlayBehaviour.ColorForFlag` equivalents.

Clears its own graph history when it observes the shared wave counter reset to 0.

### 4. Stage boundary / wave (low-churn)

Keep `OverlayBehaviour.PollStageState` as the single poller. Additions:
- `Plugin.CurrentWave` (static int) written by `PollStageState`; the taken panel reads it.
- On the NONE boundary, also call `Plugin.TakenTracker.StartEncounter(now)`.
- `OverlayBehaviour.SaveCurrentRun` additionally reads the `TakenTracker` snapshot and
  writes `taken_*` fields into the same `RunRecord`.
- No `StageWatcher` extraction (YAGNI; future cleanup).

### 5. `RunRecord` / `RunStore` schema extension

`RunRecord` gains: `TakenTotal, TakenPeak, TakenAvg, TakenBiggestHit, TakenHits,
TakenCritRate` plus `TakenAttrValues/TakenAttrAmounts` and `TakenTypeFlags/TakenTypeAmounts`
lists. `RunStore.Save` writes new lines (`taken_total=`, `taken_peak=`, …,
`taken_attr=val:amt`, `taken_type=flag:amt`); `Parse` adds matching cases. Forward/backward
compatible — old files lack the keys and default to 0/empty; the existing DPS review path
is unaffected.

### 6. Config (new entries, `[TakenUI]` / reuse)

`TakenToggleKey` (default "F10"), `TakenPosX`, `TakenPosY`, `TakenPanelWidth`,
`TakenStartVisible`. `Opacity`, `FontSize`, `LiveWindowSeconds` are shared with the DPS panel.

### 7. Plugin wiring

Init `Plugin.TakenTracker = new DamageTakenTracker(WindowSeconds.Value)`; bind new config;
`harmony.PatchAll(typeof(Hero_TakeDamage_Patch))`; register + add `TakenOverlayBehaviour`
to a GameObject (its own, or the same overlay GO).

### 8. Tests (`TrackerTests`)

Add `DamageTakenTracker` cases: total taken, biggest hit, attribute breakdown, type
breakdown, incoming crit rate, live/peak DTPS over a fake clock, and reset-to-zero.

## Error handling

- Every hook body is try/catch (game stability).
- Tracker is single-threaded (main thread only), like `DpsTracker`.
- Null/zero amount hits are ignored.

## Out of scope (YAGNI)

- ◀▶ review UI on the taken panel.
- Per-hero or per-monster breakdown.
- `StageWatcher` shared component extraction.

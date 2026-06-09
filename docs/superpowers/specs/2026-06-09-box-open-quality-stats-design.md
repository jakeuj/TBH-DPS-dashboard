# 開箱品質統計（F4）+ F5 持久化 — 設計

日期：2026-06-09
狀態：已與使用者確認方向，待 spec review

## 目標

1. **F4 新面板**：統計「開箱開出的物品品質分佈」，依箱種（NORMAL / BOSS / ACTBOSS）拆分，並保留時序開箱紀錄。讓玩家用實測觀察值回答「Boss 箱是不是真的更容易開出高品質」。
2. **F5 持久化**：現有 F5 撿箱紀錄（`BoxTracker`）改為跨遊戲重啟保留資料（跟刷關效率/RunStore 一樣），總數與各關計數重啟後仍在。

兩者都是「**實測觀察值**」，不嘗試還原遊戲理論掉率（遊戲沒有公開掉落表）。

## 背景：遊戲 IL2CPP 介面（已用 inspector 驗證）

- `TaskbarHero.Log.BoxOpenLog`（可讀、非混淆型別名）— **開箱時每開出一件就建構一次**，ctor 簽章為 `(String 物品名, EGradeType 品質)`。屬性 `beoh`=名稱、`beoi`=`EGradeType`、`beoj`=`GradeSO`。
- `TaskbarHero.Data.EGradeType`（enum，10 階 + NONE）：
  `COMMON=0, UNCOMMON=1, RARE=2, LEGENDARY=3, IMMORTAL=4, ARCANA=5, BEYOND=6, CELESTIAL=7, DIVINE=8, COSMIC=9, NONE=10`
- `TaskbarHero.EBoxType`（enum）：`NORMAL=0, BOSS=1, ACTBOSS=2`
- `TaskbarHero.UI.StageBox`：場上某一「箱堆」，有 `m_boxType : EBoxType`，開箱入口是參數為 `EBoxType` 的方法（混淆名，目前為 `Void kwj(EBoxType)` 與 `IEnumerator kwv(EBoxType)`）。
- `StageManager` **沒有**可讀的「開箱」事件（只有撿箱用的 `OnGetBox : Action<int>`，F5 已在用）。所以箱種必須靠 hook `StageBox` 開箱方法取得。
- 現有 `Hooks.cs` 已經在 patch 混淆方法名（`Monster.ebj`、`Hero.gnr`、`StageManager.set_stageState`），所以再 patch 一個 `StageBox` 混淆方法與本專案慣例一致；改版時由 `tbh-game-update` runbook 重新對應。

## 元件設計

### A. `BoxOpenTracker.cs`（新）— 擷取 + 資料模型 + 持久化

**資料擷取（兩個 Harmony hook）**

- **Hook A — 箱種 context**：prefix 於 `StageBox` 的開箱方法。
  - 解析方式：**用簽章比對**找 `StageBox` 上「instance、單一參數 `EBoxType`、回傳 `IEnumerator`」的方法（即 `kwv`），而非寫死混淆名，降低改版破壞面。若簽章找不到再 fallback 到非協程版（回傳 `Void`、參數 `EBoxType`，即 `kwj`）。
  - 行為：設定 `static EBoxType _openingType`（並記 `__instance`）。代表「接下來建構的 `BoxOpenLog` 屬於這個箱種」。
- **Hook B — 開出內容**：postfix 於 `BoxOpenLog..ctor(String, EGradeType)`。
  - 讀 ctor 參數的品質與名稱，產生一筆 `BoxOpenEvent { Time, Grade(EGradeType), Name(string), Type(EBoxType), Stage(string) }`。
  - `Type` 取自 `_openingType`；`Stage` 取自 `CharacterReader.CurrentStageId()`（與 `BoxTracker` 一致）。
  - 累加 `Counts[type][grade]++`，append 到時序 log（上限 1000，超過移除最舊），`Version++`，節流寫檔。

**邊界情況**
- 抓不到箱種 context（`_openingType` 未被設過，或 Hook A 簽章解析失敗）：歸到 `UNKNOWN` 桶（額外一欄），絕不丟資料。
- 同時多堆自動開箱：以「最近一次 Hook A 設定的箱種」歸屬；屬已知近似，於 spec 標記為可接受誤差（玩家點擊通常逐堆進行）。

**資料模型**
```
enum 對應沿用遊戲 EGradeType / EBoxType。
BoxOpenEvent { DateTime Time; int Grade; string Name; int Type; string Stage; }
Counts: long[BoxTypeCount+1, GradeCount]   // +1 = UNKNOWN 箱種桶
Log:    List<BoxOpenEvent>（上限 1000）
Version: int（面板刷新用）
```

**持久化** — `BepInEx/config/dpsmeter_boxopen/`
- `counts.txt`：終身累計矩陣（每行 `boxType grade count`）。權威的分佈來源，永久累積。
- `log.txt`：最近開箱時序（每行一筆 event，TSV），上限 1000。append-on-event，啟動載入。
- 寫檔節流（例如每 N 筆或每數秒 flush 一次），避免每件物品都 IO。

### B. `BoxOpenOverlayBehaviour.cs`（新）— F4 IMGUI 面板（仿 F5 風格）

- **標題列**：`Loc.G("boxopen_title")` + 總開箱件數 + 清除鈕。
- **品質分佈矩陣**（主視覺）：
  - 每一**列** = 一個品質（COMMON→COSMIC；NONE 不顯示；某品質四欄全 0 可隱藏以省空間）。
  - **欄** = `NORMAL | BOSS | ACTBOSS | 總計`（若有 UNKNOWN 桶且非 0，多一欄）。
  - 每格 = `次數 + 佔比%`（佔比 = 該箱種該品質 / 該箱種總件數）。
  - 品質配色：優先讀遊戲 `GradeSO` 的顏色；取不到則用內建 10 階色表。
- **分頁時序開箱紀錄**（下方）：欄位 `時間 / 箱種 / 品質 / 物品名`，與 F5 一致的分頁控制。
- **清除鈕**：清空統計；點擊時確認是否一併刪除存檔（`dpsmeter_boxopen/`）。

### C. F5 持久化 — `BoxTracker` 擴充 + `BoxStore.cs`（新）

- 新 `BoxStore`：把 `BoxTracker.Events` 落地到 `BepInEx/config/dpsmeter_boxlog/log.txt`（TSV：time / stage / arg / type）。append-on-event、啟動 `LoadAll()` 回填 `Events`（上限沿用 500，載入時取最近 500）。
- `BoxTracker.OnGetBox`：新增 event 後呼叫 `BoxStore.Append(ev)`。
- **每小時速率**：改為只用「本次 session 撿到的箱」計算 —
  - 記 `static DateTime _sessionStart`（plugin 啟動時設定）。
  - perHr 的分子/分母只取 `Time >= _sessionStart` 的 events；顯示意義與目前完全相同（這次遊玩的速率），避免持久化後被歷史稀釋成 0。
  - 總數、Boss 數、各關計數則用**完整持久化歷史**。
- F5 清除鈕：除了清記憶體 `Events`，也刪 `dpsmeter_boxlog/`。

### D. 接線 — `Plugin.cs`

- 新增 `public static KeyCode BoxOpenToggleKey = KeyCode.F4;`（F4 目前未使用，已確認）+ Config bind（與其他鍵一致，支援自訂）。
- 啟動：`BoxStore.LoadAll()` 回填 F5；`BoxOpenTracker.Load()` 回填 F4；設定 `_sessionStart`。
- Harmony：新增 `BoxOpenLog` ctor patch 與 `StageBox` 開箱 patch（後者以簽章解析方法後手動 `harmony.Patch`，因為無法用 `[HarmonyPatch(typeof, "名稱")]` 寫死混淆名）。
- 建立並掛載 `BoxOpenOverlayBehaviour`，F4 切換顯示。

### E. 多語 — `Localization.cs`

- 沿用現有 5 語（zh-Hant / en / ja / zh-Hans / es）。
- 新增 key：面板標題、欄位（箱種、品質、物品、時間、總計、每箱種佔比）、清除確認。
- 10 個品質名稱：優先沿用遊戲既有譯名來源；若無則自備 5 語對照（COMMON…COSMIC）。

## 範圍外（YAGNI）

- 每**關卡**的品質拆分（矩陣已含箱種；再依關卡拆會過度擁擠，event 內已存 `Stage`，未來要做不需改資料模型）。
- 理論掉率對照（遊戲無公開掉落表）。
- 圖表化（先用數字矩陣，足以回答核心問題）。

## 測試策略

- **單元**：`BoxOpenTracker` 的計數聚合與佔比計算（給定一串 event → 預期矩陣與百分比）；`BoxStore` 序列化往返（write→read 等值）；session-only perHr 過濾（混入歷史 event 後 perHr 不變）。沿用 `TrackerTests`。
- **手動**（依 memory「Deploy: restart + verify」）：建置 DLL → 自行重啟遊戲 → 開數箱確認 F4 矩陣計數/箱種正確、F5 重啟後仍有數據、perHr 數字合理。

## 改版風險

- `StageBox` 開箱方法與 `BoxOpenLog` ctor 為遊戲內部 API；型別名可讀（`BoxOpenLog`/`EGradeType`/`EBoxType`/`StageBox`）較穩，開箱方法為混淆名（以簽章解析緩解）。改版壞掉時走 `tbh-game-update` runbook 重新對應，與現有 hook 維護方式相同。

# 掉寶熱力圖面板 (Loot Heatmap) — 設計

日期：2026-06-09 · 接續 F1 中控台（PanelRegistry 可擴充架構的第一個 B 階段新面板）

## 目標

新增一個面板，用「日 × 24小時」熱力圖呈現開箱活動隨時間的分佈，並可在
**開箱率 / 掉寶率** 兩個指標間切換；面板底部複用 F11 的通關秒數趨勢曲線。
面板註冊進 PanelRegistry，自動出現在 F1 中控台 icon 列。

## 範圍

- 新面板（暫定熱鍵 **F3**、icon **▦**、id `lootmap`）。
- 熱力圖：**日 × 24小時**（列＝日期，欄＝0–23 時）。不做週×星期日曆。
- 指標切換（2 種）：
  - **開箱率**：該 (日,時) 格的**總開箱數**。
  - **掉寶率**：該格 **grade ≥ 3（傳說以上）** 的開箱數。
- 底部：**複用 F11 趨勢圖**（通關秒數），不重造輪子。

非目標：跨週日曆、估值指標、改動 F4 的記錄邏輯。

## 資料層：自有時間桶 store

F4 的持久化只有 `counts.txt`（終生總計，無時間）＋ `log.txt`（capped 逐筆，有時間戳）。
自動開箱量大，capped log 不足以畫歷史 → 本面板維護自己的終生時間桶。

### `LootTimeline.cs`（新，純資料 + 檔案 IO，可單元測試）
```
class LootTimeline {
    // 鍵 = (yyyy-MM-dd, hour 0..23)；值 = int[10]（每 grade 計數）
    Dictionary<(string day,int hour), int[]> Buckets;

    void Observe(BoxOpenStats stats);   // 每幀呼叫：吃掉上次以來 stats.Log 的新事件
    long Opens(day, hour);              // 全 grade 加總           → 開箱率
    long GoodLoot(day, hour);           // grade>=GoodThreshold 加總 → 掉寶率
    // 聚合 / 查詢給 UI：列出有資料的日期、某日各小時值、全域 max（給顏色正規化）

    const int GoodThreshold = 3;        // 傳說(legendary) 以上 = 好寶
}
```
- **Observe**：記住上次處理到的最後事件（以 `Time` + 計數位移判斷，避免重算/漏算）。
  每筆新 `BoxOpenEvent` → `Buckets[(e.Time.Date, e.Time.Hour)][e.Grade]++`。
  因為每幀都看，capped log 砍舊資料前一定已被我收進桶。
- **持久化**：`dpsmeter_boxopen/timeline.tsv`，每行 `yyyy-MM-dd⇥HH⇥g0..g9`。
  啟動載入（終生），有新增即存（沿用 BoxStore 風格的簡單 TSV append/rewrite）。
  Dir 由 Plugin 設定（與 BoxOpenStore 同目錄）。
- 新功能上線前的歷史開箱無時間戳、不納入（只在 F4 總計）——可接受。

## 趨勢圖複用：抽出共用元件

F11 的曲線目前內嵌在 `CompareOverlayBehaviour`。抽成共用繪圖：

### `TrendChart.cs`（新）
```
static class TrendChart {
    // 在給定 rect 畫一條折線圖（點、連線、可選基準點高亮）。
    // 參數：rect、資料點（值 + 標籤）、樣式（顏色/字級）、可選點擊回傳命中索引。
    static int Draw(Rect r, IReadOnlyList<float> values, ..., styles);
}
```
- `CompareOverlayBehaviour` 改呼叫 `TrendChart.Draw(...)`，**行為與外觀不變**（純重構）。
- 新面板底部呼叫同一個 `TrendChart.Draw(...)`，餵 F11 同來源資料（RunStore 通關秒數）。

## 面板：`LootMapOverlayBehaviour.cs`（新）

以 `BoxOpenOverlayBehaviour` / `BoxOverlayBehaviour` 為範本（同樣式/drag/close/slot/UiScale）。
InputCompat 用 **slot 7**（須把 `MaxPanels` 7→8）。熱鍵 `Plugin.LootMapToggleKey`(F3)。

版面（由上而下）：
1. 標題列：`Loc.G("lootmap_title")` + ✕；整面板空白處可拖（比照 hub 修正後）。
2. summary 卡片（仿參考圖）：總開箱、今日、本週、（可選 連續天數）。
3. 指標切換列：`(開箱率) 掉寶率` — 點擊切換 `_metric`。
4. 熱力圖：列＝最近 N 天（有資料的日期，新到舊），欄＝0–23 時；
   格子顏色深淺 = 該指標值 / 全域 max（線性或輕微 gamma）。滑過格子→ tooltip 顯示該格數值。
5. 分隔線 + `TrendChart.Draw(...)`（通關秒數趨勢，複用 F11）。

註冊：`PanelRegistry.Register("lootmap", 6, "▦", () => Loc.G("lootmap_title"), Plugin.LootMapToggleKey, get, set)`
（order 6，排在 boxopen 之後；F4 boxopen 目前 order 5）。

## Plugin / InputCompat / Localization 改動

- **Plugin.cs**：`LootMapUI` config（PosX/PosY/PanelWidth/StartVisible=false/ToggleKey="F3"）、
  `LootMapToggleKey`、`RegisterTypeInIl2Cpp<LootMapOverlayBehaviour>()` + AddComponent、
  設定 `LootTimeline` 的 Dir 並 `Load()`、log 加 lootmap。版本 bump。
- **InputCompat.cs**：`MaxPanels` 7→8（新 slot 7）。
- **Localization.cs**：`lootmap_title`、`metric_opens`(開箱率)、`metric_loot`(掉寶率)、
  summary 卡片標籤（總計/今日/本週）五語。

## 預設可見

`LootMapStartVisible` 預設 **false**（中控台啟動只開 hub+DPS+受到傷害的原則不變）。

## 與並行 F4 session 的界線

- 不改 F4 的記錄/持久化邏輯；只**唯讀觀察** `BoxOpenStats.Log`。
- 會動到的共用檔：`InputCompat.cs`(MaxPanels)、`Plugin.cs`(註冊)、`Localization.cs`、
  `CompareOverlayBehaviour.cs`(抽 TrendChart 重構)。提交時注意只納入本功能 hunk。
```

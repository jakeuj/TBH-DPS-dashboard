# F1 中控台 (Control Center) — 設計

日期：2026-06-09 · 版本目標：v0.7.0

## 目標

新增一個 F1「中控台」浮窗，作為所有面板的開關中心 + 迷你即時摘要。
預設遊戲啟動時只顯示：**F1 中控台 + DPS + 受到傷害**（其餘維持隱藏）。
架構走「面板註冊表」模式，未來新面板（例如寶箱熱力圖）只要註冊就自動出現在清單。

## 範圍

- **A（本次實作）**：中控台管理現有 5 個面板（DPS=F9、受到傷害=F10、關卡比較=F11、刷關效率=F6、掉寶=F5）的顯示開關，並顯示迷你摘要。
- **B（預留，不在本次）**：新增內嵌面板（熱力圖等）。架構需可擴充。

現有 F5/F6/F9/F10/F11 直接熱鍵**全部保留**，F1 只是多一個入口。

## 架構：方案 1 — PanelRegistry（靜態註冊表）

### `PanelRegistry.cs`（新）
```
class PanelEntry { string Id; int Order; Func<string> Name; KeyCode Hotkey; Func<bool> Get; Action<bool> Set; }
static class PanelRegistry {
    static List<PanelEntry> Panels;
    static void Register(string id, int order, Func<string> name, KeyCode hotkey, Func<bool> get, Action<bool> set);
       // 依 id 去重（Awake 可能重跑）；存在則就地更新；否則插入並依 Order 排序
}
```

### 各 overlay 改動（5 處，各 +1 行）
每個 overlay 在 `Awake()` 設定完 `_visible` 後呼叫 `PanelRegistry.Register(...)`，
Get/Set delegate 直接讀寫該實例的私有 `_visible`（捕捉 this）。本體邏輯與既有熱鍵完全不動。

| Id | order | Name key | Hotkey | 檔案 |
|----|-------|----------|--------|------|
| dps | 0 | dps_title | Plugin.ToggleKey | OverlayBehaviour.cs |
| taken | 1 | taken_title | Plugin.TakenToggleKey | TakenOverlayBehaviour.cs |
| compare | 2 | compare_title | Plugin.CompareToggleKey | CompareOverlayBehaviour.cs |
| farm | 3 | farm_title | Plugin.FarmToggleKey | FarmOverlayBehaviour.cs |
| box | 4 | box_title | Plugin.BoxToggleKey | BoxOverlayBehaviour.cs |

### `HubOverlayBehaviour.cs`（新，F1，slot 5）
以 `BoxOverlayBehaviour` 為視覺/互動範本（同樣的 styles、drag、close、slot 路由、UiScale）。
內容：
1. **標題列**：`hub_title` + ✕ 關閉 + 整列可拖曳。
2. **迷你摘要區**（一兩行）：目前 DPS、本場時長、已開寶箱數。
   - DPS / 時長：`Plugin.Tracker.GetSnapshot(Time.time)` → `.LiveDps` / `.DurationSeconds`
   - 寶箱：`BoxTracker.Events.Count`
3. **面板開關列**：迭代 `PanelRegistry.Panels`，每筆一個按鈕＝`名稱 + 熱鍵標籤`；
   `Get()==true` 亮色、`false` 暗色；點擊 → `Set(!Get())`。

### `Plugin.cs`
- config：`HubPosX/HubPosY/HubPanelWidth`、`HubStartVisible=true`、`_hubToggleKeyName="F1"`。
- `public static KeyCode HubToggleKey = KeyCode.F1;` + 解析。
- `RegisterTypeInIl2Cpp<HubOverlayBehaviour>()` + `AddComponent`（最後一個，slot 5）。
- 版本 → `0.7.0`，log 加 hub key。

### `InputCompat.cs`
- `MaxPanels` 5 → 6（容納 Hub 的 slot 5 click 路由）。

### `Localization.cs`
- 新增 `hub_title` = {中控台, Control Center, コントロール, 中控台, Centro}。
- 摘要標籤重用既有 `duration` / `boxes`；DPS 用 `dps_title` 或字面 "DPS"。

## 預設可見狀態

- DPS：`StartVisible` 預設 true（不變）
- 受到傷害：`TakenStartVisible` 預設 true（不變）
- 關卡比較 / 刷關 / 掉寶：預設 false（不變）
- 中控台：`HubStartVisible` 預設 true（新）

→ 啟動時剛好 = F1 + DPS + 受到傷害。符合需求。

## 非目標 (YAGNI)

- 不做面板位置/縮放的集中管理（各面板仍自管）。
- 不在本次新增任何資料型面板（熱力圖屬 B 階段）。
- 不改現有熱鍵行為。
```

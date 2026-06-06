# TBH DPS Meter — 觀察與診斷紀錄

最後更新：2026-06-06（session 進行中）
專案路徑：`D:\tmp\dpsmod\`　部署目標：`D:\SteamLibrary\steamapps\common\TaskbarHero\BepInEx\plugins\TBH.DpsMeter.dll`

---

## 1. 遊戲與環境

| 項目 | 值 |
|------|----|
| 遊戲 | TBH: Task Bar Hero（TaskBarHero.exe），開發商 TesseractStudio |
| 版本 | 1.00.09 |
| 引擎 | Unity **6000.0.72f1**，**IL2CPP**（無 Managed 資料夾） |
| metadata | 未混淆（類別/成員名稱明文，但部分類別名是短碼 bbj/wj/gnr 等） |
| 防作弊 | 內含 **ACTk (Anti-Cheat Toolkit)**，stats 用 ObscuredInt/Float；我們只「讀」DamageInfo，未竄改 |
| 輸入系統 | **新版 Input System（Unity.InputSystem.dll）** → 傳統 IMGUI 滑鼠事件與舊版 `UnityEngine.Input` 都收不到 |
| Steam AppID | 3678970 |
| **重要** | 必須**透過 Steam 啟動**，直接點 exe 不會注入 BepInEx（Unity 6 此 build 的 winhttp proxy 行為） |

### 使用者硬體環境（關鍵）
- **雙螢幕**：桌面合計寬度 **5120×1440**（兩台 2560×1440）
- **DPI 縮放 150%**（從 1455 ÷ 970 = 1.5、或 scale=(1.5,1.5) 推得）
- Unity 邏輯 **Screen = 1455×1338**（非整數標準解析度，疑似受 DPI/視窗影響）

---

## 2. 外掛架構（BepInEx 6 IL2CPP plugin）

| 檔案 | 職責 |
|------|------|
| `Plugin.cs` | BasePlugin 進入點、config、Harmony patch、注入兩個 overlay、Loc.Init |
| `DpsTracker.cs` | 純邏輯：造成傷害統計（滑動視窗即時DPS/峰值/平均/總傷/類型佔比/暴擊）。有單元測試 |
| `DamageTakenTracker.cs` | 純邏輯：承受傷害統計（DTPS/峰值/平均/最大單擊/受擊/入站暴擊/元素屬性佔比）。由並行 session 新增 |
| `Hooks.cs` | Harmony patch（見下） |
| `OverlayBehaviour.cs` | DPS 面板（右下）。IMGUI 繪製、曲線、分布、◀▶ 歷史回顧 |
| `TakenOverlayBehaviour.cs` | 承受傷害面板（左下）。IMGUI、曲線、元素分布。並行 session 新增 |
| `RunStore.cs` | 每關存檔/讀檔（含 taken 數據），最近 30 筆，路徑 `BepInEx/config/dpsmeter_runs/` |
| `InputCompat.cs` | **輸入相容層**（見第 4 節，目前問題核心） |
| `Localization.cs` | i18n（繁中/English/日本語，Auto 偵測） |

---

## 3. Harmony Hooks（已驗證）

| 目標方法 | 用途 | 狀態 |
|----------|------|------|
| `Monster.ebj(DamageInfo, bool)` postfix | 怪物承受傷害 → 過濾 `attacker.b_isHero` → 玩家造成的 DPS | ✅ 正常，數值/暴擊/類型正確 |
| `Hero.gnr(DamageInfo, bool)` postfix | 英雄承受傷害 → damage-taken | ✅ 正常（見下方坑） |
| `StageManager.set_stageState` | （欄位存取器，無法 patch）→ 改用輪詢 | 由 OverlayBehaviour 每幀輪詢 stageState 取代 |

### 關鍵發現
- **`DamageInfo`** 欄位：`Attacker(Unit)`、`OriginDamage(float)`、`IsCritical(bool)`、`DamageType(EDamageType)`、`DamageAttribute(EDamageAttribute)`
- **`Unit.b_isHero`** → 判斷玩家方 vs 怪物
- **`EDamageType`**：None=0, Melee=1, Projectile=2, AOE=4, Summon=8, DOT=16, Trap=32（可組合，如 10=Summon+Projectile）
- **`EDamageAttribute`**：Physical=0, Fire=1, Cold=2, Lightning=3, Chaos=4, AllElement=5, None=6
- **`EStageState`**：NONE=0, MONSTERSPAWN=1, BATTLE=2, REORGANIZATION=3
  - 每一波循環 `MONSTERSPAWN→BATTLE→REORGANIZATION`；**整關結束/換關才會出現 NONE**
  - → 重置時機定為「**逐關卡**」：只在 NONE 重置並存檔，波次之間累計（已驗證 log）

### 踩過的坑（已解）
1. **逐波次重置太頻繁** → 改逐關卡（NONE 邊界）。
2. **`Hero.ebj` 造成堆疊溢位**（0xc00000fd，coreclr.dll）：同時 patch `Monster.ebj` 與 `Hero.ebj`（同為 `Unit.ebj` 的兄弟覆寫）導致 MonoMod trampoline 無限遞迴。
   - **繞過方式**：damage-taken 改 patch **`Hero.gnr`**（不同方法，無衝突）。
   - 副作用：`gnr` 帶的 `DamageType` 永遠是 None（怪物攻擊本來就沒分類），但 `DamageAttribute`（元素）正確 → 承受傷害面板**只顯示元素分布**，移除無意義的類型分布。

---

## 4. 輸入 / 座標問題（**目前未解，核心議題**）

### 背景
新版 Input System 下：
- IMGUI `GUI.Button` 點擊、滑鼠事件 → **不觸發**
- 舊版 `UnityEngine.Input.mousePosition` → 回傳垃圾負值
- 新版 `Mouse.current.position` → **凍結**（實測停在 (-1261,998) 不隨滑鼠變動）

→ 因此改用 **Win32 API**（`InputCompat.cs`）直接讀 OS 游標與滑鼠鍵：
- `GetCursorPos` → 桌面座標
- `ScreenToClient(hwnd)` → 視窗客戶區座標
- `GetClientRect(hwnd)` → 客戶區大小
- `GetAsyncKeyState` → 滑鼠鍵 / 熱鍵
- 換算：`guiPos = clientPos × (Screen.width / clientWidth, Screen.height / clientHeight)`
- 點擊/拖曳都在 `Update()` 用輪詢 + 自訂命中判定處理（不靠 IMGUI 事件）

### 症狀
- 按鈕（Reset/◀/▶）**有時**能按、拖曳**有時**準
- **靠近左上角（受到傷害面板 x≈121）很準**；**靠右下（DPS 面板 x≈1021）超歪、按鈕按不到**
- 拖曳會「跑掉」/跳動
- → 典型「縮放係數錯誤，誤差隨離原點距離放大」

### 根因（從 diag log 確認）
取得「目標視窗 hwnd」的方法都不穩定，抓到**錯誤的視窗**：

| 嘗試 | 結果 |
|------|------|
| `GetForegroundWindow()` | 多螢幕下有時回傳桌面/別的視窗（client 2560 或 **5120×1440**） |
| `Process.MainWindowHandle` | 回傳一個**包裝視窗**，客戶區原點不同 → 產生**固定偏移**（拖曳 OK 因位移差抵消，但按鈕絕對座標歪掉） |
| `GetActiveWindow()` | 仍抓到 **5120×1440**（橫跨雙螢幕的視窗），scale=(0.284, 0.929) 非一致 → 座標飄 |

關鍵 diag（拖曳 DPS 面板時）：
```
[drag] m=(1217,1188) rect=(1009,1104) raw=(1724,1279) client=5120x1440 screen=1455x1338 scale=(0.284,0.929) down=True
```
- `client=5120x1440` = 雙螢幕桌面寬 → **抓錯視窗**（非遊戲渲染視窗）
- `scale` X≠Y 且錯 → 座標非線性偏移

另外曾觀察到「抓對視窗」時：
```
client=1455x1338 screen=1455x1338 scale=(1,1)   ← 完美 1:1
```
以及 DPI 造成的：
```
client=970x892 screen=1455x1338 scale=(1.5,1.5) ← 150% DPI
```

### 結論
- 我們的 overlay（IMGUI）以 **Unity `Screen` 像素**（1455×1338）繪製。
- 遊戲設定的 **UI 百分比** 只縮放遊戲自己的 uGUI Canvas，**不影響 Unity Screen / 我們的 IMGUI** → 與本問題無關。
- 真正需要：穩定鎖定**遊戲的 Unity 渲染視窗**，用它的 client 大小算出**一致的**縮放。

### 下一步（提議，尚未實作 — 使用者要求先記錄）
- 用 **`FindWindow("UnityWndClass", null)`** 鎖定 Unity 渲染視窗（Unity 標準視窗類別），快取之；fallback 用 GetActiveWindow/GetForegroundWindow。
- 預期該視窗 client 會 == Screen（或差一個一致的 DPI 係數），縮放一致 → 不論面板放哪都準。
- 仍需 diag 驗證：FindWindow 抓到的 client 是否 ≈ Screen。
- 備案：若 FindWindow 也不穩，考慮列舉本行程的 `UnityWndClass` 視窗、或鎖定一個固定 DPI 係數。

---

## 5. 已完成且正常的功能

- ✅ DPS 面板（右下）：即時/峰值/平均/總傷/暴擊、DPS 曲線（Y 軸刻度、波次分隔線）、傷害類型彩色分布、逐關卡累計、◀▶ 歷史回顧
- ✅ 承受傷害面板（左下）：DTPS/峰值/平均/總承受/最大單擊/受擊/入站暴擊、曲線、**元素分布**
- ✅ 閒置 **3 秒**凍結（不歸零、曲線不消失）— 用 `Time.time`＋`min(now, lastDamage+3)`
- ✅ i18n：繁中 / English / 日本語（Auto 偵測，可在 cfg 覆寫）
- ✅ BepInEx console（CMD 黑視窗）已關閉
- ✅ 發布包：`D:\tmp\dpsmod\dist\TBH-DpsMeter-v0.1.0.zip`（37MB，含安裝說明、console 關閉的 BepInEx.cfg）
- ⚠️ **未解**：拖曳/按鈕座標在多螢幕+150%DPI 下不準（第 4 節）
- ⏳ **待做**：承受傷害面板的「歷史回顧」同步（資料已存，但 taken 面板尚未跟著 DPS 面板的 ◀▶ 顯示歷史；且 taken 曲線未存歷史樣本）

---

## 6. 部署 / 測試流程備忘

```
# build
cd D:\tmp\dpsmod\DpsMeter && dotnet build -c Release   (dotnet 在 C:\Program Files\dotnet)
# deploy（遊戲執行中會鎖 DLL，要先關）
Stop-Process -Name TaskBarHero -Force; copy bin\Release\TBH.DpsMeter.dll <game>\BepInEx\plugins\
# 啟動（必須透過 Steam）
Start-Process "steam://rungameid/3678970"
# log
<game>\BepInEx\LogOutput.log   （debug 由 cfg [Debug] LogDamageSamples 控制）
```

- 單元測試：`D:\tmp\dpsmod\TrackerTests`（`dotnet run -c Release`）
- 反射 dump 工具：`D:\tmp\dpsmod\inspector`（用 MetadataLoadContext 讀 interop 的型別/方法簽名）
- 並行 session 也在編輯此專案（damage-taken 功能由使用者另一個 session 開發）

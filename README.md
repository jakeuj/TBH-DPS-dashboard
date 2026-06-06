# TBH DPS Meter

In-game **DPS / damage-taken** overlay for **TaskBarHero** (TBH: Task Bar Hero),
built as a BepInEx 6 IL2CPP plugin. Tested on game **v1.00.09** (Unity 6 / IL2CPP).

遊戲內即時 **DPS / 承受傷害** 監控外掛。支援 **繁體中文 / English / 日本語**（自動偵測）。

> ⬇️ **一般玩家只要下載 [Releases](../../releases/latest) 裡的 zip 就能用，不用編譯。**

---

## What it shows / 顯示內容

**DPS panel (你造成的傷害):**
- Live DPS (5s 滑動視窗) + Peak + Average
- 總傷害 + 戰鬥時長 + 波數
- 傷害類型分布（近戰 / 投射 / 範圍 / 召喚 / 持續 / 陷阱，含複合旗標）
- 暴擊率 + 暴傷佔比

**Damage-taken panel (你受到的傷害):**
- Live DTPS（每秒承受）+ Peak + Average
- 總承受 + 時長 + 最大單次受擊
- **受擊**（被打中的次數）+ **入站暴擊**（怪物對你的暴擊率）
- 兩條分布條：元素屬性（物理/火/冰/雷/混沌）與傷害類型

## Controls / 操作
- **F9** — 顯示/隱藏 DPS 面板（可設定：`ToggleKey`）
- **F10** — 顯示/隱藏 承受傷害面板（可設定：`TakenUI.ToggleKey`）
- 滑鼠拖曳 — 移動面板（位置自動記住，兩面板獨立）
- 面板右上「重置」— 歸零重算；**◀ ▶** — 翻看過去關卡紀錄
- **PageUp / PageDown** — 調整面板透明度（兩面板共用）

> ⚠️ 面板點擊會**穿透**到遊戲（外掛只被動讀取滑鼠、不攔截輸入），戰鬥中點面板時角色仍會動作，這是正常行為。

---

## 安裝 / Install

### A. 首次安裝（沒裝過 BepInEx）— 給朋友的做法
1. 到 **[Releases](../../releases/latest)** 下載 `TBH-DpsMeter-vX.Y.Z.zip`。
2. Steam → 對「TBH: Task Bar Hero」按右鍵 → 管理 → 瀏覽本機檔案
   （資料夾裡會看到 `TaskBarHero.exe`）。
3. 把 zip 裡的**所有檔案**解壓進那個資料夾，讓 `winhttp.dll`、`doorstop_config.ini`、
   `dotnet`、`BepInEx` 跟 `TaskBarHero.exe` 在**同一層**（問是否覆蓋選「是」）。
4. **一定要透過 Steam 啟動**遊戲（直接點 exe 不會載入外掛）。
5. 第一次啟動會黑畫面 1～3 分鐘（一次性分析），之後正常。

### B. 更新外掛（已經裝過、只是換新版）
**對，更新只要換 DLL 一個檔案就好。** BepInEx 本體不用動。

把新版 `TBH.DpsMeter.dll` 覆蓋到：
```
<遊戲資料夾>\BepInEx\plugins\TBH.DpsMeter.dll
```
> 覆蓋前請**先完全關閉遊戲**（執行中時 DLL 被佔用，無法覆蓋）。換完透過 Steam 重開即可。

單獨的 DLL 也可從對應版本的 [Releases](../../releases/latest) 取得（若僅附整包 zip，解壓後取出 `BepInEx\plugins\TBH.DpsMeter.dll` 即是更新檔）。

---

## Config / 設定
設定檔：`<遊戲資料夾>\BepInEx\config\tbh.dpsmeter.cfg`（第一次跑完才會產生）
```
[General]
Language = Auto   # 可改成 zh-Hant / en / ja 強制語言
```

## Uninstall / 移除
刪掉遊戲資料夾裡的：`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、
`dotnet\`、`BepInEx\`，即可完全還原成原版。

---

## Build from source / 從原始碼編譯（開發者）
```
dotnet build DpsMeter/DpsMeter.csproj -c Release
# 產物：DpsMeter\bin\Release\TBH.DpsMeter.dll
copy DpsMeter\bin\Release\TBH.DpsMeter.dll  <遊戲>\BepInEx\plugins\
```
重啟遊戲請**透過 Steam**（此 Unity 6 build 直接啟動 exe 不會注入 BepInEx 的 winhttp proxy）。

### How it works
- 造成傷害：Harmony postfix 掛在 `TaskbarHero.Monster.ebj(DamageInfo, bool)`，
  以 `Unit.b_isHero` 過濾出玩家側命中，讀 `OriginDamage` / `IsCritical` / `DamageType`。
- 承受傷害：Harmony postfix 掛在 `TaskbarHero.Hero.ebj(DamageInfo, bool)`，計入攻擊者
  非玩家的命中，讀 `OriginDamage` / `IsCritical` / `DamageType` / `DamageAttribute`。
- 波次邊界：輪詢 `StageManager.stageState`（MONSTERSPAWN → BATTLE → REORGANIZATION），
  每次 MONSTERSPAWN 重置、REORGANIZATION 凍結。
- DPS / DTPS 數學在純 C# 的 `DpsTracker` / `DamageTakenTracker`，並有單元測試（`TrackerTests`）。

---

## ⚠️ 免責聲明 / Disclaimer
本外掛透過 BepInEx 注入遊戲、僅**被動讀取**傷害數據、不修改任何遊戲數值，且本遊戲為單機作品。
但**任何第三方修改／注入工具都可能違反遊戲或平台（如 Steam）的使用條款**，並存在導致帳號被封鎖、
存檔損毀或其他損失的風險。

**使用本軟體即代表你自行承擔全部風險。** 對於因使用本外掛而導致的任何帳號封鎖、停權、資料毀損或
其他直接或間接損害，作者**概不負責**。若不接受此條件，請勿使用。

The software is provided "as is", without warranty of any kind. Use of any third-party mod
may violate the game's or platform's Terms of Service and could result in account suspension
or ban. **You use this software entirely at your own risk; the author is not liable for any
account ban, data loss, or other damages.** See [LICENSE](LICENSE).

## License
[MIT](LICENSE) © 2026 WarmBed

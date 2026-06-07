# TBH DPS Meter

[English](README.md) · [日本語](README.ja.md) · **繁體中文** · [简体中文](README.zh-Hans.md)

**TaskBarHero**（TBH: Task Bar Hero）的遊戲內 **DPS / 承受傷害** 監控外掛，
以 BepInEx 6 IL2CPP 外掛實作。測試版本 **v1.00.09**（Unity 6 / IL2CPP）。
介面自動偵測 **繁體中文 / 简体中文 / English / 日本語 / Español**。

> ⬇️ **一般玩家只要到 [Releases](../../releases/latest) 下載 zip 就能用，不用編譯。**

![遊戲內疊加面板](image/POWERPNT_xbsYSkt6By.png)

<table>
<tr>
<td><img src="image/TaskBarHero_FkEGMBj3Kq.png" alt="DPS 面板"></td>
<td><img src="image/TaskBarHero_3TGLxaOOR2.png" alt="承受傷害面板"></td>
</tr>
<tr>
<td align="center"><b>DPS 面板</b>（你造成的傷害）</td>
<td align="center"><b>承受傷害面板</b>（你受到的傷害）</td>
</tr>
</table>

---

## 顯示內容

**DPS 面板:**
- 即時 DPS（5 秒滑動視窗）+ 峰值 + 平均
- 總傷害 + 戰鬥時長 + 波數
- 傷害類型分布（近戰 / 投射 / 範圍 / 召喚 / 持續 / 陷阱，含複合旗標）
- 暴擊率 + 暴傷佔比

**承受傷害面板:**
- 即時 DTPS（每秒承受）+ 峰值 + 平均
- 總承受 + 時長 + 最大單次受擊
- **受擊**（被打中的次數）+ **入站暴擊**（怪物對你的暴擊率）
- 兩條分布條：元素屬性（物理/火/冰/雷/混沌）與傷害類型

## 關卡比較（F11）
按 **F11** 開啟**關卡比較面板**：把你存下的紀錄依關卡分組，拿目前這場跟**基準**（預設最快通關，或你手動釘選的一場）對照，
顯示時長、**有效輸出 vs 停輸出（跑路）時間**、平均/峰值/暴擊、傷害分配、**每波時間**，以及**裝備與技能變更**
——讓你看出「換了配置後通關效率差多少」。用 ◀ ▶ 翻看場次、≪ ≫ 切換關卡、釘選鈕設定基準。

## 操作
- **F9** — 顯示/隱藏 DPS 面板（可設定：`ToggleKey`）
- **F10** — 顯示/隱藏 承受傷害面板（可設定：`TakenUI.ToggleKey`）
- **F11** — 顯示/隱藏 關卡比較面板（可設定：`CompareUI.ToggleKey`）
- **滑鼠拖曳** — 移動面板（位置自動記住，兩面板獨立）
- 右上 **重置** 鈕歸零重算；**◀ ▶** 翻看過去關卡紀錄
- **PageUp / PageDown** — 調整面板透明度（兩面板共用）

> ⚠️ 面板點擊會**穿透**到遊戲（外掛只被動讀取滑鼠、不攔截輸入），戰鬥中點面板時角色仍會動作，這是正常行為。

---

## 安裝

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

---

## 設定
設定檔：`<遊戲資料夾>\BepInEx\config\tbh.dpsmeter.cfg`（第一次跑完才會產生）
```
[General]
Language = Auto   # 可改成 zh-Hant / zh-Hans / en / ja / es 強制語言
```

## 移除
刪掉遊戲資料夾裡的：`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`、
`dotnet\`、`BepInEx\`，即可完全還原成原版。

---

## 從原始碼編譯（開發者）
```
dotnet build DpsMeter/DpsMeter.csproj -c Release
# 產物：DpsMeter\bin\Release\TBH.DpsMeter.dll
copy DpsMeter\bin\Release\TBH.DpsMeter.dll  <遊戲>\BepInEx\plugins\
```
重啟遊戲請**透過 Steam**（此 Unity 6 build 直接啟動 exe 不會注入 BepInEx 的 winhttp proxy）。

### 運作原理
- **造成傷害：** Harmony postfix 掛在 `TaskbarHero.Monster.ebj(DamageInfo, bool)`，
  以 `Unit.b_isHero` 過濾出玩家側命中，讀 `OriginDamage` / `IsCritical` / `DamageType`。
- **承受傷害：** Harmony postfix 掛在 `TaskbarHero.Hero.ebj(DamageInfo, bool)`，計入攻擊者
  非玩家的命中，讀 `OriginDamage` / `IsCritical` / `DamageType` / `DamageAttribute`。
- **波次邊界：** 輪詢 `StageManager.stageState`（MONSTERSPAWN → BATTLE → REORGANIZATION），
  每次 MONSTERSPAWN 重置、REORGANIZATION 凍結。
- DPS / DTPS 數學在純 C# 的 `DpsTracker` / `DamageTakenTracker`，並有單元測試（`TrackerTests`）。

---

## ⚠️ 免責聲明
本外掛透過 BepInEx 注入遊戲、僅**被動讀取**傷害數據、不修改任何遊戲數值，且本遊戲為單機作品。
但**任何第三方修改／注入工具都可能違反遊戲或平台（如 Steam）的使用條款**，並存在導致帳號被封鎖、
存檔損毀或其他損失的風險。

**使用本軟體即代表你自行承擔全部風險。** 對於因使用本外掛而導致的任何帳號封鎖、停權、資料毀損或
其他直接或間接損害，作者**概不負責**。若不接受此條件，請勿使用。

## 授權
[MIT](LICENSE) © 2026 WarmBed

# 同關卡比較面板 — 設計文件

最後更新：2026-06-07
專案：TBH DPS Meter（TaskBarHero BepInEx IL2CPP 外掛）

## 目標

讓使用者在同一關卡的多次紀錄之間，挑一筆當「基準」，對比其他歷史場次的差異，
用來判斷「換了裝備／技能後，輸出與通關效率差多少」。對比維度：

- 核心數值：總時長、有效輸出時間、停輸出（跑路）時間、平均 DPS、峰值、暴擊率、主要屬性
- 傷害分配（傷害類型佔比）
- 每波時間
- 裝備差異（含詞綴）
- 技能差異（含等級）

## 已定決策

| 項目 | 決策 |
|---|---|
| 資料來源 | 遊戲記憶體即時讀（BepInEx / IL2CPP），不解密存檔 |
| 比較模式 | 同關卡分組；組內挑一筆當基準 |
| 預設基準 | 該關**最快通關**（總時長最短）；可手動 📌 釘選覆蓋 |
| 快照細節 | 詳細版（裝備含詞綴、技能含等級）；讀不到的欄位優雅降級 |
| 版面 | B 雙欄（基準｜這場）＋ 傷害分配對照 ＋ 每波時間表 ＋ 裝備/技能差異 |
| 進入方式 | 新面板，**F11** 開關，可拖曳，可比即時面板高 |

## 架構

盡量沿用現有模式（純 C# 計算器 + IL2CPP 讀取 hook + IMGUI overlay + 文字檔持久化）。

### 資料模型擴充：`RunRecord`

新增欄位（皆為可選，舊紀錄缺少時降級）：

- `string StageId` — 穩定關卡 ID，執行時從 `StageManager` 取得（不靠標題字串解析）
- `List<float> WaveDurations` — 每波耗時（秒）
- `float ActiveSeconds` — 有效輸出時間（有傷害事件的時間）
- `float IdleSeconds` — 停輸出時間（連續無傷害的空檔總和）
- `CharacterSnapshot Snapshot` — 場次凍結當下的角色狀態：
  - `List<StatEntry> Stats` — `{ string Key; double Value }`（攻擊/攻速/暴率/暴傷/生命…）
  - `List<GearItem> Equipment` — `{ string Slot; string Name; List<Affix> Affixes }`，`Affix = { string Name; double Value }`
  - `List<SkillEntry> Skills` — `{ string Name; int Level }`

### 新模組

1. **`CharacterReader`（IL2CPP，最大技術風險）**
   在一場凍結（REORGANIZATION）時讀 Hero 的屬性 / 裝備 / 技能。
   全程 try/catch；任何欄位讀失敗 → 該欄位留空，不影響其餘資料。
   實際讀得到哪些欄位由「可行性驗證」決定（見下）。

2. **每波時間 + 有效/停輸出**
   擴充現有 DPS 取樣邏輯：
   - 每波（MONSTERSPAWN→REORGANIZATION）記起訖時間 → `WaveDurations`
   - 取樣時若某秒無新增傷害，累加 `IdleSeconds`，否則累加 `ActiveSeconds`

3. **`StageCompare`（純 C#，可單元測試）**
   - 依 `StageId` 將 `RunRecord` 分組
   - 選基準：手動釘選優先，否則取該組總時長最短者
   - 算差異：核心數值 delta、傷害分配 delta、每波時間 delta、裝備（新增/移除/詞綴變更）、技能（新增/移除/等級變更）

4. **`CompareOverlayBehaviour`（新面板，F11）**
   IMGUI overlay，雙欄佈局，沿用現有拖曳/透明度/存位置機制。

5. **`RunStore` 序列化擴充**
   既有 `key=value` 文字格式新增上述欄位的鍵；新增 `version=2`。
   `version` 缺少或為 1 的舊檔正常載入（新欄位留空）。

6. **`Localization`**
   新增面板標籤的 5 語言字串（zh-Hant / en / ja / zh-Hans / es）。

### 錯誤處理

- IL2CPP 讀取全部包 try/catch（同現有 `Hooks` 風格），失敗只記 log、不崩潰。
- 缺欄位於面板顯示「—」。
- 舊紀錄（無快照/無 StageId）：仍可瀏覽核心數值；無 StageId 者歸入「未分類」組或以標題回退分組。

### 測試

- 純 C#（`TrackerTests`）：
  - `StageCompare` 分組、選基準、各類差異計算
  - 每波時間與 active/idle 數學
  - `RunStore` v2 存讀往返 + v1 向後相容
- IL2CPP 讀取：遊戲內 debug log 驗證（無法單元測試）。

## 實作第一步：可行性驗證（spike）

用現有 `inspector` 工具讀 `BepInEx\interop` 的去模糊化型別，dump Hero 及相關型別，
確認屬性 / 裝備詞綴 / 技能等級實際讀得到哪些，**據此定案詳細程度**：

- 詞綴讀得到 → 完整詳細版
- 詞綴讀不到但裝備名稱可 → 降為「裝備名稱清單 + 屬性數值」
- 技能等級讀不到 → 降為「技能名稱清單」

可行性結果寫回本文件後再進實作。

## 可行性驗證結果（IL2CPP RE，2026-06-07）

用 inspector dump interop 後確認資料路徑（成員名混淆，已用反射存取）：

- **屬性**：`Hero.cache`(ug) → `.beib`(xe) → `Single nsc(StatType)`（候選 `nsc/kaq/kar/kap`，
  哪個是「最終顯示值」需遊戲內驗證）。`StatType` 列舉完整（AttackDamage=1…MaxElementalDodgeChance=63）。
- **裝備**：`cache.jhe()`/`brrd`/`jgr()` → 物件 `tf`；`tf.brke`(ItemInfoData) 有 `ItemKey/NameKey/GEARTYPE`；
  詞綴在 `tf.bdzh..bdzl`(GearModData)：`iox()`=StatType、`ioy()`=值、`ioz()`=MODTYPE；唯一詞綴 `tf.oky()`→EGearUniqueMod。
- **技能**：`Unit.bchl`(List<ActiveSkill>) → `sk.skillCache`(uo) → `.behi`(SkillInfoData) `SkillKey/SkillNameKey`；
  等級候選 `uo.jjy()..jkm()`（需驗證哪個是等級）。
- **關卡 ID**：`StageInfoData.Act` + `.StageNo` → `"Act-StageNo"`；目前經 Harmony 掛 `UI_Stage.hqk(StageCache,bool)`
  postfix 擷取當前 StageCache（`becp`→StageInfoData）。
- **名稱為在地化 key**：Item/Skill/Stage 的 `*NameKey` 都是 key 而非顯示字串；解析器未定位，面板暫顯示 key。

## 遊戲內驗證清單（最後一步，需關遊戲先部署新 DLL）

1. 設定檔 `[Debug] LogSnapshot = true`，透過 Steam 啟動。
2. 打完一關，看 BepInEx log：
   - `[snap stat getter] nsc(MaxHp)=…` 對照遊戲狀態面板的生命，確認 `nsc` 是否正確；不對則改 `StatGetters` 順序。
   - `[snap skill getter] …` 對照技能等級，確認哪個 getter；更新 `SkillLevelGetters`。
3. F11 開比較面板：確認 StageId 不是空/`0-0`（否則 `UI_Stage.hqk` 沒命中，需換 hook 點）。
4. 確認裝備/技能有讀到（名稱為 key 可接受）；詞綴數值合理。
5. 全部 OK 後發 v0.2.0 Release（含新 zip）。

## E2E 驗證結果（遊戲內實測，2026-06-07）

關掉遊戲→部署→Steam 重啟→打完關卡，讀實際存檔 + log 驗證：

| 項目 | 結果 |
|---|---|
| 插件載入 / 3 patch / StageProbe hook UI_Stage.hqk / 三面板 | ✅ |
| 關卡 ID（`StageInfoData.Act-StageNo`） | ✅ `stageid=2-2` |
| 每波時間（27 波）/ 有效·停輸出（203/92，和≈時長298.5） | ✅ |
| 屬性（attack/aspd/critrate/critdmg/hp/armor/mspd，`xe.nsc(StatType)`） | ✅ 真實值 |
| 傷害分配 / 承受面 / 序列化 v2 / v1 相容 | ✅ |
| 技能（清單 `Unit.bchd`=List&lt;ActiveSkill&gt;，名稱 key + 等級 `ActiveSkill.meu`） | ✅ 等級 1/13/5 正確 |
| **裝備詞綴** | ❌ **已知限制** |

### 已知限制：裝備詞綴未擷取
已裝備物件以 uid 存在 `ug.jgr()` 回傳的 `IReadOnlyCollection<ObscuredULong>`（10 個）。
此介面集合無法經由「反射 / C# as / Il2CppInterop TryCast」列舉（Il2CppInterop 對介面 wrapper
的顯式實作，反射看不到；`is IEnumerable`=false、`TryCast<IEnumerable<ObscuredULong>>`=null）。
經 7 次遊戲內迭代仍未突破。程式碼路徑保留（`HeroProbe.ReadGear` + `EnumerateUids`，安全降級為不輸出），
待後續以 typed Il2CppInterop（直接 cast `cache` 為 `ug` 並 typed 呼叫 `jgr()`）解決。
其餘比較維度不受影響。

## 範圍外（YAGNI）

- 跨關卡比較、跨角色比較
- 圖表式趨勢（多場折線）
- 匯出 / 分享比較結果

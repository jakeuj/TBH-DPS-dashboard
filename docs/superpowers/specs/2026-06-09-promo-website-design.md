# TBH DPS Meter 推廣網站 — 設計文件

**日期：** 2026-06-09
**狀態：** 已通過 brainstorming，待使用者複審
**目標：** 為 TBH DPS Meter 外掛做一個官方推廣網站，主打 SEO，讓 TaskBarHero 玩家搜尋得到並安心下載。

---

## 1. 目標與成功標準

**為什麼做：**
1. **信任** — 外掛是讀記憶體的 BepInEx DLL，玩家第一個疑慮是「會不會是木馬」。一個正式網域、說明清楚、有截圖與開源連結的網站，比丟網盤/Discord 連結可信得多。
2. **被搜尋到** — 玩家會 google「TaskBarHero DPS」「TBH 傷害統計」之類字串。沒有網站＝不存在。
3. **正式的下載與更新入口** — 搭配外掛已有的自動更新功能，網站當作版本說明與下載的正式門面。

**成功標準：**
- 5 種語言（繁中 / 簡中 / 英 / 日 / 西）各有完整、可索引的頁面。
- 每頁有正確的 `<title>` / meta description / hreflang / canonical / OG。
- 產出 `sitemap.xml`、`robots.txt`、`SoftwareApplication` JSON-LD。
- 更新日誌自動從 GitHub Releases 產生，release 後自動重新部署。
- Lighthouse SEO ≧ 95、首頁零阻塞 JS。

**範圍外（YAGNI）：**
- 後端 / 資料庫 / 使用者帳號 — 純靜態站。
- 部落格 / 留言 / 分析儀表板 — 之後要再說。
- A/B 測試、電子報。

---

## 2. 技術選型（已決定）

| 項目 | 選擇 | 理由 |
|---|---|---|
| 框架 | **Astro** | 預設輸出純靜態 HTML、零 JS；內建 i18n 路由與 `@astrojs/sitemap`；可在建置時抓 GitHub Release。SEO 最佳。 |
| 部署 | **Zeabur**（靜態） | 使用者已熟悉；Astro `dist/` 直接當靜態站部署。 |
| 程式碼位置 | **同 repo `/site` 子目錄** | 與外掛同 repo，GitHub Actions 在 release 時連動重建 changelog；版本不分家。 |
| 語言 | **zh-Hant / zh-Hans / en / ja / es** | 與外掛 UI 完全對齊。 |
| 樣式 | 手寫 CSS（沿用設計稿的變數系統） | 站很小，不需 Tailwind；設計稿已驗證過配色與排版。 |
| 字體 | Inter + JetBrains Mono（數字） | 乾淨、工具感；數字用等寬。 |

**設計方向（已透過視覺伴侶驗證）：** 乾淨開發者工具風（淺色、大留白、截圖主導），化解「木馬」疑慮；唯一深色塊是結尾 CTA 區。設計稿存於 `.superpowers/brainstorm/`（`landing-pro-v1.html`）。

---

## 3. 網站結構

### 3.1 頁面

| 路徑（en 為預設、無前綴） | 內容 |
|---|---|
| `/` | 首頁：Hero → 數據條 → 四大功能 → 安裝摘要 → FAQ → 結尾 CTA → 頁尾 |
| `/install/` | 完整安裝教學（首次安裝 + 更新；含黑畫面說明、Steam 啟動、解除安裝） |
| `/changelog/` | 更新日誌，建置時由 GitHub Releases 產生 |

其他語言以前綴呈現：`/zh-Hant/`、`/zh-Hant/install/`、`/zh-Hant/changelog/`，以此類推 `/zh-Hans/`、`/ja/`、`/es/`。

**預設語言＝英文（root `/`）** 以求最大國際觸及；語言切換器涵蓋全部 5 種，並輸出 `hreflang` + `x-default`。

### 3.2 i18n 設定

- Astro `i18n`：`defaultLocale: 'en'`、`locales: ['en','zh-Hant','zh-Hans','ja','es']`、`routing.prefixDefaultLocale: false`。
- 文案集中在 `src/i18n/<locale>.ts`（或 `.json`）字典，每個元件取字串，不在模板裡硬寫文字。
- 共用一份頁面模板，文字由字典注入 → 5 語言不重複維護版型。

---

## 4. 元件拆解

每個元件單一職責、吃 props（含當前 locale 與字典），可獨立理解：

| 元件 | 職責 | 依賴 |
|---|---|---|
| `BaseLayout.astro` | `<head>`（title/meta/OG/hreflang/canonical/JSON-LD）、字體、全域 CSS、Nav + Footer 外殼 | `seo.ts`、字典 |
| `Nav.astro` | 頂部導覽 + 語言切換 + 下載鍵 | `LanguageSwitcher` |
| `LanguageSwitcher.astro` | 列出 5 語言，連到對應 locale 的同一頁 | Astro i18n 工具 |
| `Hero.astro` | 標題、副標、雙 CButton、信任行、Hero 截圖 | 字典、`Screenshot` |
| `StatsBand.astro` | 4 個數據（6 傷害類型 / 4 面板 / 5 語言 / 100% 開源） | 字典 |
| `FeatureRow.astro` | 單一功能：tag + 標題 + 文案 + ✓ 清單 + 截圖；支援左右交錯 | 字典、`Screenshot` |
| `InstallSteps.astro` | 三步驟卡片（首頁摘要版） | 字典 |
| `Faq.astro` | 問答清單（細線分隔；可選輕量展開） | 字典 |
| `FinalCta.astro` | 深色收尾行動區 | 字典 |
| `Footer.astro` | GitHub / 授權 / 免責 / 語言 | 字典 |
| `Screenshot.astro` | 統一的截圖外框（圓角 + 邊框 + 陰影，**無視窗燈號列**） | — |

**資料模組：**
| 模組 | 職責 |
|---|---|
| `src/lib/seo.ts` | 給定 locale + 頁面 → 產生 title/description/canonical/hreflang 連結/OG/JSON-LD 的純函式 |
| `src/lib/releases.ts` | 建置時 `fetch` GitHub Releases API，解析 `tag_name`/日期/`body`(markdown)，回傳結構化清單供 `/changelog` 使用 |
| `src/config.ts` | `SITE_URL`、GitHub `owner/repo`、最新版號等常數 |

---

## 5. 更新日誌資料流

```
GitHub Release 發佈
   └─(GitHub Actions: on release published)→ 觸發 Zeabur 重新部署
        └─ Astro 建置 → releases.ts fetch api.github.com/repos/<owner>/<repo>/releases
             └─ 解析 markdown body → 產生 /changelog/（5 語言各一份，版本內容共用、UI 字串本地化）
```

- **建置時抓**（非前端即時）→ 內容變靜態 HTML，SEO 吃得到、無 API 流量限制。
- 抓取失敗時 fallback：建置不中斷，changelog 顯示「請見 GitHub Releases」連結（避免一次 API 失敗就讓整個部署掛掉）。
- Hero 旁放一顆「v<最新版> · 看更新內容 →」徽章連到 `/changelog/`。
- release notes 的 markdown 用建置期套件（如 `marked`）轉 HTML；版號與日期語言中性，外層 UI 字串走本地化。

---

## 6. SEO 細節（核心交付）

- **每頁** `<title>` 與 meta description：locale 專屬、含目標關鍵字（如「TaskBarHero DPS meter」「TBH 傷害統計 疊圖」）。
- **hreflang**：每頁輸出全部 5 語言的 `<link rel="alternate" hreflang>` + `x-default`（指向 en）。
- **canonical**：每頁自我 canonical 到 `SITE_URL` + 該 locale 路徑。
- **sitemap**：`@astrojs/sitemap`，含所有 locale，並設 i18n alternate。
- **robots.txt**：允許全爬，指向 sitemap。
- **結構化資料**：首頁注入 `SoftwareApplication` JSON-LD（名稱、作業系統、價格 0、授權 MIT、截圖、`softwareVersion`）。
- **OG / Twitter**：每 locale 一張 OG 圖（或共用一張帶 logo 的 1200×630），`og:title`/`description`/`image`/`twitter:card=summary_large_image`。
- **效能即 SEO**：Astro 預設零 JS；圖片用 `astro:assets` 壓縮並產 width/height 防 CLS；字體 `display=swap` + preconnect。

---

## 7. 資產

- 截圖沿用 repo `/image/`（DPS、承受傷害、關卡比較、刷圖規劃、合成 Hero 圖）。建置時經 `astro:assets` 最佳化。
- 需新增：favicon、OG 分享圖、logo 標記（設計稿用漸層方塊 + ⚔，可沿用或請使用者確認）。

---

## 8. 測試與驗收

站很小，測試聚焦在「不破」與「SEO 正確」：

1. `astro build` 成功、無壞連結（可加 `astro-broken-link-checker` 或建置後簡單檢查）。
2. 自動檢查：每個 locale 首頁存在、hreflang 數量正確、canonical 指向自身、sitemap 含全 locale。（用一支小 Node 腳本掃 `dist/`。）
3. 手動：Lighthouse SEO ≧ 95；用 Google Rich Results Test 驗 JSON-LD；5 語言語言切換器互連正確。
4. changelog：以一次真實 release 驗證 Actions → Zeabur 重建 → 頁面出現新版。

---

## 9. 待確認 / 風險

- **網域**：`SITE_URL` 需一個正式網域才能讓 canonical/hreflang/sitemap 生效。未定前可先用 Zeabur 子網域，定案後改一處設定。
- **GitHub repo 是否公開**：changelog 走未登入 API（每 IP 每小時 60 次），建置時抓足夠；若 repo 私有需另帶 token。
- **遊戲 ToS**：使用者已評估為「只做監控、未違反」；網站免責聲明沿用 README 既有條款放頁尾。
- **翻譯品質**：5 語言文案初版可由我產出，建議使用者（尤其日/西）至少掃一遍。

---

## 10. 實作里程碑（供後續 writing-plans 展開）

1. `/site` 建立 Astro 專案骨架 + i18n 設定 + 設計稿的 CSS 變數。
2. 共用元件（Layout/Nav/Footer/Screenshot/LanguageSwitcher）。
3. 首頁區塊元件 + en 文案，套真實截圖。
4. SEO 模組（seo.ts、sitemap、robots、JSON-LD、OG）。
5. `/install` 與 `/changelog`（含 releases.ts 建置期抓取）。
6. 其餘 4 語言字典與文案。
7. Zeabur 部署設定 + GitHub Actions（release → 重建）。
8. 驗收腳本 + Lighthouse/Rich Results 檢查。

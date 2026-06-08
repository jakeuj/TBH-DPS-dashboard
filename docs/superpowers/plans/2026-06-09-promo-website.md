# TBH DPS Meter 推廣網站 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `/site` 建一個 Astro 靜態網站，主打 SEO、5 語言（繁簡中英日西），推廣 TBH DPS Meter 外掛並提供下載、安裝教學與自動更新日誌。

**Architecture:** Astro 靜態站，預設語言英文（root `/`），其餘語言加前綴。文案集中於 i18n 字典，所有頁面共用元件。SEO（hreflang/canonical/JSON-LD）由純函式 `seo.ts` 產生；更新日誌由 `releases.ts` 在建置時抓 GitHub Releases API。輸出 `dist/` 部署到 Zeabur；GitHub Actions 在 release 時觸發重建。

**Tech Stack:** Astro 4、TypeScript、Vitest（單元測試）、`@astrojs/sitemap`、`marked`（release notes markdown→HTML）、手寫 CSS。

---

## File Structure

```
site/
  astro.config.mjs          # Astro + i18n + sitemap 設定
  package.json
  tsconfig.json
  vitest.config.ts
  public/
    robots.txt
    favicon.svg
    og.png                  # 1200x630 分享圖（先佔位，後換）
  src/
    config.ts               # SITE_URL、GitHub owner/repo、LOCALES、最新版號常數
    i18n/
      types.ts              # Dict 型別
      en.ts  zh-Hant.ts  zh-Hans.ts  ja.ts  es.ts
      index.ts              # getDict(locale) / locale 工具
    lib/
      seo.ts                # 純函式：title/desc/canonical/hreflang/JSON-LD
      seo.test.ts
      releases.ts           # 建置時抓 + 解析 GitHub Releases
      releases.test.ts
    styles/
      global.css            # 設計稿的 CSS 變數 + 基礎樣式
    components/
      BaseLayout.astro  Nav.astro  Footer.astro  LanguageSwitcher.astro
      Screenshot.astro  Hero.astro  StatsBand.astro  FeatureRow.astro
      InstallSteps.astro  Faq.astro  FinalCta.astro
    pages/
      index.astro                 # en 首頁
      install.astro  changelog.astro
      [locale]/index.astro        # 其他語言首頁
      [locale]/install.astro  [locale]/changelog.astro
  scripts/
    check-seo.mjs           # 建置後掃 dist/ 驗 hreflang/canonical/sitemap
  .github/workflows/
    (在 repo 根) deploy-site.yml  # release → 觸發 Zeabur 重建
```

設計稿（已驗證）：`.superpowers/brainstorm/landing-pro-v1.html` — 元件實作時對照它的版型與配色。

---

## Task 1: Astro 專案骨架 + 設定

**Files:**
- Create: `site/package.json`, `site/astro.config.mjs`, `site/tsconfig.json`, `site/src/config.ts`

- [ ] **Step 1: 在 /site 初始化 Astro（最小範本）**

Run（在 repo 根 `D:\code\TBHdpsmod`）:
```bash
cd site 2>/dev/null || mkdir site
npm create astro@latest site -- --template minimal --no-install --no-git --typescript strict --yes
```
Expected: `site/` 出現 `package.json`、`astro.config.mjs`、`src/pages/index.astro`。

- [ ] **Step 2: 安裝相依**

Run:
```bash
cd site && npm install && npm install @astrojs/sitemap marked && npm install -D vitest
```
Expected: 安裝成功，`node_modules` 建立。

- [ ] **Step 3: 寫 config.ts**

Create `site/src/config.ts`:
```ts
export const SITE_URL = process.env.SITE_URL ?? 'https://tbh-dps-meter.zeabur.app';
export const GITHUB_OWNER = 'warmbed';
export const GITHUB_REPO = 'TBHdpsmod';
export const LATEST_VERSION = 'v0.5.7';
export const DEFAULT_LOCALE = 'en' as const;
export const LOCALES = ['en', 'zh-Hant', 'zh-Hans', 'ja', 'es'] as const;
export type Locale = (typeof LOCALES)[number];
```

- [ ] **Step 4: 設定 astro.config.mjs（i18n + sitemap）**

Replace `site/astro.config.mjs`:
```js
import { defineConfig } from 'astro/config';
import sitemap from '@astrojs/sitemap';

export default defineConfig({
  site: process.env.SITE_URL ?? 'https://tbh-dps-meter.zeabur.app',
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'zh-Hant', 'zh-Hans', 'ja', 'es'],
    routing: { prefixDefaultLocale: false },
  },
  integrations: [sitemap()],
});
```

- [ ] **Step 5: 驗證建置**

Run: `cd site && npm run build`
Expected: build 成功，產生 `site/dist/index.html` 與 `sitemap-index.xml`。

- [ ] **Step 6: Commit**

```bash
cd /d/code/TBHdpsmod
git add site/.gitignore site/package.json site/package-lock.json site/astro.config.mjs site/tsconfig.json site/src/config.ts
git commit -m "chore(site): scaffold Astro project with i18n + sitemap"
```

---

## Task 2: SEO 純函式（TDD）

**Files:**
- Create: `site/src/lib/seo.ts`, `site/src/lib/seo.test.ts`, `site/vitest.config.ts`

- [ ] **Step 1: 設定 vitest**

Create `site/vitest.config.ts`:
```ts
import { defineConfig } from 'vitest/config';
export default defineConfig({ test: { include: ['src/**/*.test.ts'] } });
```
Add to `site/package.json` `"scripts"`: `"test": "vitest run"`.

- [ ] **Step 2: 寫失敗測試**

Create `site/src/lib/seo.test.ts`:
```ts
import { describe, it, expect } from 'vitest';
import { localizedPath, hreflangLinks, canonical } from './seo';

describe('localizedPath', () => {
  it('default locale en has no prefix', () => {
    expect(localizedPath('en', '/install')).toBe('/install');
    expect(localizedPath('en', '/')).toBe('/');
  });
  it('non-default locale is prefixed', () => {
    expect(localizedPath('zh-Hant', '/install')).toBe('/zh-Hant/install');
    expect(localizedPath('ja', '/')).toBe('/ja');
  });
});

describe('canonical', () => {
  it('joins site url + localized path', () => {
    expect(canonical('https://x.com', 'zh-Hant', '/')).toBe('https://x.com/zh-Hant');
    expect(canonical('https://x.com', 'en', '/install')).toBe('https://x.com/install');
  });
});

describe('hreflangLinks', () => {
  it('emits all 5 locales + x-default', () => {
    const links = hreflangLinks('https://x.com', '/install');
    expect(links).toHaveLength(6);
    expect(links).toContainEqual({ hreflang: 'en', href: 'https://x.com/install' });
    expect(links).toContainEqual({ hreflang: 'zh-Hant', href: 'https://x.com/zh-Hant/install' });
    expect(links).toContainEqual({ hreflang: 'x-default', href: 'https://x.com/install' });
  });
});
```

- [ ] **Step 3: 跑測試確認失敗**

Run: `cd site && npm test`
Expected: FAIL（找不到 `./seo`）。

- [ ] **Step 4: 實作 seo.ts**

Create `site/src/lib/seo.ts`:
```ts
import { LOCALES, DEFAULT_LOCALE, type Locale } from '../config';

export function localizedPath(locale: Locale, path: string): string {
  const clean = path === '/' ? '' : path;
  if (locale === DEFAULT_LOCALE) return clean === '' ? '/' : clean;
  return `/${locale}${clean}`;
}

export function canonical(siteUrl: string, locale: Locale, path: string): string {
  const base = siteUrl.replace(/\/$/, '');
  const p = localizedPath(locale, path);
  return p === '/' ? base : base + p;
}

export interface HreflangLink { hreflang: string; href: string; }

export function hreflangLinks(siteUrl: string, path: string): HreflangLink[] {
  const links: HreflangLink[] = LOCALES.map((l) => ({
    hreflang: l,
    href: canonical(siteUrl, l, path),
  }));
  links.push({ hreflang: 'x-default', href: canonical(siteUrl, DEFAULT_LOCALE, path) });
  return links;
}

export interface SoftwareAppJsonLd { '@context': string; '@type': string; name: string;
  applicationCategory: string; operatingSystem: string; softwareVersion: string;
  offers: { '@type': string; price: string; priceCurrency: string };
  license: string; screenshot: string[]; }

export function softwareAppJsonLd(opts: {
  name: string; version: string; screenshots: string[];
}): SoftwareAppJsonLd {
  return {
    '@context': 'https://schema.org', '@type': 'SoftwareApplication',
    name: opts.name, applicationCategory: 'GameApplication', operatingSystem: 'Windows',
    softwareVersion: opts.version,
    offers: { '@type': 'Offer', price: '0', priceCurrency: 'USD' },
    license: 'https://opensource.org/licenses/MIT', screenshot: opts.screenshots,
  };
}
```

- [ ] **Step 5: 跑測試確認通過**

Run: `cd site && npm test`
Expected: PASS（5 test 全綠）。

- [ ] **Step 6: Commit**

```bash
git add site/src/lib/seo.ts site/src/lib/seo.test.ts site/vitest.config.ts site/package.json
git commit -m "feat(site): SEO helpers (localizedPath, canonical, hreflang, JSON-LD) + tests"
```

---

## Task 3: i18n 字典骨架

**Files:**
- Create: `site/src/i18n/types.ts`, `site/src/i18n/index.ts`, `site/src/i18n/en.ts`

- [ ] **Step 1: 定義 Dict 型別**

Create `site/src/i18n/types.ts`:
```ts
export interface Dict {
  meta: { homeTitle: string; homeDesc: string; installTitle: string; installDesc: string;
          changelogTitle: string; changelogDesc: string };
  nav: { features: string; install: string; faq: string; download: string };
  hero: { eyebrow: string; titleA: string; titleHighlight: string; lede: string;
          ctaDownload: string; ctaGithub: string;
          trust: { mit: string; readonly: string; langs: string; tested: string } };
  stats: { damageTypes: string; panels: string; languages: string; openSource: string };
  featuresKicker: string; featuresTitle: string; featuresSub: string;
  features: { tag: string; title: string; body: string; points: string[] }[];
  install: { kicker: string; title: string; sub: string;
             steps: { title: string; body: string }[]; full: string };
  faq: { kicker: string; title: string; items: { q: string; a: string }[] };
  finalCta: { title: string; sub: string };
  footer: { license: string; disclaimer: string; disclaimerLong: string };
  changelog: { title: string; intro: string; fallback: string };
}
```

- [ ] **Step 2: 寫 en 字典（完整文案）**

Create `site/src/i18n/en.ts` — 依設計稿 `landing-pro-v1.html` 的英文對應文案填滿 `Dict` 所有欄位（features 陣列含 4 項：DPS / Damage taken / Stage compare / Farming planner，各含 tag、title、body、3 條 points；faq 含 4 題）。實作時逐欄對照設計稿，不可留空字串。

- [ ] **Step 3: 寫 index.ts（getDict）**

Create `site/src/i18n/index.ts`:
```ts
import type { Locale } from '../config';
import type { Dict } from './types';
import en from './en';
const dicts: Partial<Record<Locale, Dict>> = { en };
export function getDict(locale: Locale): Dict {
  return dicts[locale] ?? en;
}
export async function loadDicts() {
  const [zhHant, zhHans, ja, es] = await Promise.all([
    import('./zh-Hant'), import('./zh-Hans'), import('./ja'), import('./es'),
  ]);
  dicts['zh-Hant'] = zhHant.default; dicts['zh-Hans'] = zhHans.default;
  dicts['ja'] = ja.default; dicts['es'] = es.default;
}
```
> 注意：其他 4 語言檔在 Task 8 建立；此時 `loadDicts` 會因檔案不存在而無法 build。為避免阻塞，先讓 `index.ts` 只引 `en`，`loadDicts` 留到 Task 8 補。改為：

Create `site/src/i18n/index.ts`（Task 3 版本）:
```ts
import type { Locale } from '../config';
import type { Dict } from './types';
import en from './en';
const dicts: Record<string, Dict> = { en };
export function getDict(locale: Locale): Dict {
  return dicts[locale] ?? en;
}
export function registerDict(locale: Locale, d: Dict) { dicts[locale] = d; }
```

- [ ] **Step 4: 型別檢查通過**

Run: `cd site && npx astro check`
Expected: 0 errors（en 字典符合 Dict 型別）。

- [ ] **Step 5: Commit**

```bash
git add site/src/i18n/
git commit -m "feat(site): i18n dict types + English dictionary"
```

---

## Task 4: 全域樣式 + BaseLayout + Screenshot

**Files:**
- Create: `site/src/styles/global.css`, `site/src/components/BaseLayout.astro`, `site/src/components/Screenshot.astro`

- [ ] **Step 1: global.css**

Create `site/src/styles/global.css` — 從設計稿 `landing-pro-v1.html` 的 `<style>` 抽出 `:root` 變數、`body`、`.wrap`、`.btn`、`nav`、`.hero`、`.win`、`.stats`、`.section`、`.feat`、`.steps`、`.faq`、`.final`、`footer` 與 RWD media query，原樣搬入（移除假視窗 `.winbar` 規則，保留 `.win` 圓角/邊框/陰影）。

- [ ] **Step 2: Screenshot.astro**

Create `site/src/components/Screenshot.astro`:
```astro
---
interface Props { src: ImageMetadata; alt: string; }
const { src, alt } = Astro.props;
import { Image } from 'astro:assets';
---
<div class="win"><Image src={src} alt={alt} /></div>
```

- [ ] **Step 3: BaseLayout.astro（head + SEO）**

Create `site/src/components/BaseLayout.astro`:
```astro
---
import '../styles/global.css';
import { SITE_URL, LATEST_VERSION, type Locale } from '../config';
import { canonical, hreflangLinks, softwareAppJsonLd } from '../lib/seo';
import Nav from './Nav.astro';
import Footer from './Footer.astro';
import { getDict } from '../i18n';
interface Props { locale: Locale; path: string; title: string; description: string; jsonLd?: boolean; }
const { locale, path, title, description, jsonLd = false } = Astro.props;
const dict = getDict(locale);
const url = canonical(SITE_URL, locale, path);
const alternates = hreflangLinks(SITE_URL, path);
const ld = jsonLd ? softwareAppJsonLd({ name: 'TBH DPS Meter', version: LATEST_VERSION,
  screenshots: [`${SITE_URL}/og.png`] }) : null;
---
<!doctype html>
<html lang={locale}>
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{title}</title>
  <meta name="description" content={description} />
  <link rel="canonical" href={url} />
  {alternates.map((a) => <link rel="alternate" hreflang={a.hreflang} href={a.href} />)}
  <meta property="og:title" content={title} />
  <meta property="og:description" content={description} />
  <meta property="og:url" content={url} />
  <meta property="og:image" content={`${SITE_URL}/og.png`} />
  <meta property="og:type" content="website" />
  <meta name="twitter:card" content="summary_large_image" />
  <link rel="icon" href="/favicon.svg" type="image/svg+xml" />
  <link rel="preconnect" href="https://fonts.googleapis.com" />
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700;800;900&family=JetBrains+Mono:wght@600;700&display=swap" rel="stylesheet" />
  {ld && <script type="application/ld+json" set:html={JSON.stringify(ld)} />}
</head>
<body>
  <Nav locale={locale} path={path} />
  <slot />
  <Footer locale={locale} />
</body>
</html>
```
> 注意：Nav 與 Footer 在 Task 5 建立；本任務先建 BaseLayout，Task 5 完成後 build 才會綠。可先建 Nav/Footer 空殼避免阻塞——見 Task 5 Step 1。

- [ ] **Step 4: Commit**

```bash
git add site/src/styles/global.css site/src/components/BaseLayout.astro site/src/components/Screenshot.astro
git commit -m "feat(site): global styles, BaseLayout with SEO head, Screenshot frame"
```

---

## Task 5: Nav / Footer / LanguageSwitcher

**Files:**
- Create: `site/src/components/Nav.astro`, `site/src/components/Footer.astro`, `site/src/components/LanguageSwitcher.astro`

- [ ] **Step 1: LanguageSwitcher.astro**

Create `site/src/components/LanguageSwitcher.astro`:
```astro
---
import { LOCALES, type Locale } from '../config';
import { localizedPath } from '../lib/seo';
interface Props { locale: Locale; path: string; }
const { locale, path } = Astro.props;
const labels: Record<Locale, string> = { en: 'English', 'zh-Hant': '繁體中文',
  'zh-Hans': '简体中文', ja: '日本語', es: 'Español' };
---
<div class="langsw">
  {LOCALES.map((l) => (
    <a href={localizedPath(l, path)} aria-current={l === locale ? 'true' : undefined}>{labels[l]}</a>
  ))}
</div>
```

- [ ] **Step 2: Nav.astro**

Create `site/src/components/Nav.astro` — 依設計稿 `nav` 結構：logo（漸層方塊 + ⚔ + 「TBH DPS Meter」）、功能/安裝/FAQ 連結（用 `getDict(locale).nav`，href 走 `localizedPath`）、`LanguageSwitcher`、下載鍵（連 GitHub releases latest）。props `{ locale, path }`。

- [ ] **Step 3: Footer.astro**

Create `site/src/components/Footer.astro` — 依設計稿 footer：logo、GitHub / MIT 授權 / 免責 / 語言連結、免責長句（用 `dict.footer.disclaimerLong`）。props `{ locale }`。

- [ ] **Step 4: 加 LanguageSwitcher 樣式到 global.css**

Modify `site/src/styles/global.css` 末尾加：
```css
.langsw{display:flex;gap:12px;font-size:13px}
.langsw a{color:var(--muted)} .langsw a[aria-current]{color:var(--ink);font-weight:600}
```

- [ ] **Step 5: 驗證 build**

Run: `cd site && npm run build`
Expected: build 成功（BaseLayout + Nav + Footer 串起來，雖然還沒有頁面內容）。

- [ ] **Step 6: Commit**

```bash
git add site/src/components/Nav.astro site/src/components/Footer.astro site/src/components/LanguageSwitcher.astro site/src/styles/global.css
git commit -m "feat(site): Nav, Footer, LanguageSwitcher"
```

---

## Task 6: 首頁區塊元件

**Files:**
- Create: `site/src/components/Hero.astro`, `StatsBand.astro`, `FeatureRow.astro`, `InstallSteps.astro`, `Faq.astro`, `FinalCta.astro`
- Create: `site/src/pages/index.astro`
- Assets: 從 repo `/image/` 複製到 `site/src/assets/`

- [ ] **Step 1: 複製截圖到 site assets**

Run:
```bash
mkdir -p site/src/assets
cp image/TaskBarHero_MS3KA9KGlR.jpg site/src/assets/hero.jpg
cp image/TaskBarHero_FkEGMBj3Kq.png site/src/assets/dps.png
cp image/TaskBarHero_3TGLxaOOR2.png site/src/assets/taken.png
cp image/TaskBarHero_5BRF6aiQF5.png site/src/assets/compare.png
cp image/TaskBarHero_5vnzsSLfTW.png site/src/assets/farm.png
```

- [ ] **Step 2: 建立六個區塊元件**

依設計稿 `landing-pro-v1.html` 對應段落，各建一個 `.astro` 元件，皆吃 `{ locale }`（或直接吃 dict 片段）並用 `getDict`：
- `Hero.astro` — eyebrow / h1（titleA + grad titleHighlight）/ lede / 雙 CTA / trustline / Hero 截圖（`Screenshot` 吃 `hero.jpg`）。
- `StatsBand.astro` — 4 個 `.stat`（值寫死 6/4/5/100%，標籤走 dict）。
- `FeatureRow.astro` — props `{ feature: Dict['features'][number]; image: ImageMetadata; reversed: boolean }`，渲染 tag/標題/body/✓清單 + `Screenshot`。
- `InstallSteps.astro` — 3 步驟卡（dict.install.steps）+「看完整教學」連 `localizedPath(locale,'/install')`。
- `Faq.astro` — dict.faq.items 清單。
- `FinalCta.astro` — 深色區 + 雙 CTA。

- [ ] **Step 3: 組 index.astro（en 首頁）**

Create `site/src/pages/index.astro`:
```astro
---
import BaseLayout from '../components/BaseLayout.astro';
import Hero from '../components/Hero.astro';
import StatsBand from '../components/StatsBand.astro';
import FeatureRow from '../components/FeatureRow.astro';
import InstallSteps from '../components/InstallSteps.astro';
import Faq from '../components/Faq.astro';
import FinalCta from '../components/FinalCta.astro';
import { getDict } from '../i18n';
import dps from '../assets/dps.png';
import taken from '../assets/taken.png';
import compare from '../assets/compare.png';
import farm from '../assets/farm.png';
const locale = 'en' as const;
const dict = getDict(locale);
const images = [dps, taken, compare, farm];
---
<BaseLayout locale={locale} path="/" title={dict.meta.homeTitle} description={dict.meta.homeDesc} jsonLd>
  <Hero locale={locale} />
  <StatsBand locale={locale} />
  <section class="section alt" id="features"><div class="wrap">
    <div class="kicker">{dict.featuresKicker}</div>
    <div class="h2">{dict.featuresTitle}</div>
    <p class="sub">{dict.featuresSub}</p>
    {dict.features.map((f, i) => <FeatureRow feature={f} image={images[i]} reversed={i % 2 === 1} />)}
  </div></section>
  <InstallSteps locale={locale} />
  <Faq locale={locale} />
  <FinalCta locale={locale} />
</BaseLayout>
```

- [ ] **Step 4: 驗證 build + 視覺**

Run: `cd site && npm run build && npm run preview`
Expected: build 成功；瀏覽 `http://localhost:4321/` 應與設計稿一致（淺色、四功能交錯、結尾深色 CTA、文字顏色正常）。

- [ ] **Step 5: Commit**

```bash
git add site/src/components/ site/src/pages/index.astro site/src/assets/
git commit -m "feat(site): homepage sections + English home page"
```

---

## Task 7: GitHub Releases → changelog（TDD 解析）

**Files:**
- Create: `site/src/lib/releases.ts`, `site/src/lib/releases.test.ts`
- Create: `site/src/pages/changelog.astro`, `site/src/pages/install.astro`

- [ ] **Step 1: 寫失敗測試（解析邏輯）**

Create `site/src/lib/releases.test.ts`:
```ts
import { describe, it, expect } from 'vitest';
import { parseReleases } from './releases';

const raw = [
  { tag_name: 'v0.5.7', published_at: '2026-06-08T00:00:00Z', name: 'v0.5.7',
    body: '# Fixes\n- only swallow clicks when foreground', draft: false, prerelease: false },
  { tag_name: 'v0.5.6', published_at: '2026-06-07T00:00:00Z', name: '',
    body: 'in-panel update', draft: false, prerelease: false },
  { tag_name: 'v0.5.5-rc', published_at: '2026-06-06T00:00:00Z', name: 'rc',
    body: 'x', draft: false, prerelease: true },
];

describe('parseReleases', () => {
  it('drops drafts and prereleases, keeps order', () => {
    const r = parseReleases(raw);
    expect(r.map((x) => x.tag)).toEqual(['v0.5.7', 'v0.5.6']);
  });
  it('renders markdown body to html', () => {
    const r = parseReleases(raw);
    expect(r[0].html).toContain('<li>');
  });
  it('falls back to tag when name empty', () => {
    const r = parseReleases(raw);
    expect(r[1].title).toBe('v0.5.6');
  });
});
```

- [ ] **Step 2: 跑測試確認失敗**

Run: `cd site && npm test`
Expected: FAIL（找不到 `parseReleases`）。

- [ ] **Step 3: 實作 releases.ts**

Create `site/src/lib/releases.ts`:
```ts
import { marked } from 'marked';
import { GITHUB_OWNER, GITHUB_REPO } from '../config';

export interface RawRelease { tag_name: string; name: string; published_at: string;
  body: string; draft: boolean; prerelease: boolean; }
export interface Release { tag: string; title: string; date: string; html: string; }

export function parseReleases(raw: RawRelease[]): Release[] {
  return raw
    .filter((r) => !r.draft && !r.prerelease)
    .map((r) => ({
      tag: r.tag_name,
      title: r.name?.trim() ? r.name : r.tag_name,
      date: r.published_at.slice(0, 10),
      html: marked.parse(r.body ?? '', { async: false }) as string,
    }));
}

export async function fetchReleases(): Promise<Release[]> {
  try {
    const res = await fetch(
      `https://api.github.com/repos/${GITHUB_OWNER}/${GITHUB_REPO}/releases?per_page=50`,
      { headers: { Accept: 'application/vnd.github+json' } },
    );
    if (!res.ok) return [];
    return parseReleases((await res.json()) as RawRelease[]);
  } catch {
    return [];
  }
}
```

- [ ] **Step 4: 跑測試確認通過**

Run: `cd site && npm test`
Expected: PASS（seo + releases 全綠）。

- [ ] **Step 5: changelog.astro（en，建置時抓）**

Create `site/src/pages/changelog.astro`:
```astro
---
import BaseLayout from '../components/BaseLayout.astro';
import { getDict } from '../i18n';
import { fetchReleases } from '../lib/releases';
const locale = 'en' as const;
const dict = getDict(locale);
const releases = await fetchReleases();
---
<BaseLayout locale={locale} path="/changelog" title={dict.meta.changelogTitle} description={dict.meta.changelogDesc}>
  <section class="section"><div class="wrap" style="max-width:760px">
    <div class="kicker">{dict.changelog.title}</div>
    <div class="h2">{dict.changelog.title}</div>
    <p class="sub">{dict.changelog.intro}</p>
    {releases.length === 0 && <p>{dict.changelog.fallback}</p>}
    {releases.map((r) => (
      <article class="qa">
        <h4>{r.title} <span>{r.date}</span></h4>
        <div set:html={r.html} />
      </article>
    ))}
  </div></section>
</BaseLayout>
```

- [ ] **Step 6: install.astro（en，完整教學）**

Create `site/src/pages/install.astro` — 用 BaseLayout，內容依 README 的 Install 段（首次安裝 A、更新 B、黑畫面、解除安裝）轉成區塊，文字走 `dict`（install 教學文字可放 `dict.install` 既有欄位 + 視需要在 Task 8 擴充；本步驟先用英文 README 內容硬寫於頁面亦可，但優先走 dict）。

- [ ] **Step 7: 驗證 build**

Run: `cd site && npm run build`
Expected: build 成功；`dist/changelog/index.html` 含真實 release 內容（若有網路），`dist/install/index.html` 存在。

- [ ] **Step 8: Commit**

```bash
git add site/src/lib/releases.ts site/src/lib/releases.test.ts site/src/pages/changelog.astro site/src/pages/install.astro
git commit -m "feat(site): changelog from GitHub releases (build-time) + install page"
```

---

## Task 8: 其餘 4 語言 + 動態 locale 路由

**Files:**
- Create: `site/src/i18n/zh-Hant.ts`, `zh-Hans.ts`, `ja.ts`, `es.ts`
- Modify: `site/src/i18n/index.ts`
- Create: `site/src/pages/[locale]/index.astro`, `[locale]/install.astro`, `[locale]/changelog.astro`

- [ ] **Step 1: 翻譯 4 語言字典**

依 `en.ts` 結構，逐一建立 `zh-Hant.ts`、`zh-Hans.ts`、`ja.ts`、`es.ts`，每個 `export default` 一個符合 `Dict` 的物件。繁中文案以設計稿 `landing-pro-v1.html` 為準；簡中由繁中轉換並校對用詞（檔案/資料夾→文件/文件夹等）；日/西翻譯後標記待使用者校對。

- [ ] **Step 2: index.ts 改為靜態引入全部字典**

Replace `site/src/i18n/index.ts`:
```ts
import type { Locale } from '../config';
import type { Dict } from './types';
import en from './en';
import zhHant from './zh-Hant';
import zhHans from './zh-Hans';
import ja from './ja';
import es from './es';
const dicts: Record<Locale, Dict> = { en, 'zh-Hant': zhHant, 'zh-Hans': zhHans, ja, es };
export function getDict(locale: Locale): Dict { return dicts[locale]; }
```

- [ ] **Step 3: 動態 locale 首頁**

Create `site/src/pages/[locale]/index.astro`:
```astro
---
import BaseLayout from '../../components/BaseLayout.astro';
import Hero from '../../components/Hero.astro';
import StatsBand from '../../components/StatsBand.astro';
import FeatureRow from '../../components/FeatureRow.astro';
import InstallSteps from '../../components/InstallSteps.astro';
import Faq from '../../components/Faq.astro';
import FinalCta from '../../components/FinalCta.astro';
import { getDict } from '../../i18n';
import { LOCALES, DEFAULT_LOCALE, type Locale } from '../../config';
import dps from '../../assets/dps.png';
import taken from '../../assets/taken.png';
import compare from '../../assets/compare.png';
import farm from '../../assets/farm.png';
export function getStaticPaths() {
  return LOCALES.filter((l) => l !== DEFAULT_LOCALE).map((locale) => ({ params: { locale } }));
}
const locale = Astro.params.locale as Locale;
const dict = getDict(locale);
const images = [dps, taken, compare, farm];
---
<BaseLayout locale={locale} path="/" title={dict.meta.homeTitle} description={dict.meta.homeDesc} jsonLd>
  <Hero locale={locale} />
  <StatsBand locale={locale} />
  <section class="section alt" id="features"><div class="wrap">
    <div class="kicker">{dict.featuresKicker}</div>
    <div class="h2">{dict.featuresTitle}</div>
    <p class="sub">{dict.featuresSub}</p>
    {dict.features.map((f, i) => <FeatureRow feature={f} image={images[i]} reversed={i % 2 === 1} />)}
  </div></section>
  <InstallSteps locale={locale} />
  <Faq locale={locale} />
  <FinalCta locale={locale} />
</BaseLayout>
```

- [ ] **Step 4: 動態 locale install / changelog**

Create `site/src/pages/[locale]/install.astro` 與 `[locale]/changelog.astro`，結構同 en 版但用 `getStaticPaths`（同 Step 3 過濾預設語言）取 `Astro.params.locale`；changelog 同樣 `await fetchReleases()`（release 內容共用、UI 字串走該 locale dict）。

- [ ] **Step 5: 型別檢查 + build**

Run: `cd site && npx astro check && npm run build`
Expected: 0 type error；`dist/` 出現 `zh-Hant/`、`zh-Hans/`、`ja/`、`es/` 各含 index/install/changelog。

- [ ] **Step 6: Commit**

```bash
git add site/src/i18n/ "site/src/pages/[locale]/"
git commit -m "feat(site): 4 locale dictionaries + dynamic locale routes"
```

---

## Task 9: robots / OG / favicon + SEO 驗收腳本

**Files:**
- Create: `site/public/robots.txt`, `site/public/favicon.svg`, `site/public/og.png`, `site/scripts/check-seo.mjs`

- [ ] **Step 1: robots.txt**

Create `site/public/robots.txt`:
```
User-agent: *
Allow: /
Sitemap: https://tbh-dps-meter.zeabur.app/sitemap-index.xml
```
> 部署網域定案後，把 Sitemap 行改成正式網域（或改用建置時以 SITE_URL 產生）。

- [ ] **Step 2: favicon.svg + og.png 佔位**

Create `site/public/favicon.svg`（漸層方塊 + ⚔，呼應 logo）。`og.png` 先放一張 1200×630 帶 logo 與標題的圖（可由設計稿 Hero 截一張，後續再換正式版）。

- [ ] **Step 3: 寫 SEO 驗收腳本**

Create `site/scripts/check-seo.mjs`:
```js
import { readFile } from 'node:fs/promises';
import { glob } from 'node:fs/promises'; // node 22+: fs.glob
const fail = (m) => { console.error('FAIL:', m); process.exitCode = 1; };

const locales = ['', 'zh-Hant/', 'zh-Hans/', 'ja/', 'es/'];
for (const l of locales) {
  const f = `dist/${l}index.html`;
  let html;
  try { html = await readFile(f, 'utf8'); } catch { fail(`missing ${f}`); continue; }
  if (!/rel="canonical"/.test(html)) fail(`${f}: no canonical`);
  const hreflangs = (html.match(/hreflang="/g) || []).length;
  if (hreflangs < 6) fail(`${f}: expected >=6 hreflang, got ${hreflangs}`);
}
// sitemap exists
try { await readFile('dist/sitemap-index.xml', 'utf8'); }
catch { fail('missing sitemap-index.xml'); }
if (!process.exitCode) console.log('SEO checks passed');
```
Add `site/package.json` script: `"check:seo": "node scripts/check-seo.mjs"`.

- [ ] **Step 4: 跑驗收**

Run: `cd site && npm run build && npm run check:seo`
Expected: 印出 `SEO checks passed`，exit 0。

- [ ] **Step 5: Commit**

```bash
git add site/public/ site/scripts/check-seo.mjs site/package.json
git commit -m "feat(site): robots, favicon, og image, SEO verification script"
```

---

## Task 10: Zeabur 部署 + GitHub Actions（release → 重建）

**Files:**
- Create: `.github/workflows/deploy-site.yml`（repo 根）
- Modify: `README.md`（加網站連結）

- [ ] **Step 1: 確認 Zeabur 靜態部署設定**

在 Zeabur 建立服務指向此 repo，root 設 `site`，build command `npm run build`，output `dist`。設定環境變數 `SITE_URL` 為正式網域（暫用 Zeabur 子網域）。記下 Zeabur 的 Deploy Hook URL（用於 release 觸發）。
> 參考 `infra-api` / `zeabur-deploy` skill 取得 API 與重新部署方式。

- [ ] **Step 2: GitHub Actions 在 release 時觸發重建**

Create `.github/workflows/deploy-site.yml`:
```yaml
name: Rebuild site on release
on:
  release:
    types: [published]
jobs:
  trigger:
    runs-on: ubuntu-latest
    steps:
      - name: Trigger Zeabur redeploy
        run: curl -fsSL -X POST "${{ secrets.ZEABUR_DEPLOY_HOOK }}"
```
在 GitHub repo Secrets 加入 `ZEABUR_DEPLOY_HOOK`。

- [ ] **Step 3: 驗證部署**

Run（手動觸發一次）: 推送後在 Zeabur 確認 build 成功、網站可開、5 語言路徑都在、changelog 顯示真實 release。
> 用 `zeabur-deploy` skill 監看 build log。

- [ ] **Step 4: README 加網站連結**

Modify `README.md` 頂部加一行指向正式網站（網域定案後填）。

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/deploy-site.yml README.md
git commit -m "ci(site): rebuild on release + link site from README"
```

---

## Self-Review 紀錄

- **Spec 覆蓋：** 目標(Task1-10)、5 語言(T3,T8)、SEO head/hreflang/canonical/JSON-LD(T2,T4)、sitemap/robots(T1,T9)、changelog 建置時抓+Actions 重建(T7,T10)、安裝頁(T7)、效能用 astro:assets(T6)、驗收(T9)、ToS 免責放 footer(T5) — 皆有對應任務。
- **Placeholder 掃描：** 文案類步驟（en/4 語言字典、install、Nav/Footer 內容）以「對照設計稿/ README 逐欄填滿、不可留空」描述，因內容量大且來源明確（設計稿 + README），不展開逐字；其餘程式碼步驟均附完整可執行碼。
- **型別一致：** `Locale`、`Dict`、`getDict`、`localizedPath`、`canonical`、`hreflangLinks`、`parseReleases`/`fetchReleases`/`Release` 跨任務命名一致。
- **已知相依順序：** BaseLayout(T4) 依 Nav/Footer(T5)、首頁(T6) 依區塊元件、[locale] 路由(T8) 依 en 頁與全字典 — 任務順序已排好，並在 T4/T3 註記避免 build 阻塞的處理。

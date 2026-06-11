// Build the web Terminal dataset from OUR Steam data (prices.json + history.json, written by
// fetch-prices.mjs earlier in the same job) — reliable, USD cents, consistent with the F4 price-peek.
// The ONLY thing fetched from tbh-market is the live ORDER BOOK (Steam's is login-gated), and that
// goes through the Cloudflare Worker proxy (datacenter IPs are 403'd by tbh-market directly).
import { readFile, writeFile, mkdir } from 'node:fs/promises';

const UA = 'tbh-market-cron';
const BASE = (process.env.PROXY_BASE || 'https://tbh-market.com').replace(/\/+$/, '');
const PROXY_KEY = process.env.PROXY_KEY || '';
const ORDERBOOK = `${BASE}/api/orderbook/`;
const MAX_DETAIL = Number(process.env.MAX_DETAIL || 140);   // items with a baked order book (top-N by volume)
const OB_SLEEP = Number(process.env.OB_SLEEP || 500);
const SPARK_N = 24;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function jget(url, tries = 3) {
  const h = { 'User-Agent': UA }; if (PROXY_KEY) h['x-proxy-key'] = PROXY_KEY;
  let last;
  for (let i = 0; i < tries; i++) {
    try { const r = await fetch(url, { headers: h }); if (r.ok) return r.json(); last = new Error(`${r.status} ${url}`); if (r.status >= 400 && r.status < 500 && r.status !== 429) throw last; }
    catch (e) { last = e; }
    if (i < tries - 1) await sleep(1500 * (i + 1));   // transient (CF cold-start 1042 / 5xx) — retry
  }
  throw last;
}
function slug(s) { let h = 5381; for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) >>> 0; return h.toString(36); }
function clampSpike(v) { const s = v.slice().sort((a, b) => a - b); const cap = s[Math.floor(s.length * 0.95)] || s[s.length - 1] || 1; return v.map((x) => Math.min(x, cap)); }
function sparkOf(hist) { if (!hist || hist.length < 2) return null; const v = clampSpike(hist.map((p) => p[1])); const out = []; for (let i = 0; i < SPARK_N; i++) out.push(v[Math.round(i / (SPARK_N - 1) * (v.length - 1))]); return out; }

const now = Date.now();
const prices = JSON.parse(await readFile('prices.json', 'utf8'));
const items = prices.items || {};

// Official localized item names from the plugin's embedded wiki data (DpsMeter/item_names.json, keyed by
// en-US == the Steam hash) + the grade map (Localization.cs). Materials match directly; gear hashes
// "Base (Rarity) A" are composed from the localized base + localized rarity.
let byEn = {};
try {
  const nm = JSON.parse(await readFile(new URL('../DpsMeter/item_names.json', import.meta.url), 'utf8'));
  for (const k in nm) { const e = nm[k]['en-US']; if (e && !byEn[e]) byEn[e] = nm[k]; }
  console.log(`item_names: ${Object.keys(byEn).length} en-US names`);
} catch (e) { console.warn('item_names.json: ' + e.message); }
const RARITY = {
  common: { 'zh-Hant': '普通', 'zh-Hans': '普通', ja: 'コモン', es: 'Común' },
  uncommon: { 'zh-Hant': '罕見', 'zh-Hans': '罕见', ja: 'アンコモン', es: 'Infrecuente' },
  rare: { 'zh-Hant': '稀有', 'zh-Hans': '稀有', ja: 'レア', es: 'Raro' },
  legendary: { 'zh-Hant': '傳奇', 'zh-Hans': '传奇', ja: 'レジェンダリー', es: 'Legendario' },
  immortal: { 'zh-Hant': '不朽', 'zh-Hans': '不朽', ja: 'イモータル', es: 'Inmortal' },
  arcana: { 'zh-Hant': '至寶', 'zh-Hans': '至宝', ja: '至宝', es: 'Tesoro' },
  beyond: { 'zh-Hant': '超凡', 'zh-Hans': '超凡', ja: '超凡', es: 'Trascendente' },
  celestial: { 'zh-Hant': '天界', 'zh-Hans': '天界', ja: 'セレスティアル', es: 'Celestial' },
  divine: { 'zh-Hant': '神聖', 'zh-Hans': '神圣', ja: 'ディヴァイン', es: 'Divino' },
  cosmic: { 'zh-Hant': '宇宙', 'zh-Hans': '宇宙', ja: 'コズミック', es: 'Cósmico' },
};
function localized(hash) {
  const d = byEn[hash];
  if (d) return { 'zh-Hant': d['zh-Hant'], 'zh-Hans': d['zh-Hans'], ja: d['ja-JP'], es: d['es-ES'] };
  const m = /^(.+) \(([^)]+)\) (\S+)$/.exec(hash);
  if (m) {
    const base = byEn[m[1]], rar = RARITY[m[2].toLowerCase()], suf = m[3];
    if (base && rar) return {
      'zh-Hant': base['zh-Hant'] + ' (' + rar['zh-Hant'] + ') ' + suf,
      'zh-Hans': base['zh-Hans'] + ' (' + rar['zh-Hans'] + ') ' + suf,
      ja: base['ja-JP'] + ' (' + rar.ja + ') ' + suf,
      es: base['es-ES'] + ' (' + rar.es + ') ' + suf,
    };
  }
  return null;
}

// catalog + price/median/vol/listings + spark/chg, all from Steam (USD cents)
const list = Object.keys(items).map((hash) => {
  const v = items[hash];
  const prev = (v.prevCents != null && v.prevCents > 0) ? v.prevCents : null;
  return {
    hash, slug: slug(hash), name: v.dispName || hash, names: localized(hash) || undefined,
    color: v.color || null, icon: v.icon || null, type: v.type || null,
    price: v.lowestCents || 0, median: (v.medianCents != null ? v.medianCents : 0), listings: v.qty || 0, vol: (v.vol != null ? v.vol : 0),
    chg: prev ? Math.round((v.lowestCents - prev) / prev * 1000) / 10 : null,
    spark: sparkOf(v.hist),
  };
});

// detail = our lowest-ask price history (USD cents, matches the header) + tbh order book, for top-N by volume
const byVol = list.slice().sort((a, b) => b.vol - a.vol).slice(0, MAX_DETAIL);
await mkdir('detail', { recursive: true });
let done = 0;
for (const it of byVol) {
  const v = items[it.hash];
  // hist samples ~every 2h; append the current price so the chart's last point == the header price
  const h = (v.hist || []).slice();
  const nowSec = Math.floor(now / 1000);
  if (!h.length || h[h.length - 1][1] !== v.lowestCents) h.push([nowSec, v.lowestCents]);
  let ob = null;
  try { ob = await jget(ORDERBOOK + encodeURIComponent(it.hash)); } catch (e) { console.warn(`orderbook ${it.hash}: ${e.message}`); }
  await writeFile(`detail/${it.slug}.json`, JSON.stringify({ hash: it.hash, slug: it.slug, builtAt: now, hist: h, orderbook: ob }));
  if (ob) done++;
  await sleep(OB_SLEEP);
}

const out = { builtAt: now, total: list.length, count: list.length, currency: '$', list };
await writeFile('market.json', JSON.stringify(out));
console.log(`wrote market.json: ${list.length} items (Steam prices.json); ${done}/${byVol.length} order books via proxy`);

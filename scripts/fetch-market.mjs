// Publish the rich market dataset for the web Terminal (and later the F4 single-item card):
//   market.json          full catalog (list-level fields) from tbh-market /api/items, with a
//                        compact spark + 24h chg carried per item that has detail
//   detail/<slug>.json   steam_history (price+volume) + live orderbook for the top-N by volume
//
// ONLY this cron touches tbh-market; the site/plugin read these from the `data` branch via
// raw.githubusercontent (CORS *). `updated_at` from /api/items lets us skip unchanged items so we
// stay polite to tbh-market. Detail for items we skip persists because the publish step seeds the
// orphan commit from the existing data branch (see .github/workflows/prices.yml).
import { writeFile, mkdir } from 'node:fs/promises';

const REPO = process.env.GITHUB_REPOSITORY || 'WarmBed/TBH-DPS-dashboard';
const UA = `Mozilla/5.0 (tbh-market-cron; +https://github.com/${REPO})`;
// tbh-market 403s datacenter IPs (GitHub Actions), so in CI we go through a Cloudflare Worker proxy
// (CF egress is not blocked). Set PROXY_BASE + PROXY_KEY in CI; locally on a residential IP, leave
// them unset to hit tbh-market directly.
const BASE = (process.env.PROXY_BASE || 'https://tbh-market.com').replace(/\/+$/, '');
const PROXY_KEY = process.env.PROXY_KEY || '';
const ITEMS = `${BASE}/api/items`;
const ITEM = `${BASE}/api/item/`;
const ORDERBOOK = `${BASE}/api/orderbook/`;
const PAGE_SIZE = 500;
const MAX_DETAIL = Number(process.env.MAX_DETAIL || 140);   // detail items considered per run (top-N by volume)
const DETAIL_SLEEP = Number(process.env.DETAIL_SLEEP || 650);
const SPARK_N = 24;
const PREV_MARKET_URL = `https://raw.githubusercontent.com/${REPO}/data/market.json`;

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
async function jget(url, tries = 3) {
  const headers = { 'User-Agent': UA };
  if (PROXY_KEY) headers['x-proxy-key'] = PROXY_KEY;
  let last;
  for (let i = 0; i < tries; i++) {
    try {
      const r = await fetch(url, { headers });
      if (r.ok) return r.json();
      last = new Error(`${r.status} ${url}`);
      if (r.status >= 400 && r.status < 500 && r.status !== 429) throw last;   // hard client error, don't retry
    } catch (e) { last = e; }
    if (i < tries - 1) await sleep(1500 * (i + 1));   // transient (CF cold-start 1042, 5xx, network) — retry
  }
  throw last;
}
// stable, filesystem-safe id for an item hash (djb2 -> base36); also stored in market.json so the
// client never has to recompute it.
function slug(s) { let h = 5381; for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) >>> 0; return h.toString(36); }

// 95th-percentile clamp so a single Steam glitch print doesn't wreck spark/chg
function clampSpike(vals) {
  const s = vals.slice().sort((a, b) => a - b);
  const cap = s[Math.floor(s.length * 0.95)] || s[s.length - 1] || 1;
  return vals.map((x) => Math.min(x, cap));
}
function changePct(hist) {
  const n = hist.length; if (n < 2) return null;
  const span = hist[n - 1][0] - hist[0][0]; if (span <= 0) return null;
  const step = span / (n - 1), back = Math.max(1, Math.round(86400 / step));
  const last = hist[n - 1][1], prev = hist[Math.max(0, n - 1 - back)][1];
  return prev ? Math.round((last - prev) / prev * 1000) / 10 : null;
}
function sparkOf(hist) {
  if (hist.length < 2) return null;
  const v = clampSpike(hist.map((p) => p[1]));
  const out = [];
  for (let i = 0; i < SPARK_N; i++) out.push(Math.round(v[Math.round(i / (SPARK_N - 1) * (v.length - 1))] * 100) / 100);
  return out;
}

// ---- 1. full catalog (paginated). The API caps pageSize at ~96 regardless of what we ask, so
// trust the returned pageSize and page through all of them. ----
const first = await jget(`${ITEMS}?page=1&pageSize=${PAGE_SIZE}`);
const total = first.total || 0;
const PS = (first.items || []).length || first.pageSize || PAGE_SIZE;
const pages = Math.max(1, Math.ceil(total / PS));
let raw = (first.items || []).slice();
for (let p = 2; p <= pages; p++) {
  try { const j = await jget(`${ITEMS}?page=${p}&pageSize=${PAGE_SIZE}`); raw = raw.concat(j.items || []); }
  catch (e) { console.warn(`catalog page ${p}: ${e.message}`); }
  await sleep(250);
}
console.log(`catalog: ${raw.length}/${total} items over ${pages} pages (pageSize ${PS})`);

const now = Date.now();
const list = raw.map((it) => ({
  hash: it.hash_name, slug: slug(it.hash_name), name: it.name, nameJa: it.name_ja || null,
  color: it.name_color || null, icon: it.icon_url || null, type: it.type || null, gear: it.gear || null,
  price: it.sell_price || 0, median: it.median_price || 0, listings: it.sell_listings || 0,
  vol: it.volume || 0, updatedAt: it.updated_at || 0,
}));

// ---- 2. prior market.json: reuse updated_at + spark/chg for items we won't refetch ----
const prev = {};
try { const pj = await jget(PREV_MARKET_URL); (pj.list || []).forEach((x) => { prev[x.hash] = x; }); console.log(`prev market.json: ${Object.keys(prev).length} items`); }
catch { console.log('no prior market.json (first run)'); }

// ---- 3. refresh detail for top-N by volume whose data changed (or that we have no detail for) ----
const byVol = list.slice().sort((a, b) => b.vol - a.vol).slice(0, MAX_DETAIL);
const stale = byVol.filter((it) => { const p = prev[it.hash]; return !p || p.updatedAt !== it.updatedAt || p.spark == null; });
console.log(`detail: ${stale.length} to refresh, ${byVol.length - stale.length} unchanged (top ${MAX_DETAIL})`);

await mkdir('detail', { recursive: true });
const meta = {};   // hash -> { chg, spark }
let done = 0;
for (const it of stale) {
  try {
    const resp = await jget(ITEM + encodeURIComponent(it.hash));
    const item = resp.item || resp;
    let cur = '', hist = [];
    try { const sh = JSON.parse(item.steam_history || '{}'); cur = sh.cur || ''; hist = (sh.points || []).map((p) => [p.recorded_at, p.price, p.volume || 0]); } catch { /* no history */ }
    await sleep(DETAIL_SLEEP);
    let ob = null; try { ob = await jget(ORDERBOOK + encodeURIComponent(it.hash)); } catch { /* ok */ }
    await writeFile(`detail/${it.slug}.json`, JSON.stringify({ hash: it.hash, slug: it.slug, builtAt: now, updatedAt: it.updatedAt, cur, hist, orderbook: ob }));
    meta[it.hash] = { chg: changePct(hist), spark: sparkOf(hist) };
    done++;
  } catch (e) { console.warn(`detail ${it.hash}: ${e.message}`); }
  await sleep(DETAIL_SLEEP);
}

// attach spark/chg: fresh where refetched, carried forward otherwise
for (const it of list) {
  const m = meta[it.hash] || prev[it.hash];
  if (m) { if (m.chg !== undefined) it.chg = m.chg; if (m.spark) it.spark = m.spark; }
}

const out = { builtAt: now, total, count: list.length, currency: '$', list };
await writeFile('market.json', JSON.stringify(out));
console.log(`wrote market.json: ${list.length} items; refreshed ${done}/${stale.length} detail files`);

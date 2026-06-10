// Fetch TaskBarHero (Steam appid 3678970) Community Market prices into prices.json.
// Paged via market/search/render (no login, no per-item priceoverview spam): each result gives
// the lowest sell price (cents) + live listing count. Run by the steam-prices GitHub Action every
// 30 min; the output is published to the `data` branch and served via jsDelivr to the plugin.
import { writeFile } from 'node:fs/promises';

const APPID = 3678970;
const PER = 100;            // Steam caps search/render at 100 per page
const UA = 'Mozilla/5.0 (tbh-prices-cron; +https://github.com/WarmBed/TBH-DPS-dashboard)';

async function page(start) {
  const url = `https://steamcommunity.com/market/search/render/?appid=${APPID}&norender=1&count=${PER}&start=${start}`;
  const res = await fetch(url, { headers: { 'User-Agent': UA } });
  if (!res.ok) throw new Error(`steam HTTP ${res.status} at start=${start}`);
  return res.json();
}

const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const items = {};
let start = 0, total = Infinity, currency = '', guard = 0;
while (start < total && guard++ < 40) {
  let j = null;
  // Steam throttles unauthenticated bursts: a short/empty page is usually rate-limiting, not the end.
  for (let attempt = 0; attempt < 4; attempt++) {
    try { j = await page(start); } catch { j = null; }
    if (j && j.success === true && (j.results?.length || (j.total_count ?? 0) === 0)) break;
    await sleep(3000 * (attempt + 1));   // back off and retry the same start
    j = null;
  }
  if (!j) throw new Error(`steam kept failing at start=${start} (got ${Object.keys(items).length}/${total})`);
  total = j.total_count ?? total;
  const results = j.results ?? [];
  if (results.length === 0) break;       // genuinely no more
  for (const r of results) {
    const name = r.hash_name || r.name;
    if (!name) continue;
    if (!currency && r.sell_price_text) currency = r.sell_price_text.replace(/[0-9.,\s]/g, ''); // e.g. "$"
    items[name] = { lowestCents: r.sell_price ?? 0, qty: r.sell_listings ?? 0 };
  }
  start += results.length;               // advance by what we actually got, not a fixed page size
  if (start < total) await sleep(2000);  // be gentle to Steam between pages
}

// ---- price history (for the client's 波動 / 24h change display) ----
// The data branch is republished as a single orphan commit each run (no git history), so we instead
// keep a rolling window inside history.json and read it back each run from raw.githubusercontent
// (near-real-time, unlike jsDelivr's long cache). We sample sparsely (~2h) and keep ~8 days, then stamp
// each item with prevCents = its price closest to 24h ago, so the plugin can show the change %.
const now = Date.now();
const DAY = 86400000;
const HISTORY_DAYS = 8;
const SAMPLE_GAP_MS = 110 * 60 * 1000;   // ~2h spacing -> ~96 points over 8 days
const REPO = process.env.GITHUB_REPOSITORY || 'WarmBed/TBH-DPS-dashboard';
const HISTORY_URL = `https://raw.githubusercontent.com/${REPO}/data/history.json`;

let history = { snapshots: [] };
try {
  const r = await fetch(HISTORY_URL, { headers: { 'User-Agent': UA } });
  if (r.ok) { const j = await r.json(); if (Array.isArray(j?.snapshots)) history = j; }
} catch { /* first run / 404 -> empty history */ }

// append a sparse snapshot (skip if the last point is newer than the sample gap)
const last = history.snapshots[history.snapshots.length - 1];
if (!last || now - last.t >= SAMPLE_GAP_MS) {
  const p = {};
  for (const [k, v] of Object.entries(items)) p[k] = v.lowestCents;
  history.snapshots.push({ t: now, p });
}
const cutoff = now - HISTORY_DAYS * DAY;
history.snapshots = history.snapshots.filter(s => s && s.t >= cutoff);

// reference price ~24h ago = the snapshot whose timestamp is closest to now-24h
let ref = null, refDist = Infinity;
for (const s of history.snapshots) { const d = Math.abs(s.t - (now - DAY)); if (d < refDist) { refDist = d; ref = s; } }
let stamped = 0;
if (ref && refDist <= 18 * 3600 * 1000) {   // only if we actually have a point within ~18h of the 24h mark
  for (const [name, v] of Object.entries(items)) {
    const pc = ref.p[name];
    if (pc != null) { v.prevCents = pc; v.prevAt = ref.t; stamped++; }
  }
}

// per-item price series for the plugin's sparkline: each item's sampled prices over the last 7 days,
// plus the current live price as the final point. Drawn as a mini curve in the price box.
const HIST_WINDOW_MS = 7 * DAY;
const histCut = now - HIST_WINDOW_MS;
const windowPts = history.snapshots.filter(s => s.t >= histCut);
for (const [name, v] of Object.entries(items)) {
  const arr = [];
  for (const s of windowPts) { const c = s.p[name]; if (c != null) arr.push(c); }
  if (arr.length === 0 || arr[arr.length - 1] !== v.lowestCents) arr.push(v.lowestCents);
  if (arr.length) v.hist = arr;
}

// ---- 24h volume + median sale price (priceoverview is per-item & rate-limited, so refresh only every
// ~6h and persist between the 30-min price runs inside history.vol). ----
const VOL_REFRESH_MS = 6 * 3600 * 1000;
let vol = (history.vol && history.vol.items) ? history.vol : { at: 0, items: {} };
const volAge = vol.at ? now - vol.at : Infinity;
if (volAge >= VOL_REFRESH_MS) {
  const names = Object.keys(items);
  let done = 0;
  for (const name of names) {
    let pj = null;
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        const r = await fetch(`https://steamcommunity.com/market/priceoverview/?appid=${APPID}&currency=1&market_hash_name=${encodeURIComponent(name)}`,
          { headers: { 'User-Agent': UA } });
        if (r.status === 429) { await sleep(15000); continue; }   // rate-limited: wait and retry
        if (r.ok) { pj = await r.json(); break; }
      } catch { /* retry */ }
      await sleep(4000);
    }
    if (pj && pj.success) {
      const vRaw = (pj.volume || '').replace(/[^0-9]/g, '');
      const mRaw = (pj.median_price || '').replace(/[^0-9.]/g, '');
      vol.items[name] = { vol: vRaw ? parseInt(vRaw, 10) : 0, medianCents: mRaw ? Math.round(parseFloat(mRaw) * 100) : 0 };
      done++;
    }
    await sleep(3500);   // ~17 req/min — stay under Steam's unauthenticated limit
  }
  vol.at = now;
  console.log(`refreshed volume/median for ${done}/${names.length} items`);
}
history.vol = vol;
for (const [name, v] of Object.entries(items)) {
  const vr = vol.items[name];
  if (vr) { v.vol = vr.vol; v.medianCents = vr.medianCents; }
}

const out = { cachedAt: now, appid: APPID, currency: currency || '$', count: Object.keys(items).length, items };
await writeFile('prices.json', JSON.stringify(out));
await writeFile('history.json', JSON.stringify(history));
console.log(`wrote prices.json: ${out.count} items, currency='${out.currency}', total reported ${total}; history ${history.snapshots.length} pts, prevCents on ${stamped} items`);

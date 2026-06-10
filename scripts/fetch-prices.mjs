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

const out = { cachedAt: Date.now(), appid: APPID, currency: currency || '$', count: Object.keys(items).length, items };
await writeFile('prices.json', JSON.stringify(out));
console.log(`wrote prices.json: ${out.count} items, currency='${out.currency}', total reported ${total}`);

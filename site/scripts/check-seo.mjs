import { readFile } from 'node:fs/promises';

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
for (const f of ['dist/sitemap-index.xml', 'dist/robots.txt']) {
  try { await readFile(f, 'utf8'); } catch { fail(`missing ${f}`); }
}
if (!process.exitCode) console.log('SEO checks passed');

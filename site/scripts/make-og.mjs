import sharp from 'sharp';
import { writeFile } from 'node:fs/promises';

const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="630" viewBox="0 0 1200 630">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#5b5bf5"/><stop offset="1" stop-color="#00c2a8"/>
    </linearGradient>
    <radialGradient id="glow" cx="50%" cy="0%" r="80%">
      <stop offset="0" stop-color="#1b2233"/><stop offset="1" stop-color="#0d1117"/>
    </radialGradient>
  </defs>
  <rect width="1200" height="630" fill="url(#glow)"/>
  <rect x="84" y="250" width="84" height="84" rx="20" fill="url(#g)"/>
  <g stroke="#fff" stroke-width="6" stroke-linecap="round" fill="none">
    <line x1="108" y1="312" x2="144" y2="272"/>
    <line x1="108" y1="272" x2="144" y2="312"/>
  </g>
  <text x="196" y="312" font-family="Arial, Helvetica, sans-serif" font-size="62" font-weight="800" fill="#ffffff">TBH DPS Meter</text>
  <text x="86" y="396" font-family="Arial, Helvetica, sans-serif" font-size="32" fill="#9aa4b2">Real-time DPS overlay for TaskBarHero</text>
  <text x="86" y="446" font-family="Arial, Helvetica, sans-serif" font-size="26" fill="#5b5bf5" font-weight="700">MIT · open-source · read-only · 5 languages</text>
</svg>`;

const png = await sharp(Buffer.from(svg)).png().toBuffer();
await writeFile('public/og.png', png);
console.log('og.png written', png.length, 'bytes');

import type { Dict } from './types';

const en: Dict = {
  meta: {
    homeTitle: 'TBH DPS Meter — Real-time DPS overlay for TaskBarHero',
    homeDesc: 'Free, open-source in-game overlay for TaskBarHero (TBH). Live DPS, damage taken, per-wave stage comparison and a personalized farming planner. Read-only — never modifies game values.',
    installTitle: 'Install — TBH DPS Meter for TaskBarHero',
    installDesc: 'Install the TBH DPS Meter overlay for TaskBarHero in three steps. No compiling needed.',
    changelogTitle: 'Changelog — TBH DPS Meter',
    changelogDesc: 'Release notes and version history for the TBH DPS Meter TaskBarHero overlay.',
  },
  nav: { features: 'Features', install: 'Install', faq: 'FAQ', download: 'Download' },
  hero: {
    eyebrow: 'Built for TaskBarHero · BepInEx overlay',
    titleA: 'Understand every',
    titleHighlight: 'hit you deal',
    lede: 'Live DPS, damage taken, per-wave stage comparison and a personalized farming planner — all as an in-game overlay. Open-source, read-only, and it never changes a single game value.',
    ctaDownload: 'Download latest',
    ctaGithub: 'View source on GitHub',
    trust: { mit: 'MIT open-source', readonly: 'Read-only · no value edits', langs: '5 languages', tested: 'Tested on v1.00.09' },
  },
  stats: { damageTypes: 'Damage types', panels: 'Analysis panels', languages: 'Languages', openSource: 'Open-source · read-only' },
  featuresKicker: 'Features',
  featuresTitle: 'Not just a number — a full combat dashboard',
  featuresSub: 'Four panels, each with one job. One keypress overlays them on the game, and clicks pass through so they never block play.',
  features: [
    {
      tag: 'F9 · Live',
      title: 'DPS panel',
      body: 'Live DPS over a 5-second sliding window, plus peak and average. See your damage structure at a glance and know which stat to gear next.',
      points: ['Live / peak / average DPS', 'Crit rate and crit-damage share', 'Melee / projectile / area / summon / DoT / trap breakdown'],
    },
    {
      tag: 'F10 · Defense',
      title: 'Damage-taken panel',
      body: "It is not just how hard you hit — it is how hard you get hit. Find where the killing blow comes from and adjust your resistances and positioning.",
      points: ['DTPS, biggest single hit, hit count', "Monsters' crit rate against you", 'Physical / fire / ice / lightning / chaos distribution'],
    },
    {
      tag: 'F11 · Compare',
      title: 'Per-wave stage comparison',
      body: 'Put this run side by side with your best clear — down to the gear and skill differences. A pace chart shows exactly which wave dragged you down.',
      points: ['Per-wave time, active vs idle running time', 'Full character gear & skill loadout diff', 'Clear-time trend chart — click any point to inspect'],
    },
    {
      tag: 'F6 · Plan',
      title: 'Personalized farming planner',
      body: 'Not a static wiki table — calibrated to your own real runs. Gold/sec and exp/sec ranked side by side, telling you exactly which stage to farm.',
      points: ['Measured values for cleared stages, personal multiplier for the rest', 'EXP-retention column reflects the level penalty', 'Auto-detects gear changes and prompts a re-clear'],
    },
  ],
  install: {
    kicker: 'Install',
    title: 'Three steps, three minutes',
    sub: 'No compiling, no config. Unzip, drop it into the folder, launch from Steam.',
    steps: [
      { title: 'Download the zip', body: 'Grab the latest TBH-DpsMeter.zip from Releases — no compiling needed.' },
      { title: 'Unzip to the game folder', body: 'Extract everything next to TaskBarHero.exe; choose overwrite if asked.' },
      { title: 'Launch from Steam', body: 'Always start the game through Steam — the overlay loads automatically.' },
    ],
    full: 'See the full install guide →',
  },
  faq: {
    kicker: 'FAQ',
    title: "You're probably wondering",
    items: [
      { q: 'Will I get banned?', a: 'The tool injects via BepInEx, only reads damage data, modifies no game values, and the game is single-player. That said, any third-party mod or injection tool may violate the game\'s or platform\'s (e.g. Steam) Terms of Service — use it at your own risk.' },
      { q: 'How do I update?', a: 'Just overwrite the single TBH.DpsMeter.dll in the plugins folder — BepInEx itself stays untouched. The panel also shows an in-app update notice with one-click download.' },
      { q: 'Is the black screen on first launch normal?', a: 'Yes. The first launch runs a one-time 1–3 minute setup, after which everything is normal.' },
      { q: 'Which game versions are supported?', a: 'Tested on v1.00.09 (Unity 6 / IL2CPP). After major game updates, fixes follow as quickly as possible.' },
    ],
  },
  finalCta: { title: 'Ready to see your damage clearly?', sub: 'Free, open-source, three-minute install.' },
  footer: {
    license: 'MIT License',
    disclaimer: 'Disclaimer',
    disclaimerLong: 'This tool only reads data and does not modify game values. You use this software entirely at your own risk. © 2026 WarmBed',
  },
  changelog: {
    title: 'Changelog',
    intro: 'Every release of TBH DPS Meter, pulled straight from GitHub.',
    fallback: 'Could not load releases right now — see them on GitHub.',
  },
};

export default en;

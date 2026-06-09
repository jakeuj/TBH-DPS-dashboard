export const SITE_URL = process.env.SITE_URL ?? 'https://tbh-dps-meter.zeabur.app';
export const GITHUB_OWNER = 'WarmBed';
export const GITHUB_REPO = 'TBH-DPS-dashboard';
export const LATEST_VERSION = 'v0.5.8';
export const DEFAULT_LOCALE = 'en' as const;
export const LOCALES = ['en', 'zh-Hant', 'zh-Hans', 'ja', 'es'] as const;
export type Locale = (typeof LOCALES)[number];

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

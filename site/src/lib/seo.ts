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

import { describe, it, expect } from 'vitest';
import { localizedPath, hreflangLinks, canonical } from './seo';

describe('localizedPath', () => {
  it('default locale en has no prefix', () => {
    expect(localizedPath('en', '/install')).toBe('/install');
    expect(localizedPath('en', '/')).toBe('/');
  });
  it('non-default locale is prefixed', () => {
    expect(localizedPath('zh-Hant', '/install')).toBe('/zh-Hant/install');
    expect(localizedPath('ja', '/')).toBe('/ja');
  });
});

describe('canonical', () => {
  it('joins site url + localized path', () => {
    expect(canonical('https://x.com', 'zh-Hant', '/')).toBe('https://x.com/zh-Hant');
    expect(canonical('https://x.com', 'en', '/install')).toBe('https://x.com/install');
  });
});

describe('hreflangLinks', () => {
  it('emits all 5 locales + x-default', () => {
    const links = hreflangLinks('https://x.com', '/install');
    expect(links).toHaveLength(6);
    expect(links).toContainEqual({ hreflang: 'en', href: 'https://x.com/install' });
    expect(links).toContainEqual({ hreflang: 'zh-Hant', href: 'https://x.com/zh-Hant/install' });
    expect(links).toContainEqual({ hreflang: 'x-default', href: 'https://x.com/install' });
  });
});

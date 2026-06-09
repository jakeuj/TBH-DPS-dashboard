import { describe, it, expect } from 'vitest';
import { parseReleases } from './releases';

const raw = [
  { tag_name: 'v0.5.7', published_at: '2026-06-08T00:00:00Z', name: 'v0.5.7',
    body: '# Fixes\n- only swallow clicks when foreground', draft: false, prerelease: false },
  { tag_name: 'v0.5.6', published_at: '2026-06-07T00:00:00Z', name: '',
    body: 'in-panel update', draft: false, prerelease: false },
  { tag_name: 'v0.5.5-rc', published_at: '2026-06-06T00:00:00Z', name: 'rc',
    body: 'x', draft: false, prerelease: true },
];

describe('parseReleases', () => {
  it('drops drafts and prereleases, keeps order', () => {
    const r = parseReleases(raw);
    expect(r.map((x) => x.tag)).toEqual(['v0.5.7', 'v0.5.6']);
  });
  it('renders markdown body to html', () => {
    const r = parseReleases(raw);
    expect(r[0].html).toContain('<li>');
  });
  it('falls back to tag when name empty', () => {
    const r = parseReleases(raw);
    expect(r[1].title).toBe('v0.5.6');
  });
});

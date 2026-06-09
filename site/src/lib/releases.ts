import { marked } from 'marked';
import { GITHUB_OWNER, GITHUB_REPO } from '../config';

export interface RawRelease { tag_name: string; name: string; published_at: string;
  body: string; draft: boolean; prerelease: boolean; }
export interface Release { tag: string; title: string; date: string; html: string; }

export function parseReleases(raw: RawRelease[]): Release[] {
  return raw
    .filter((r) => !r.draft && !r.prerelease)
    .map((r) => ({
      tag: r.tag_name,
      title: r.name?.trim() ? r.name : r.tag_name,
      date: r.published_at.slice(0, 10),
      html: marked.parse(r.body ?? '', { async: false }) as string,
    }));
}

export async function fetchReleases(): Promise<Release[]> {
  try {
    const res = await fetch(
      `https://api.github.com/repos/${GITHUB_OWNER}/${GITHUB_REPO}/releases?per_page=50`,
      { headers: { Accept: 'application/vnd.github+json' } },
    );
    if (!res.ok) return [];
    return parseReleases((await res.json()) as RawRelease[]);
  } catch {
    return [];
  }
}

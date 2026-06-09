import type { APIRoute } from 'astro';
import { SITE_URL } from '../config';

export const GET: APIRoute = () => {
  const base = SITE_URL.replace(/\/$/, '');
  const body = `User-agent: *\nAllow: /\nSitemap: ${base}/sitemap-index.xml\n`;
  return new Response(body, { headers: { 'Content-Type': 'text/plain' } });
};

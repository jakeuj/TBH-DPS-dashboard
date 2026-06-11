// Thin egress proxy so the GitHub Actions cron (datacenter IP, which tbh-market 403s) can reach
// tbh-market through Cloudflare (not blocked). Only proxies /api/* to tbh-market and requires a
// shared secret (x-proxy-key) so it isn't an open proxy. Light edge cache to be polite.
export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (env.PROXY_KEY && request.headers.get("x-proxy-key") !== env.PROXY_KEY)
      return new Response("forbidden", { status: 403 });
    if (!url.pathname.startsWith("/api/"))
      return new Response("not found", { status: 404 });
    const target = "https://tbh-market.com" + url.pathname + url.search;
    const r = await fetch(target, {
      headers: { "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36", "Accept": "application/json,*/*" },
      cf: { cacheTtl: 60, cacheEverything: true },
    });
    return new Response(r.body, {
      status: r.status,
      headers: { "content-type": r.headers.get("content-type") || "application/json", "access-control-allow-origin": "*" },
    });
  }
}

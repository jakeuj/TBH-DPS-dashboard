# Deploying the website (Zeabur)

The marketing site in `site/` is a static Astro build. It deploys to Zeabur as a static service.

## One-time Zeabur setup
1. Create a new service in Zeabur pointing at this GitHub repo.
2. Set the service **root directory** to `site`.
3. Build command: `npm run build` — output directory: `dist`.
4. Add an environment variable **`SITE_URL`** = your final domain, e.g. `https://tbh-dps-meter.zeabur.app` or a custom domain. This drives canonical URLs, hreflang, sitemap and the robots.txt `Sitemap:` line — set it before going live.
5. Deploy. Note the service's **Deploy Hook URL** (Zeabur → service → Settings → Deploy Hook).

## Auto-rebuild on release
The changelog page is generated at **build time** from the GitHub Releases API, so the site must rebuild when you publish a release.

1. In the GitHub repo: Settings → Secrets and variables → Actions → add a secret **`ZEABUR_DEPLOY_HOOK`** = the Deploy Hook URL from step 5 above.
2. The workflow `.github/workflows/deploy-site.yml` fires on `release: published` and POSTs that hook, triggering a fresh Zeabur build that picks up the new release notes.

## Custom domain
Point your domain at the Zeabur service (Zeabur → Domains), then update `SITE_URL` to match and redeploy so all SEO URLs are correct.

## Local preview
```
cd site
npm install
npm run build && npm run preview
```

# Agent Guide (IntelligenceX Website)

This folder contains the PowerForge.Web website for IntelligenceX.

Repo layout (maintainer):
- Website folder: `C:\Support\GitHub\IntelligenceX\Website`
- Engine repo: `C:\Support\GitHub\PSPublishModule`

## How To Build

- Fast dev build:
  - `.\build.ps1 -Dev`
- Serve + watch:
  - `.\build.ps1 -Serve -Watch -Dev`
- CI-equivalent (strict gates enabled by mode):
  - `powerforge-web pipeline --config .\pipeline.json --mode ci`

If you don't have the engine repo next to this repo, set:
- `POWERFORGE_ROOT` = path to `PSPublishModule`

## Key Files

- Site config: `site.json`
  - Features: `docs`, `apiDocs`, `blog`, `search`, `notFound`
  - Docs collection points to `../Docs` (outside this Website folder)
- Pipeline: `pipeline.json`
  - API docs steps generate into `./_site/api` and `./_site/api/powershell`
  - API docs nav token injection uses:
    - `config: ./site.json`
    - `nav: ./site.json`
    - `navContextPath: "/"` (keeps API header nav consistent with non-API pages)
- Theme: `themes/intelligencex/theme.manifest.json`

## Deploy + Cloudflare Cache

- GitHub Pages deploy workflow: `../.github/workflows/deploy-website.yml`
- Cloudflare secrets used by deploy:
  - `CLOUDFLARE_API_TOKEN`
  - `CLOUDFLARE_ZONE_ID_IX`
- Post-deploy cache commands are standardized and route-driven from site config:
  - `cloudflare purge --site-config "Website/site.json"`
  - `cloudflare verify --site-config "Website/site.json" --warmup 1`
- Canonical cache-rule guidance lives in:
  - `C:\Support\GitHub\PSPublishModule\Docs\PowerForge.Web.Cloudflare.md`

## Theme Best Practices (Nav Stability)

- Prefer Scriban helpers:
  - `{{ pf.nav_links "main" }}`
  - `{{ pf.nav_actions }}`
- Avoid `navigation.menus[0]` (menu ordering can change across sites/profiles).

## FAQ Route Guardrail

- Treat the two FAQ destinations as distinct surfaces with distinct labels:
  - `/docs/faq/` => `Docs FAQ`
  - `/faq/` => `Common Questions`
- Do not reuse the same visible link label for both destinations on the same page, or the website audit can fail with a link-purpose warning.

## Doctor Budget Guardrail

- `maxTotalFiles` in `pipeline.json` intentionally has headroom for normal docs and API growth.
- If the site approaches that ceiling again, adjust the budget intentionally in the website PR instead of letting small content changes fail unrelated branches.

# IntelligenceX Onboarding Roadmap (Wizard + CLI)

Status: Planning (not started)

## Phase 0 — Goals & constraints
- [ ] Confirm onboarding goals (wizard + PR-only path + upgrade path)
- [ ] Confirm default auth choice (vendor OAuth for single repo, BYO App for org)
- [ ] Confirm secret handling policy (auto if Sodium, manual fallback)
- [ ] Confirm UI choice (local web UI + Spectre.Console wizard)

## Phase 1 — Core setup architecture (shared by CLI + UI)
- [ ] Add SetupHost orchestration layer (single source of truth)
- [ ] Add wizard state model (repos, config, auth, apply mode)
- [ ] Implement Plan → Apply flow with dry-run output
- [ ] Implement upgrade/modify detection (existing workflow/config)

## Phase 2 — CLI Wizard (Spectre.Console)
- [ ] Add Spectre.Console dependency (CLI project only)
- [ ] Implement interactive steps:
  - [ ] Auth mode selection
  - [ ] GitHub auth flow
  - [ ] Org vs repo selection
  - [ ] Repo multi-select
  - [ ] Config presets + advanced JSON editor
  - [ ] OpenAI login (reuse if present)
  - [ ] Apply (PR vs direct)
- [ ] Non-interactive fallback (--plain, redirected input)
- [ ] Summary table + PR links

## Phase 3 — GitHub App Manifest (BYO App)
- [ ] Manifest generation (pre-filled app definition)
- [ ] Open GitHub "Create App from Manifest"
- [ ] Handle callback and exchange code for app id + PEM
- [ ] App install flow (select repos / all repos)
- [ ] Store app credentials locally for reuse

## Phase 4 — Secrets handling
- [ ] Auto-encrypt and upload secrets when Sodium available
- [ ] Manual secret fallback (print export + instructions)
- [ ] Support INTELLIGENCEX_AUTH_KEY for encrypted store

## Phase 5 — Local Web UI Wizard
- [ ] Local web host (Kestrel) + static assets
- [ ] Wizard screens (same steps as CLI)
- [ ] Advanced JSON editor panel
- [ ] Progress checklist + success summary
- [ ] "Manage existing setup" flow

## Phase 6 — Reviewer improvements
- [ ] Early auth validation with actionable errors
- [ ] Safer auth store handling in reviewer
- [ ] Explicit secrets in workflow (no secrets: inherit)

## Phase 7 — Docs & README
- [ ] README rewrite (what it is, trust model, quickstart)
- [ ] Docs: wizard onboarding, CLI quickstart, security/trust
- [ ] Screenshot placeholders + asset folder

## Phase 8 — Copilot (experimental)
- [ ] Keep CLI Copilot optional provider
- [ ] Research native Copilot feasibility
- [ ] Add provider toggle in wizard

# IntelligenceX Roadmap

Status: In progress

## Engine Roadmap
Status: In progress

### Now — Phase 1–2 (concrete)
- [x] Add error classification enum + mapping (S)
- [x] Add diagnostic context in reviewer output (request id, retry count, provider) (S)
- [x] Add retry policy options in config (backoff + max attempts) (M)
- [x] Gate fail-open to transient errors only (S)
- [x] Add connectivity preflight (DNS/TLS) with actionable errors (M)
- [x] Add diff-range selection + default (current/pr-base/first-review) (M)
- [x] Add include/exclude glob filters for files (S)
- [x] Add smart chunking (group related hunks) (M)
- [ ] Add review intent presets (security/perf/maintainability) (S)

### Phase 0 — Scope + success criteria
- [ ] Define "engine" scope (review pipeline, providers, context builder, formatter, thread triage).
- [ ] Capture success metrics (review latency, failure rate, reviewer usefulness score).
- [ ] Decide default review mode + model policy (safe defaults).

### Phase 1 — Reliability + diagnostics
- [x] Classify error types (transient vs auth vs config vs provider) with explicit codes.
- [x] Add structured diagnostics block to reviewer output (request id, retry count, provider).
- [x] Add connectivity preflight (DNS/TLS) with actionable error messages.
- [x] Add configurable retry policy with exponential backoff + jitter.
- [x] Add fail-open gating for transient errors only (explicit config).

### Phase 2 — Context quality
- [x] Add diff-range strategy options (current/pr-base/first-review) with default.
- [x] Add file filters (include/exclude globs).
- [ ] Add binary/generated file skipping.
- [x] Add smart chunking (keep related hunks together; avoid orphaned changes).
- [x] Add language-aware hints (prompt includes detected languages).
- [ ] Add "review intent" presets (security/perf/maintainability).

### Phase 3 — Review output + UX
- [x] Add optional "reasoning level" label in header (low/medium/high) when provider supports it.
- [x] Add optional usage/limits line near model header (opt-in; ChatGPT auth).
- [x] Add structured findings schema for bots/automation (severity + file + line).
- [x] Add summary stability (avoid noisy rewording across reruns).
- [x] Add “triage mode” that only checks open threads.

### Phase 4 — Thread triage + auto-resolve
- [x] Keep thread triage in main review comment (configurable placement).
- [x] Support "explain why not resolved" replies (optional).
- [x] Add diff-based auto-resolve checks (explicit evidence required).
- [x] Add per-bot policies (auto-resolve only for our bot by default).
- [x] Add PR comment summarizing what was auto-resolved.

### Phase 5 — Provider abstraction
- [ ] Centralize provider contracts (capabilities, limits, streaming, auth).
- [ ] Add provider capability flags (usage API, reasoning level, streaming).
- [ ] Add opt-in provider fallback (e.g., OpenAI → Copilot).
- [ ] Add provider health checks and circuit breaker.

### Phase 5.5 — Code host support (Azure DevOps Services)
- [ ] Define code-host interface (PR metadata, files, diff, comments, threads).
- [x] Add ADO auth options (PAT, System.AccessToken) + env var mapping.
- [x] Phase 1: summary-only PR comments (no inline) using ADO REST APIs (PR-level changes endpoint for full file list).
- [x] Document Azure auth scheme heuristic + override guidance.
- [x] Document PR-level changes behavior (uses pull request changes endpoint).
- [ ] Phase 2: inline comments with iteration + line mapping support.
- [ ] Phase 3: thread triage + auto-resolve via thread status updates.
- [ ] Add CLI flags/config: `provider=azure`, `azureOrg`, `azureProject`, `azureRepo`, `azureBaseUrl`, `azureTokenEnv`.
- [ ] Add ADO pipeline templates (onboarding, permissions, secrets).

### Phase 6 — Performance + cost
- [ ] Add response streaming where supported (show partial progress).
- [x] Add cache for context artifacts (diff, file lists, PR metadata).
- [x] Add concurrency controls to avoid API throttling.
- [x] Consider shared HttpClient/IHttpClientFactory for Azure DevOps client.
- [ ] Add token budgeting per file/group with hard caps.
- [x] Add optional "budget exceeded" summary behavior.

### Phase 7 — Security + trust
- [x] Redact sensitive data before prompt (secrets, tokens, private keys).
- [x] Sanitize Azure DevOps API error payloads in logs.
- [x] Add "untrusted PR" guardrails (no secret access, no write actions).
- [x] Add workflow integrity check (block self-modifying workflow runs).
- [x] Add audit logging for secrets usage.

### Phase 8 — Testing + validation
- [ ] Add deterministic test harness with recorded provider responses.
- [ ] Add golden-file tests for formatter output stability.
- [ ] Add smoke tests for thread triage + auto-resolve.
- [ ] Add integration test for usage/limits display.

### Phase 9 — Developer experience
- [ ] Provide local "engine replay" CLI (load PR snapshot + run offline).
- [ ] Provide structured JSON output mode for integrations.
- [x] Add config validator with helpful errors + schema links.

## Onboarding Roadmap (Wizard + CLI)

Status: In progress

### Phase 0 — Goals & constraints
- [ ] Confirm onboarding goals (wizard + PR-only path + upgrade path)
- [ ] Confirm default auth choice (vendor OAuth for single repo, BYO App for org)
- [ ] Confirm secret handling policy (auto if Sodium, manual fallback)
- [ ] Confirm UI choice (local web UI + Spectre.Console wizard)

### Phase 1 — Core setup architecture (shared by CLI + UI)
- [x] Add SetupHost orchestration layer (single source of truth)
- [x] Add wizard state model (repos, config, auth, apply mode)
- [x] Implement Plan → Apply flow with dry-run output
- [x] Implement upgrade/modify detection (existing workflow/config)

### Phase 2 — CLI Wizard (Spectre.Console)
- [x] Add Spectre.Console dependency (CLI project only)
- [x] Implement interactive steps:
  - [x] Auth mode selection
  - [x] GitHub auth flow
  - [x] Org vs repo selection
  - [x] Repo multi-select
  - [x] Config presets + advanced JSON editor
  - [x] OpenAI login (reuse if present)
  - [x] Apply (PR creation)
- [x] Non-interactive fallback (--plain, redirected input)
- [x] Summary table + PR links
- [x] Keep-secret propagation for cleanup
- [x] Disable manual-secret for update-secret flow

### Phase 3 — GitHub App Manifest (BYO App)
- [x] Manifest generation (pre-filled app definition)
- [x] Open GitHub "Create App from Manifest"
- [x] Handle callback and exchange code for app id + PEM
- [x] App install flow (select repos / all repos)
- [x] Store app credentials locally for reuse

### Phase 4 — Secrets handling
- [x] Auto-encrypt and upload secrets when Sodium available
- [x] Manual secret fallback (print export + instructions)
- [x] Support INTELLIGENCEX_AUTH_KEY for encrypted store

### Phase 5 — Local Web UI Wizard
- [x] Local web host (Kestrel) + static assets
- [ ] Wizard screens (same steps as CLI)
- [x] Advanced JSON editor panel
- [x] Progress checklist + success summary
- [x] "Manage existing setup" flow (load config from repo)
- [x] Workflow preview in web UI
- [x] Config presets (local storage)
- [x] Preset export/import (web UI)
- [x] Status badges (auth/repo/secret)
- [x] Enforce loopback + HTTP-only binding
- [x] Device flow timeout + expiry messaging
- [x] GitHub App manifest flow in web UI
- [x] Detect existing workflow/config (repo inspection)
- [x] Recommend setup actions based on inspection
- [x] Support auth bundle input for secrets (web UI)
- [x] Update-secret support in web UI
- [x] Reject non-local web UI requests + require JSON body

### Phase 6 — Reviewer improvements
- [x] Early auth validation with actionable errors
- [x] Safer auth store handling in reviewer
- [x] Explicit secrets in workflow (no secrets: inherit)
- [x] Retry extra ResponseEnded + fail-open summary option

### Phase 7 — Docs & README
- [x] README rewrite (what it is, trust model, quickstart)
- [x] Docs: wizard onboarding, CLI quickstart, security/trust
- [x] Screenshot placeholders + asset folder

### Phase 8 — Copilot (experimental)
- [ ] Keep CLI Copilot optional provider
- [ ] Research native Copilot feasibility
- [x] Add provider toggle in wizard

### Phase 9 — DevEx automation
- [x] Auto-resolve IntelligenceX bot review threads after fixes (CLI command or GitHub App action)

## Review Thread Cleanup (GitHub)

These are unresolved review threads on recent PRs (many are bot comments that may already be addressed in code). Triage each PR and either resolve the thread or create a follow-up task.

- [ ] PR #63 feat: add secrets audit logging — 4 unresolved review thread(s) (MERGED)
- [ ] PR #68 feat: configurable triage embed placement — 9 unresolved review thread(s) (MERGED)
- [ ] PR #67 feat: post auto-resolve summary comment — 6 unresolved review thread(s) (MERGED)
- [ ] PR #66 feat: enforce bot allowlist for auto-resolve — 5 unresolved review thread(s) (MERGED)
- [ ] PR #65 feat: always summarize thread auto-resolve — 6 unresolved review thread(s) (MERGED)
- [ ] PR #64 feat: require diff evidence for thread auto-resolve — 10 unresolved review thread(s) (MERGED)
- [ ] PR #62 feat: block self-modifying workflow runs — 6 unresolved review thread(s) (MERGED)
- [ ] PR #69 feat: explain kept threads on auto-resolve — 1 unresolved review thread(s) (MERGED)
- [ ] PR #61 feat: add untrusted PR guardrails — 3 unresolved review thread(s) (MERGED)
- [ ] PR #60 feat: add GitHub API concurrency limit — 6 unresolved review thread(s) (MERGED)
- [ ] PR #59 feat: add default redaction patterns — 9 unresolved review thread(s) (MERGED)
- [ ] PR #47 feat: tool docs and reviewer hints — 10 unresolved review thread(s) (MERGED)
- [ ] PR #57 feat: cache GitHub review context — 3 unresolved review thread(s) (MERGED)
- [ ] PR #56 feat: add review budget summary note — 5 unresolved review thread(s) (MERGED)
- [ ] PR #55 fix: sanitize Azure DevOps error payloads — 4 unresolved review thread(s) (MERGED)
- [ ] PR #54 feat: add language hints to review prompts — 4 unresolved review thread(s) (MERGED)
- [ ] PR #53 feat: smart patch chunking — 9 unresolved review thread(s) (MERGED)
- [ ] PR #52 feat: usage summary line — 3 unresolved review thread(s) (MERGED)
- [ ] PR #41 feat: copilot direct transport + unified roadmap — 13 unresolved review thread(s) (MERGED)
- [ ] PR #51 docs: Azure DevOps auth + changes notes — 2 unresolved review thread(s) (MERGED)
- [ ] PR #50 feat: summary stability, reasoning label, and ADO tests — 2 unresolved review thread(s) (MERGED)
- [ ] PR #49 fix: tool definitions determinism — 1 unresolved review thread(s) (MERGED)
- [ ] PR #48 feat: structured findings block — 3 unresolved review thread(s) (MERGED)
- [ ] PR #39 Website — 15 unresolved review thread(s) (OPEN)
- [ ] PR #46 fix: use PR-level ADO changes — 2 unresolved review thread(s) (MERGED)
- [ ] PR #45 OpenAI native tool calling — 6 unresolved review thread(s) (MERGED)
- [ ] PR #43 Reviewer: Azure DevOps summary-only support — 7 unresolved review thread(s) (MERGED)
- [ ] PR #42 Reviewer improvements: config validation + triage-only — 5 unresolved review thread(s) (MERGED)
- [ ] PR #40 Docs: polish reviewer config and CLI usage — 1 unresolved review thread(s) (MERGED)
- [ ] PR #37 Docs: clarify config load exception wording — 2 unresolved review thread(s) (MERGED)
- [ ] PR #35 Docs: add XML docs for config models — 1 unresolved review thread(s) (MERGED)
- [ ] PR #36 Add Claude Code GitHub Workflow — 12 unresolved review thread(s) (MERGED)
- [ ] PR #32 Reviewer: add include/exclude path filters — 2 unresolved review thread(s) (MERGED)
- [ ] PR #29 Reviewer: add error classification + retry diagnostics — 12 unresolved review thread(s) (MERGED)
- [ ] PR #28 Reviewer: improve usage summary output — 3 unresolved review thread(s) (MERGED)
- [ ] PR #26 Docs: add engine roadmap — 12 unresolved review thread(s) (CLOSED)
- [ ] PR #22 Add review thread resolver and simplify release notes — 2 unresolved review thread(s) (MERGED)
- [ ] PR #19 Reviewer: add summary parser test — 6 unresolved review thread(s) (MERGED)
- [ ] PR #16 Add review-thread triage context — 6 unresolved review thread(s) (MERGED)
- [ ] PR #18 Reviewer: show commit in summary — 9 unresolved review thread(s) (MERGED)
- [ ] PR #10 Review smoke test (2026-01-29) — 1 unresolved review thread(s) (MERGED)
- [ ] PR #14 Add default context guardrails for reviewer — 7 unresolved review thread(s) (MERGED)
- [ ] PR #13 Add release notes generator + workflow — 11 unresolved review thread(s) (MERGED)
- [ ] PR #12 Review suggestion smoke test — 3 unresolved review thread(s) (MERGED)
- [ ] PR #11 Fix inline comment parsing for claude output — 13 unresolved review thread(s) (MERGED)
- [ ] PR #6 fix(reviewer): honor openai_transport input — 1 unresolved review thread(s) (MERGED)
- [ ] PR #5 fix(reviewer): add PR fetch + safe event parsing — 1 unresolved review thread(s) (MERGED)
- [ ] PR #4 fix(reviewer): input-only workflow runs — 10 unresolved review thread(s) (MERGED)
- [ ] PR #2 Add IntelligenceX review automation — 6 unresolved review thread(s) (CLOSED)
- [ ] PR #1 Add IntelligenceX review automation — 1 unresolved review thread(s) (CLOSED)

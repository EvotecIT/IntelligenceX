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
- [x] Add review intent presets (security/perf/maintainability) (S)

### Phase 0 — Scope + success criteria
- [x] Define "engine" scope (review pipeline, providers, context builder, formatter, thread triage).
- [x] Capture success metrics (review latency, failure rate, reviewer usefulness score).
- [x] Decide default review mode + model policy (safe defaults).

### Phase 1 — Reliability + diagnostics
- [x] Classify error types (transient vs auth vs config vs provider) with explicit codes.
- [x] Add structured diagnostics block to reviewer output (request id, retry count, provider).
- [x] Add connectivity preflight (DNS/TLS) with actionable error messages.
- [x] Add configurable retry policy with exponential backoff + jitter.
- [x] Add fail-open gating for transient errors only (explicit config).

### Phase 2 — Context quality
- [x] Add diff-range strategy options (current/pr-base/first-review) with default.
- [x] Add file filters (include/exclude globs).
- [x] Add binary/generated file skipping.
- [x] Add smart chunking (keep related hunks together; avoid orphaned changes).
- [x] Add language-aware hints (prompt includes detected languages).
- [x] Add "review intent" presets (security/perf/maintainability).

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
- [x] Centralize provider contracts (capabilities, limits, streaming, auth).
- [x] Add provider capability flags (usage API, reasoning level, streaming).
- [x] Add opt-in provider fallback (e.g., OpenAI → Copilot).
- [x] Add provider health checks and circuit breaker.

### Phase 5.5 — Code host support (Azure DevOps Services)
- [x] Define code-host interface (PR metadata, files, diff, comments, threads).
- [x] Add ADO auth options (PAT, System.AccessToken) + env var mapping.
- [x] Phase 1: summary-only PR comments (no inline) using ADO REST APIs (PR-level changes endpoint for full file list).
- [x] Document Azure auth scheme heuristic + override guidance.
- [x] Document PR-level changes behavior (uses pull request changes endpoint).
- [ ] Phase 2: inline comments with iteration + line mapping support.
- [ ] Phase 3: thread triage + auto-resolve via thread status updates.
- [x] Add CLI flags/config: `provider=azure`, `azureOrg`, `azureProject`, `azureRepo`, `azureBaseUrl`, `azureTokenEnv`.
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

## Review Feedback Backlog (Bots)

Collapsed by PR. Includes only explicit checklist items found in bot reviews/comments.

- [x] PR #3 Review smoke test — checklist items: Generate review findings (in progress); Finalize summary. Links: https://github.com/EvotecIT/IntelligenceX/pull/3#issuecomment-3813472397
- [x] PR #21 Auto-resolve missing inline review threads — checklist items: Decide if auto-resolving should run when the latest review has zero inline comments; if yes, remove the `inlineKeys.Count > 0` guard (suggested above).. Links: https://github.com/EvotecIT/IntelligenceX/pull/21#issuecomment-3825536164
- [x] PR #22 Add review thread resolver and simplify release notes — checklist items: Redact or avoid posting raw exception summaries to PR comments; keep detailed errors in logs.; Ensure fail-open output is detected and used to skip inline comments / thread resolution.; Propagate compare API truncation to avoid incorrect diff-based auto-resolve decisions.. Links: https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3826408373
- [x] PR #30 Reviewer: configurable retry backoff + jitter — checklist items: Validate `retryBackoffMultiplier` is finite in config loader and env parsing to prevent invalid delays.. Links: https://github.com/EvotecIT/IntelligenceX/pull/30#issuecomment-3833603457
- [x] PR #31 Reviewer: add native connectivity preflight — checklist items: Simplify or differentiate the non‑success HTTP status handling in the preflight block to avoid redundant logic.; Add tests that cover preflight timeout, DNS failure, and non‑2xx responses to ensure error mapping is stable.. Links: https://github.com/EvotecIT/IntelligenceX/pull/31#issuecomment-3833692431
- [x] PR #33 Reviewer: add review diff-range selection — checklist items: Unregister the `Console.CancelKeyPress` handler on exit (e.g., in a `finally` block).. Links: https://github.com/EvotecIT/IntelligenceX/pull/33#issuecomment-3833798330
- [x] PR #36 Add Claude Code GitHub Workflow — checklist items: Generate review findings (in progress); Finalize summary. Links: https://github.com/EvotecIT/IntelligenceX/pull/36#issuecomment-3836649848
- [x] PR #37 Docs: clarify config load exception wording — checklist items: Align exception type/documentation for parse failures (either adjust behavior or revert doc to “not found” only).. Links: https://github.com/EvotecIT/IntelligenceX/pull/37#issuecomment-3837105937
- [x] PR #40 Docs: polish reviewer config and CLI usage — checklist items: Verify the correct CLI flag for reviewer auth and align the `resolve-threads` example accordingly.. Links: https://github.com/EvotecIT/IntelligenceX/pull/40#issuecomment-3837177213
- [x] PR #41 feat: copilot direct transport + unified roadmap — checklist items: Preserve required environment variables (or require absolute `CliPath`) when `InheritEnvironment=false`.; Reject `Timeout == TimeSpan.Zero` in `CopilotChatClientOptions.Validate()`.; Define and enforce precedence between `Token` and `Authorization` header for direct transport.. Links: https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3840061440
- [x] PR #43 Azure DevOps review changes — checklist items: Fetch full PR changes instead of only the latest iteration; consider using the PR-level changes endpoint or aggregating across iterations to avoid missing files.. Links: https://github.com/EvotecIT/IntelligenceX/pull/43
- [x] PR #45 OpenAI native tool calling — checklist items: Support snake_case `"response_id"` in `TurnInfo.FromJson` to avoid losing the response id on different payload formats.; Make `ToolRegistry.GetDefinitions()` return a deterministic order to avoid request/test flakiness.. Links: https://github.com/EvotecIT/IntelligenceX/pull/45#issuecomment-3842964985
- [x] PR #56 feat: add review budget summary note — checklist items: Align `PrepareFiles` behavior for `maxFiles <= 0` with ADO path to avoid empty context and misleading budget notes.. Links: https://github.com/EvotecIT/IntelligenceX/pull/56#issuecomment-3843966686
- [x] PR #62 feat: block self-modifying workflow runs — checklist items: Add a `.yaml` workflow test case to cover both supported extensions.. Links: https://github.com/EvotecIT/IntelligenceX/pull/62#issuecomment-3845835997
- [x] PR #63 feat: add secrets audit logging — checklist items: Prevent `SecretsAudit.Record` from queuing entries when auditing is disabled (gate with an “enabled” flag or similar).; Fix token selection to fall back on `GITHUB_TOKEN` when `INTELLIGENCEX_GITHUB_TOKEN` is empty/whitespace.. Links: https://github.com/EvotecIT/IntelligenceX/pull/63#issuecomment-3845855649
- [x] PR #65 feat: always summarize thread auto-resolve — checklist items: Make `BuildFallbackTriageSummary` accessible to tests without reflection (e.g., `internal` + `InternalsVisibleTo`) to reduce brittleness.; Add at least one more test case for fallback summary (e.g., kept-only and mixed resolved/kept).. Links: https://github.com/EvotecIT/IntelligenceX/pull/65#issuecomment-3845900159
- [x] PR #73 fix: harden retry backoff and file limits — checklist items: Deduplicate finite validation between config/env parsing; Add a negative `maxFiles` test to document `<= 0` behavior; Document why non-finite backoff values are rejected; Clarify `maxFiles <= 0` meaning in docs.. Links: https://github.com/EvotecIT/IntelligenceX/pull/73
- [x] PR #74 Fix reviewer backlog items — checklist items: Consider an integration-style test that validates the failure-summary update path in `Program.RunAsync`.. Links: https://github.com/EvotecIT/IntelligenceX/pull/74
<details>
<summary>PR #95 Fix duplicate weekly labels in usage summary</summary>

- [x] Replace string-based type detection (`StartsWith("code review")`) with semantic context input. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#discussion_r2771453274
- [x] Tighten secondary suffix detection (`EndsWith("(secondary)")`) to avoid accidental matches. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#discussion_r2771453336
- [x] Make test assertions less formatting-coupled while still validating disambiguation behavior. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#issuecomment-3856748283
- [x] Add at least one additional regression case for `(secondary)` code-review weekly labels. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#issuecomment-3856748283
- [x] Decide whether `code review` prefix applies broadly to code-review duration labels and align tests with that intent. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#discussion_r2771495599
- [x] Replace switch default in `GetUsageLimitFallbackLabel` with explicit `ArgumentOutOfRangeException`. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#discussion_r2771495630
- [x] Strengthen tests with negative assertions preventing ambiguous legacy labels from reappearing. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#issuecomment-3856748283
- [x] Add explicit usage-summary part-count assertions for duplicate-weekly scenarios. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#issuecomment-3856748283
- [x] Keep secondary suffix ownership in one formatter layer to avoid future double-suffix regressions. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#discussion_r2771555112
- [x] Standardize usage summary delimiter/prefix handling while avoiding new public API coupling between reviewer and tests. Links: https://github.com/EvotecIT/IntelligenceX/pull/95#discussion_r2771568731
</details>
<details>
<summary>PR #109 Improve static-analysis visibility in review comments</summary>

- [x] Count parsed analysis files only after successful parse/processing to avoid double-counting with failed files. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774051038, https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053381, https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053613
- [x] Replace broad `catch {}` in analysis loading with scoped recoverable exception handling. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053428
- [x] Remove dead null-check on `lines` in `AddOutcomeLines`. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053471
- [x] Refactor per-rule count increment to single-assignment/ternary form. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774044094, https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053648
- [x] Normalize rule IDs before outcome matching to reduce undercount risk from formatting/casing variations. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053525
- [x] Pass a single `AnalysisLoadResult` through policy rendering to reduce future drift between report and findings. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053656
- [x] Add explicit tests for zero-findings and unavailable-input summary behavior. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774053589
- [x] Prefer LINQ projection/grouping (`Select`/`GroupBy`) for rule normalization/count aggregation paths in policy outcomes. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774097816, https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774097825
- [x] De-duplicate resolved analysis inputs before loading to avoid double counting and duplicate reads. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774098086
- [x] Narrow recoverable exception filter and avoid treating broad `ArgumentException` as recoverable parse noise. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774098158
- [x] Mark policy status as partial when findings are outside enabled packs, even when enabled-rule findings are zero. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774098208
- [x] Make `Rule outcomes` wording explicit for findings outside enabled packs. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774098250
</details>
<details>
<summary>PR #85 Static analysis catalog + CLI export</summary>

- [x] Update analysis loading to use `reviewFiles` so analysis findings respect filters. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#pullrequestreview-3757563544
- [x] Add resilience around catalog loading to avoid failing reviews when the catalog is missing or unreadable. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#pullrequestreview-3757563544
- [x] Expand severity normalization to handle “critical” (and other high-severity values if expected). Links: https://github.com/EvotecIT/IntelligenceX/pull/85#pullrequestreview-3757563544
- [x] Null-guard pack rules before adding to policy output. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#discussion_r2770314429
- [x] Normalize `SeverityOverrides` into a case-insensitive dictionary. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#discussion_r2769812614
- [x] Catch malformed glob patterns to avoid `ArgumentException`. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#discussion_r2770238132
- [x] Treat unknown severities distinctly from `none` to avoid silent suppression. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#discussion_r2770655013
- [x] Log malformed glob patterns so config errors are visible. Links: https://github.com/EvotecIT/IntelligenceX/pull/85#discussion_r2770655055
</details>
<details>
<summary>PR #99 Add maintainability LOC rule and split setup runner</summary>

- [x] Replace string-marker path exclusion with robust normalized path segment checks. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#issuecomment-3858550015
- [x] De-duplicate rule ID/threshold by loading from catalog or centralized constants. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#issuecomment-3858550015
- [x] Expand generated-file detection (header-based + configurable patterns). Links: https://github.com/EvotecIT/IntelligenceX/pull/99#issuecomment-3858550015
- [x] Add tests for CRLF/LF and trailing newline/no-trailing-newline LOC counting. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#issuecomment-3858550015
- [x] Add test for case-insensitive path exclusions on Windows. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#issuecomment-3858550015
- [x] Validate docs path resolution strategy for rule metadata. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#issuecomment-3858550015
- [x] Prefer metadata-driven internal LOC rule selection/limits over hardcoded rule IDs. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772931157
- [x] Normalize and de-duplicate generated suffix handling before matching. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772931230
- [x] Make generated-header markers stricter/configurable to reduce false positives. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772931318
- [x] Keep partial-class shared state minimal/immutable during setup-runner split. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772931392
- [x] Keep internal findings `tool` value aligned with rule metadata (`IntelligenceX.Maintainability`). Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772961232
- [x] Extend internal scan directory exclusions to cover `.vs` and `node_modules`. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772961320
- [x] Make generated-header marker checks case-insensitive on trimmed comment lines. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2772961368
- [x] Increase generated-header scan window and keep early stop at first code token. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2773050456
- [x] Match generated suffixes against filename (not full path) with normalized suffix handling. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2773050517
- [x] Keep generated marker/suffix defaults in catalog tags as canonical source (no duplicate in runner constants). Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2773234767
- [x] Warn on unknown/malformed IXLOC001 tags to avoid silent config typos. Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2773420136, https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2773420211
- [x] Clarify maintainability pack enablement is explicit repo-config change (no forced migration/push into existing repos). Links: https://github.com/EvotecIT/IntelligenceX/pull/99#discussion_r2773653898
</details>

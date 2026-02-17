# IntelligenceX Roadmap

Status: In progress

## Ops Agent Backlog
For the "smart colleague" chat agent roadmap (ADPlayground/TestimoX/ComputerX/EventViewerX + runtime parallelism),
see [Docs/agent-superpowers-backlog.md](Docs/agent-superpowers-backlog.md).

## PR Feedback Churn
- [ ] PR #399: Confirm intended shutdown semantics/ownership boundaries for queued UI publish awaiters (`CancelQueuedUiPublishesForShutdown`) because reviewer todo/critical guidance alternates between "complete on shutdown" vs "cancel on shutdown" and repeatedly reports outdated shutdown-order findings after fixes. Source: https://github.com/EvotecIT/IntelligenceX/pull/399#issuecomment-3906851784
- [ ] PR #438: Confirm intended routing transparency emission policy (`ShouldEmitRoutingTransparency`) because reviewer todo/critical guidance repeatedly alternates between "emit only on consistent/informative routing states" and "always emit with normalized counts," producing non-deterministic merge blockers after each fix. Source: https://github.com/EvotecIT/IntelligenceX/pull/438#issuecomment-3912769531

## Reviewer E2E Launch Plan
Status: In progress

Goal: reviewer + static analysis + onboarding (CLI + Web) feel "done" end-to-end for a new repo.

### Acceptance (Definition Of Done)
- [x] A new user can run `intelligencex setup wizard` on a clean machine and reach "PR created" without manual repo edits.
- [x] A new user can run `intelligencex setup web` and reach "PR created" without manual repo edits.
- [ ] First merged onboarding PR produces a successful review on the next PR (sticky summary + inline when supported).
- [ ] Review comment always includes reviewed SHA and an explicit diff-range label (base -> head).
- [ ] Static analysis runs before review, publishes artifacts, and the review comment always renders analysis status (pass/unavailable) even when findings are zero.
- [ ] Static analysis gate behavior is predictable: failing types/severities are documented and match observed CI results.
- [x] Dependabot identity limitation is documented and visible during onboarding (reviews may be authored by `github-actions`).

### Phase A — Onboarding UX (CLI + Web)
- [x] CLI wizard: add "Enable static analysis" toggle and pack picker (default `all-50`).
- [x] Web UI: add "Enable static analysis" toggle and pack picker (default `all-50`).
- [x] CLI + Web: show a final "Effective config" preview (review + analysis) before Apply.
- [x] CLI + Web: surface the Dependabot secrets limitation in the UI copy (why bot identity may differ).
- [x] CLI + Web: add a post-Apply "Verify" step (workflow present, config present if requested, required secrets present, last runs links).

### Phase B — Setup Output (Workflow + reviewer.json)
- [x] Setup config writer: include `analysis` section in `.intelligencex/reviewer.json` when static analysis is enabled (create + merge paths).
- [x] Setup presets: define recommended tiers for analysis packs (`all-50`, `all-100`, `all-500`) and a "no analysis" option.
- [x] Ensure workflow/config generation stays stable across upgrades (managed block upgrades do not delete user customization outside managed block).

### Phase C — Review Reliability (Reduce Churn, Increase Continuity)
- [ ] Add a diff range option for incremental review (for example `reviewDiffRange: last-reviewed` uses the last reviewed commit from the sticky summary as base).
- [ ] Add a deterministic "replay" mode for debugging: load a saved PR snapshot + artifacts and run review formatting without provider calls.
- [ ] Ensure thread triage does not repeatedly re-suggest already-addressed items; prefer dedupe and summary stability over rewriting.

### Phase D — Static Analysis Productization
- [x] Wizard: explain analysis gate semantics (which types/severities fail the check) and link to docs.
- [x] Add "list packs" affordance in onboarding (CLI and Web) so users can browse available packs.
- [x] Provide an optional "export analyzer config" path for IDE support (explicit opt-in, never default).
- [x] Add a CI guardrail: `intelligencex analyze validate-catalog` and pack integrity checks run on every PR that touches Analysis/Catalog or Analysis/Packs.

### Phase E — Docs + Samples
- [x] Promote `Docs/reviewer/static-analysis.md` from Draft to stable docs (align examples with actual wizard output).
- [x] Add "First PR checklist" doc: what to expect after merging onboarding PR and how to debug common issues.
- [x] Add screenshots (CLI + Web) for the "Configure" step and the "Verify" step.

### Phase F — End-To-End Tests
- [x] Add tests for setup plan generation: ensure enabling analysis produces `analysis` in reviewer.json and does not regress existing review settings.
- [x] Add tests for config merge behavior (existing reviewer.json + enable analysis preserves unrelated user keys).
- [x] Add unit tests for `SetupAnalysisPacks.TryNormalizeCsv` (empty/default, invalid chars, max ids/length, dedupe).
- [x] Add tests for web setup validation: analysis fields rejected when not applicable (config override, update-secret/cleanup), and rejected when `analysisEnabled != true` but gate/packs provided.

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
- [x] Add golden-file tests for formatter output stability.
- [x] Add smoke tests for thread triage + auto-resolve.
- [x] Add integration test for usage/limits display.

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
- [x] PR #21 Auto-resolve missing inline review threads — checklist items: Decide if auto-resolving should run when the latest review has zero inline comments; if yes, remove the `inlineKeys.Count > 0` guard (suggested above). Links: https://github.com/EvotecIT/IntelligenceX/pull/21#issuecomment-3825536164
- [x] PR #22 Add review thread resolver and simplify release notes — checklist items: Redact or avoid posting raw exception summaries to PR comments; keep detailed errors in logs.; Ensure fail-open output is detected and used to skip inline comments / thread resolution.; Propagate compare API truncation to avoid incorrect diff-based auto-resolve decisions.. Links: https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3826408373
- [x] PR #30 Reviewer: configurable retry backoff + jitter — checklist items: Validate `retryBackoffMultiplier` is finite in config loader and env parsing to prevent invalid delays.. Links: https://github.com/EvotecIT/IntelligenceX/pull/30#issuecomment-3833603457
- [x] PR #31 Reviewer: add native connectivity preflight — checklist items: Simplify or differentiate the non‑success HTTP status handling in the preflight block to avoid redundant logic.; Add tests that cover preflight timeout, DNS failure, and non‑2xx responses to ensure error mapping is stable. Links: https://github.com/EvotecIT/IntelligenceX/pull/31#issuecomment-3833692431
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
- [ ] PR #208 Manage hub external-command review churn — checklist item keeps reappearing despite early-return startup/read-init failure handling in `IntelligenceX.Cli/Program.Manage.Utility.cs`; treat as churn unless maintainers escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/208#issuecomment-3880247612
- [ ] PR #210 CLI/Web onboarding review churn — blocker oscillates between opposite onboarding state models across iterations; treat as churn unless maintainers explicitly escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/210#issuecomment-3883453782
- [ ] PR #229 analysis-export duplicate review churn — checklist item still reports missing mixed-separator duplicate normalization after `ce8f1c2`; `TestSetupAnalysisExportDuplicateTargetDetection` now includes `.intelligencex\\analyzers\\.editorconfig` vs `.intelligencex/analyzers/.EDITORCONFIG` and passes locally + CI. Treat as churn unless maintainers escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/229#issuecomment-3884832998
- [ ] PR #234 setup post-apply verification churn — latest bot item claims `HandleSetupAsync` still passes `request.SecretOrg`, but code at `IntelligenceX.Cli/Setup/Web/WebApi.Setup.cs:210` already passes `secretOrgForRepo` into `ResolveOrgSecretVerificationContext`; treat as churn unless maintainers escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/234#issuecomment-3885712980
- [ ] PR #248 onboarding autodetect review churn — after multiple fix batches (`2dd8482`) and green required checks, bot still reports speculative merge blockers about subprocess strategy/workspace validation not tied to reproducible failures in current diff. Track separately and escalate only if maintainers require additional hardening before merge. Links: https://github.com/EvotecIT/IntelligenceX/pull/248#issuecomment-3889295672
- [ ] PR #275 multi-account routing review churn — after multiple fix batches (`5642679`, `f325671`) and green required checks, latest bot todo items report non-reproducible issues already covered by current code paths/tests (case-insensitive account dedupe and sticky-id null/whitespace guard). Treat as churn unless maintainers explicitly escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/275#issuecomment-3896742650
- [ ] PR #262 onboarding acceptance-path review churn — latest blocker oscillates between opposite recommendations (first rejecting production helper exposure, then rejecting reflection fallback after helper removal) despite green required checks and validated behavior; treat as churn unless maintainers explicitly escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/262#issuecomment-3890747719
- [ ] PR #293 strict-lookahead review churn — latest blocker continues to claim `analyze run --strict --framework net8.0` parsing failure after parser branch hardening and dedicated regressions (`TestAnalyzeRunStrictFlagAllowsKnownOptionLookaheadWithFrameworkValue`, strict equals override tests) passed locally on net8/net10 and required checks are green. Treat as churn unless maintainers explicitly escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/293#issuecomment-3901612365
- [ ] PR #400 vision contract policy-prefix review churn — latest blocker still reports backticked policy-prefix miscounting after parser hardening (`policySection`-based explicit counters) plus direct regressions (`TestVisionCheckParseDocumentSupportsBacktickedPolicyPrefixes`, `TestVisionCheckRunEnforceContractSupportsBacktickedPolicyPrefixes`) passing locally on net8/net10 and required checks are green. Treat as churn unless maintainers explicitly escalate. Links: https://github.com/EvotecIT/IntelligenceX/pull/400#issuecomment-3907047162
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
- [x] De-duplicate resolved files without a second materialization pass to keep memory bounded on large glob expansions. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774210599
- [x] Treat no-enabled-rules + no-findings policy state as unavailable/not-applicable (with explicit message). Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774210666
- [x] Align `AnalysisSummaryBuilder.BuildSummary` nullable signature with defensive null handling. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774210712
- [x] Emit unavailable analysis summary on internal load failures instead of silently dropping the analysis block. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774210754
- [x] Make summary-body parser stop at a more resilient model section prefix (`### Model`) to reduce template-string fragility. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774210790
- [x] Avoid empty placeholder line in model/usage bullets by always rendering a reasoning bullet. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774210837
- [x] Remove redundant `File.Exists` pre-check in analysis loading loop and rely on existing IO-exception path around direct reads. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774290821
- [x] Improve analysis-load failure logging to include full exception context for diagnostics. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774290928
- [x] Refine status semantics so “outside enabled packs” does not always imply execution degradation when enabled rules are clean. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774291073
- [x] Confirm heading-casing output shift is covered by tests/docs to protect downstream parser expectations. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774291169
- [x] Keep `parsedInputFiles` aligned with successful parse semantics by excluding empty-file no-op inputs. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774291277
- [x] Remove `InvalidOperationException` from recoverable analysis-load exceptions to avoid masking logic bugs. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774320699
- [x] Keep analysis-load error logging concise and avoid dumping full exception details in CI logs. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774320762
- [x] Align `BuildUnavailableSummary` formatting with other builders by trimming trailing newline output. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774320883
- [x] Keep `AddOutcomeLines` nullability contract consistent (non-null inputs, no redundant null-coalescing). Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774320982
- [x] Mark policy as partial when findings exist outside enabled packs to keep risk visible in status. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774321084
- [x] Add `JsonException` to recoverable analysis-load exceptions for JSON parser compatibility. Links: user request in Codex thread (2026-02-06)
- [x] Clarify and document `parsed` counter semantics for analysis result files. Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774385771
- [x] Add regression coverage for mixed rule outcomes (enabled findings + outside-pack findings). Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774385859
- [x] Verify and lock behavior that zero-findings summaries still render content (no empty-string contract). Links: https://github.com/EvotecIT/IntelligenceX/pull/109#discussion_r2774385922
- [x] Render unavailable policy status when analysis load fails and summary output is disabled. Links: user request in Codex thread (2026-02-06)
- [x] Add test for deduplicated resolved inputs with one parse success and one parse failure counter path. Links: user request in Codex thread (2026-02-06)
</details>
<details>
<summary>PR #112 Address remaining static-analysis follow-up TODOs</summary>

- [x] Use one computed sanitized analysis-load failure reason and pass it consistently to unavailable policy and summary builders. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571048, https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571128
- [x] Decouple unavailable policy rendering from `BuildPolicy(settings)` output shape via dedicated base-policy preparation path. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571204
- [x] Move parsed/failed counter semantics into `AnalysisLoadReport` XML docs to reduce drift. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571298
- [x] Add ordering-insensitive dedupe regression coverage for resolved analysis inputs. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571405
- [x] Add explicit coverage that duplicate bad input matches increment `FailedInputFiles` once per unique file. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571492
- [x] Keep unavailable reason exposure bounded (type + sanitized/truncated message) for user-facing review blocks. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774571048
- [x] Expand path-root redaction coverage for unavailable reason formatting (workspace/current/temp/profile variants). Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774639419
- [x] Use text-element-safe truncation for unavailable reason rendering to avoid splitting grapheme clusters. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774639474
- [x] Add defensive sanitize/trim path in `BuildUnavailablePolicy` for future raw-reason callers. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774639591
- [x] Keep recoverable parser exception rationale explicit and verify single-failure counting via tests. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774639515
- [x] Simplify user-facing failure reason to exception-type allowlist + generic fallback for unexpected internals. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774691270
- [x] Keep unavailable-policy sanitization defensive for future callers while reducing sensitive detail exposure in reasons. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774691136, https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774691199
- [x] Clarify parser-phase recoverable exception intent for `FormatException`/`JsonException` and keep non-parse exceptions escalated. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774691424
- [x] Align `AnalysisLoadReport` XML wording with reviewer docs (“valid payloads that produce zero findings”). Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774691520
- [x] Re-throw `OperationCanceledException` in analysis-load handling to preserve cancellation semantics. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774728195
- [x] Remove dead null-check branch from `BuildAnalysisLoadFailureReason(Exception)` to keep nullability contract strict. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774728266
- [x] Map analysis-load unavailable reasons to stable user-facing categories (permission/read/format/internal). Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774728330
- [x] Add regression test for top-level analysis failure path when `ShowPolicy=true` and `Summary=false` (policy embeds, summary omitted). Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774728395
- [x] Keep `BuildUnavailablePolicy` reason sanitization length-bounded to prevent oversized unavailable blocks. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774748896
- [x] Add explicit zero-findings parsed-counter coverage for both findings JSON and SARIF payload paths (including empty runs/results). Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774748951, https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774749013
- [x] Preserve cancellation semantics through top-level reviewer error handling without posting failure-summary updates on cancellation. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774771818
- [x] Keep analysis-load failure embedding no-op when both `showPolicy=false` and `summary=false`. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774771896
- [x] Reduce IO-specific wording in user-facing unavailable reasons to stable generic category text. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774771941
- [x] Align `failed` counter docs with implementation for unreadable/inaccessible matched files. Links: https://github.com/EvotecIT/IntelligenceX/pull/112#discussion_r2774772025

</details>
<details>
<summary>PR #113 Improve static analysis policy readability with enabled-rules preview</summary>

- [x] Keep enabled-rules preview API signatures intent-focused for append-only list building. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775200950
- [x] Add defensive null handling for preview rule description fallback paths. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775201048
- [x] Preserve configured rule order in enabled-rules preview output (no implicit sorting). Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775201172
- [x] Add regression coverage for enabled-rules preview truncation formatting (`(truncated)`). Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775201255
- [x] Add regression coverage for blank-title rule preview fallback to rule ID. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775201327
- [x] Align static-analysis docs example with real enabled-rules preview output format and truncation suffix. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775201649
- [x] Clarify effective enabled-rule ordering source in policy builder (pack order after disabled filtering). Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775238139
- [x] Bound long rule preview titles to keep policy lines readable and stable. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775238253
- [x] Add no-truncation assertion for empty enabled-rules preview path. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775238338
- [x] Add boundary assertion that preview includes item `MaxListItems` and excludes overflow. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775238434
- [x] Keep `TruncatePreviewTitle` null-safe with text-element-aware truncation (`string?` input + `StringInfo`). Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775275828
- [x] Remove brittle hardcoded truncation math from tests and derive expected preview from shared formatting behavior. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775275886
- [x] Assert a single truncation marker occurrence when preview output is truncated. Links: https://github.com/EvotecIT/IntelligenceX/pull/113#discussion_r2775275936

</details>
<details>
<summary>PR #114 Improve static analysis policy with explicit failing/clean rule previews</summary>

- [x] Make `AddOutcomeLines` null-safe for findings input and preserve safe policy rendering when findings are null. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#issuecomment-3861788077
- [x] Refactor `TryBuildBasePolicy` multi-out return shape to a typed context object for maintainability. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#issuecomment-3861788077
- [x] Add docs note describing deterministic ordering and truncation behavior for outcome preview lines. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#discussion_r2775319845, https://github.com/EvotecIT/IntelligenceX/pull/114#issuecomment-3861788077
- [x] Add regression test for null findings with a present load report. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#issuecomment-3861788077
- [x] Add regression test asserting deterministic ordering for failing and outside-pack preview sections. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#issuecomment-3861788077
- [x] Sort failing-rule preview by finding count (desc) then rule id to keep truncation behavior focused on highest-impact failures. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#discussion_r2775323218
- [x] Align static-analysis docs sample with actual truncation behavior when only 5 enabled rules are shown. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#discussion_r2775323255
- [x] Keep explicit aggregate outside-pack count assertion in analysis policy tests to protect status/count semantics. Links: https://github.com/EvotecIT/IntelligenceX/pull/114#discussion_r2775368110
</details>
<details>
<summary>PR #115 Harden static analysis policy load path and preview tests</summary>

- [x] Narrow exception handling in catalog load path and avoid blanket catch behavior. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Return an unavailable policy block (`Status: unavailable`) when catalog load fails instead of empty output. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Reduce mutable state exposure in policy context by using read-only/immutable collections. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Strengthen analysis policy tests with structured line assertions instead of only `AssertContainsText`. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Verify nullability handling around findings and counters in `AddOutcomeLines`. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Keep deterministic ordering culture-invariant with ordinal comparers in sorted output paths. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Make `TruncatePreviewTitle` null-safe at signature level. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Reduce brittle expected-string construction in truncation tests. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Add assertion for single `(truncated)` marker occurrence on preview line. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Add regression coverage for non-BMP Unicode preview title truncation behavior. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Centralize preview formatting constants for policy/test alignment. Links: user request in Codex thread (2026-02-06), https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Normalize unavailable-policy pack display to skip blank pack IDs and preserve trimmed values. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775613244
- [x] Keep one lightweight `AssertContainsText` header assertion alongside strict line assertions for policy output stability. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775620847
- [x] Group analysis-policy test registration into a dedicated helper (`RunAnalysisPolicyReportingTests`) to reduce main-runner churn. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775620879
- [x] Complete constant migration and behavior-oriented naming (`MaxRulePreviewItems`, `TruncatedPreviewSuffix`, `TruncationEllipsis`) in policy builder and tests. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775620920, https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775625708
- [x] Split oversized analysis reporting tests into topic files to satisfy internal LOC maintainability rule (`IXLOC001`). Links: https://github.com/EvotecIT/IntelligenceX/pull/115
- [x] Keep analysis-policy helper registration grouped by feature with table-driven execution for easier reorder/maintenance. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775698320
- [x] Align analysis-policy test display naming with `unavailable` terminology for catalog-load fallback scenarios. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775698368
- [x] Add one snapshot-style full policy block assertion to protect against line-order or formatting regressions beyond key/value checks. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775698408
- [x] Keep analysis-policy helper under `INTELLIGENCEX_REVIEWER` symbol to preserve existing test-variant coverage boundaries. Links: https://github.com/EvotecIT/IntelligenceX/pull/115#discussion_r2775698460
</details>
<details>
<summary>PR #116 Cleanup static-analysis review followups and TODO backlog</summary>

- [x] Remove raw-string snapshot literal in analysis-policy test to keep Windows/net472 compilation compatible. Links: https://github.com/EvotecIT/IntelligenceX/actions/runs/21765222612/job/62799147040
- [x] Set analysis config mode explicitly in snapshot test setup to avoid default-coupled expectations. Links: https://github.com/EvotecIT/IntelligenceX/pull/116#discussion_r2775950426
- [x] Make `AssertTextBlockEquals` trim both ends to reduce non-semantic formatting noise failures. Links: https://github.com/EvotecIT/IntelligenceX/pull/116#discussion_r2775951574
- [x] Make `NormalizeNewlines` null-safe for future helper reuse. Links: https://github.com/EvotecIT/IntelligenceX/pull/116#discussion_r2775951622
- [x] Keep full policy block snapshot strict in this scenario to intentionally catch line-order and formatting regressions. Links: https://github.com/EvotecIT/IntelligenceX/pull/116#discussion_r2775951509

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
<details>
<summary>PR #127 Add tiered analysis packs and rule inventory formats</summary>

- [x] `list-rules` should recognize `--help`, `-h`, and `help` and return usage text with exit 0. Links: https://github.com/EvotecIT/IntelligenceX/pull/127#discussion_r2777702370, https://github.com/EvotecIT/IntelligenceX/pull/127#discussion_r2777703517
- [x] Keep `--format json` output machine-parseable by writing warnings to `stderr` instead of `stdout`. Links: https://github.com/EvotecIT/IntelligenceX/pull/127#discussion_r2777703369
- [x] Emit `[]` for empty rule sets in JSON mode instead of text output. Links: https://github.com/EvotecIT/IntelligenceX/pull/127#discussion_r2777703370
- [x] Test helper should capture both `stdout` and `stderr` so diagnostics stream moves do not hide regressions. Links: https://github.com/EvotecIT/IntelligenceX/pull/127#discussion_r2777703538
</details>
<details>
<summary>PR #129 Expand C# static-analysis catalog and tier pack coverage</summary>

- [x] Correct malformed CA5350 description text in generated catalog metadata. Links: https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777725816, https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777731753
- [x] Normalize CA5389 title casing in generated metadata for user-facing consistency. Links: https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777725822, https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777731774
- [x] Document interpreter-based catalog refresh command in README for portability. Links: https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777725827, https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777731783
- [x] Avoid selecting `defaultSeverity: none` rules in tier packs so enabled packs do not silently ship disabled analyzer entries. Links: https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777729122
- [x] Select latest NetAnalyzers NuGet package using semantic version ordering. Links: https://github.com/EvotecIT/IntelligenceX/pull/129#discussion_r2777729123
</details>

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

## Review Feedback Backlog (Bots)

Grouped by PR to keep backlog actionable. Includes unresolved inline threads and bot review/comment items that contain explicit action signals (checkboxes/TODO/P-levels).

### PR #63 feat: add secrets audit logging
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> When `secretsAudit` is disabled, `TryStart` clears `Pending` and returns `null`, but subsequent `Record` calls still enqueue into `Pending` forever. T... [https://github.com/EvotecIT/IntelligenceX/pull/63#discussion_r2762642058]
- [ ] (inline, copilot-pull-request-reviewer): This variable is manually [disposed](1) in a [finally block](2) - consider a C# using statement as a preferable resource management technique. [https://github.com/EvotecIT/IntelligenceX/pull/63#discussion_r2762662145]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> When `SecretsAudit` is disabled (TryStart returns null), subsequent `Record` calls will still enqueue into `Pending` and never be cleared/printed. Thi... [https://github.com/EvotecIT/IntelligenceX/pull/63#discussion_r2763458371]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Token selection still uses `??`, so an empty `INTELLIGENCEX_GITHUB_TOKEN` will block fallback to `GITHUB_TOKEN` while `tokenSource` claims `GITHUB_TOK... [https://github.com/EvotecIT/IntelligenceX/pull/63#discussion_r2763458415]
- [ ] (comment, claude): ## Code Review: feat: add secrets audit logging ### Summary This PR adds a secrets audit logging feature to track which secret sources are accessed during review operations. The im... [https://github.com/EvotecIT/IntelligenceX/pull/63#issuecomment-3845849990]
- [ ] (comment, claude): Code Review: Secrets Audit Logging Thanks for implementing this security feature! Overall, this is a solid implementation. Strengths: - Well-designed architecture with AsyncLocal s... [https://github.com/EvotecIT/IntelligenceX/pull/63#issuecomment-3846358685]
- [ ] (comment, claude): ## Code Review - PR #63: feat: add secrets audit logging ### Summary This PR adds a well-designed secrets audit logging system that records which secret sources are accessed withou... [https://github.com/EvotecIT/IntelligenceX/pull/63#issuecomment-3846793515]

### PR #68 feat: configurable triage embed placement
- [ ] (inline, copilot-pull-request-reviewer): The test coverage for ApplyEmbedPlacement is incomplete. It should test edge cases including: 1. Empty reviewBody with non-empty embedBlock 2. Non-empty reviewBody with empty embed... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2762839603]
- [ ] (inline, copilot-pull-request-reviewer): Missing test for NormalizeEmbedPlacement function. Following the pattern established in TestReviewThreadsDiffRangeNormalize (line 1043), there should be a dedicated test that verif... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2762839632]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Trimming both ends can remove intentional leading whitespace (e.g., markdown indentation or code blocks). Previously only `TrimEnd` was used. Consider... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2762840243]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `var method = typeof(ReviewerApp).GetMethod("ApplyEmbedPlacement", BindingFlags.NonPublic | BindingFlags.Static);` Reflection-based testing of a priva... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2763126855]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Right now unknown values are silently coerced. If config/schema validation isn’t guaranteed at runtime, this can hide misconfiguration. Consider retur... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2763346675]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Reflection against a private method is brittle and easy to break during refactors. Consider making the method `internal` and using `InternalsVisibleTo... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2763346717]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Split the properties onto separate lines; current formatting makes the example invalid JSON and harder to read. ```suggestion "reviewThreadsAutoResolv... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2763434394]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> ``` [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2763434457]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The two test registrations are on one line; split them to keep formatting consistent and avoid merge noise. ```suggestion failed += Run("Thread triage... [https://github.com/EvotecIT/IntelligenceX/pull/68#discussion_r2763434506]
- [ ] (comment, claude): # Code Review - PR #68: Configurable triage embed placement ## Summary This PR adds configurable placement (top/bottom) for embedded thread triage blocks in review comments. The im... [https://github.com/EvotecIT/IntelligenceX/pull/68#issuecomment-3846100118]
- [ ] (comment, claude): ## Code Review - PR #68: Configurable Triage Embed Placement ### Summary This PR adds configurable placement (top/bottom) for embedded thread triage blocks in review comments. The ... [https://github.com/EvotecIT/IntelligenceX/pull/68#issuecomment-3846296877]
- [ ] (comment, claude): ## Code Review: Configurable Triage Embed Placement ### Summary This PR adds the ability to configure placement of embedded thread triage blocks (top vs bottom) in review comments.... [https://github.com/EvotecIT/IntelligenceX/pull/68#issuecomment-3846398995]
- [ ] (comment, claude): ## Code Review: Configurable Triage Embed Placement ### Summary This PR adds a configurable placement option for embedded thread triage blocks, allowing users to position them at t... [https://github.com/EvotecIT/IntelligenceX/pull/68#issuecomment-3846656117]
- [ ] (comment, claude): # Code Review: PR #68 - Configurable Triage Embed Placement ## Summary This PR adds the ability to configure whether embedded thread triage blocks appear at the top or bottom of re... [https://github.com/EvotecIT/IntelligenceX/pull/68#issuecomment-3846680440]
- [ ] (comment, claude): ## Code Review: Configurable Triage Embed Placement ### Summary This PR adds a new configuration option to control whether AI-generated thread triage blocks appear at the top or bo... [https://github.com/EvotecIT/IntelligenceX/pull/68#issuecomment-3846767503]

### PR #67 feat: post auto-resolve summary comment
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Guard summary comment with allowCommentPost** The new summary comment bypasses the `allowComm... [https://github.com/EvotecIT/IntelligenceX/pull/67#discussion_r2762687354]
- [ ] (inline, copilot-pull-request-reviewer): `BuildThreadAutoResolveSummaryComment` posts a summary comment even when `allowCommentPost` is `false`, because this condition only checks `ReviewThreadsAutoResolveSummaryComment`.... [https://github.com/EvotecIT/IntelligenceX/pull/67#discussion_r2762700199]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The `!commentPosted` guard suppresses the summary if any earlier comment was posted. The feature reads like an additional standalone summary, so this ... [https://github.com/EvotecIT/IntelligenceX/pull/67#discussion_r2762710535]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The `!commentPosted` guard prevents the “standalone summary comment” from posting when a main review comment was already posted. If the intent is a st... [https://github.com/EvotecIT/IntelligenceX/pull/67#discussion_r2763332230]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_COMMENT` looks like a typo; it likely won’t be set by users and adds confusion. Consider removing or corre... [https://github.com/EvotecIT/IntelligenceX/pull/67#discussion_r2763332281]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `GetInput` includes `"REVIEW_REVIEW_THREADS_AUTO_RESOLVE_SUMMARY_COMMENT"` which looks like a typo/duplicate. This adds confusion for operators and do... [https://github.com/EvotecIT/IntelligenceX/pull/67#discussion_r2763366834]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `5c30ac0788` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/67#pullrequestreview-3749537069]
- [ ] (comment, claude): ## Code Review: feat: post auto-resolve summary comment ### Summary This PR adds an optional standalone summary comment for thread auto-resolve decisions. The implementation is cle... [https://github.com/EvotecIT/IntelligenceX/pull/67#issuecomment-3846256588]
- [ ] (comment, claude): ## Code Review: PR #67 - Add auto-resolve summary comment ### Summary This PR adds an optional standalone summary comment for thread auto-resolve decisions. The implementation is w... [https://github.com/EvotecIT/IntelligenceX/pull/67#issuecomment-3846328605]
- [ ] (comment, claude): ## Code Review: feat: post auto-resolve summary comment ### Overview This PR adds an optional standalone summary comment for thread auto-resolve decisions. The implementation is so... [https://github.com/EvotecIT/IntelligenceX/pull/67#issuecomment-3846627521]
- [ ] (comment, claude): ## Code Review: PR #67 - Post auto-resolve summary comment ### Summary This PR adds a new optional feature to post a standalone summary comment listing auto-resolved and kept revie... [https://github.com/EvotecIT/IntelligenceX/pull/67#issuecomment-3846641235]

### PR #66 feat: enforce bot allowlist for auto-resolve
- [ ] (inline, copilot-pull-request-reviewer): The new allowlist test covers the “non-allowlisted bot is rejected” case, but it doesn’t exercise the documented behavior that an explicitly empty `ReviewThreadsAutoResolveBotLogin... [https://github.com/EvotecIT/IntelligenceX/pull/66#discussion_r2762693006]
- [ ] (inline, copilot-pull-request-reviewer): Doc wording is a bit ambiguous: the allowlist is “set” by default (non-empty), so saying “When set” can be confusing. Consider rephrasing to “When non-empty, it acts as an allowlis... [https://github.com/EvotecIT/IntelligenceX/pull/66#discussion_r2762693020]
- [ ] (inline, copilot-pull-request-reviewer): This method is named `ThreadHasOnlyBotComments`, but after switching the predicate to `IsAutoResolveBot` it now means “only comments from auto-resolve-eligible bots” (i.e., allowli... [https://github.com/EvotecIT/IntelligenceX/pull/66#discussion_r2762693033]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `ReviewThreadsAutoResolveBotLogins` may be null depending on how settings are constructed. This will throw. Consider null-safe handling to preserve cu... [https://github.com/EvotecIT/IntelligenceX/pull/66#discussion_r2762694873]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If the allowlist contains only whitespace/invalid entries, the loop skips them but the method still returns `false` and never falls back to generic bo... [https://github.com/EvotecIT/IntelligenceX/pull/66#discussion_r2763312412]
- [ ] (comment, claude): ## Code Review - PR #66: Enforce bot allowlist for auto-resolve ### Summary This PR introduces an allowlist mechanism for bot auto-resolve functionality, creating a new `IsAutoReso... [https://github.com/EvotecIT/IntelligenceX/pull/66#issuecomment-3845907966]
- [ ] (comment, claude): # Code Review: PR #66 - feat: enforce bot allowlist for auto-resolve ## Overview This PR introduces a new `IsAutoResolveBot` method that enforces an allowlist-based approach for bo... [https://github.com/EvotecIT/IntelligenceX/pull/66#issuecomment-3846283821]
- [ ] (comment, claude): ## Code Review - PR #66: Enforce bot allowlist for auto-resolve ### Summary This PR introduces a proper allowlist mechanism for bot auto-resolve functionality, separating the conce... [https://github.com/EvotecIT/IntelligenceX/pull/66#issuecomment-3846320162]
- [ ] (comment, claude): ## Code Review: PR #66 - Enforce bot allowlist for auto-resolve ### Summary This PR introduces an allowlist mechanism for auto-resolving bot comments, which is a good feature addit... [https://github.com/EvotecIT/IntelligenceX/pull/66#issuecomment-3846610886]

### PR #65 feat: always summarize thread auto-resolve
- [ ] (inline, copilot-pull-request-reviewer): In the both-resolved-and-kept case, the message "Auto-resolve: resolved {resolved.Count}, kept {kept.Count} thread(s)." only attaches "thread(s)" to the kept count, which makes the... [https://github.com/EvotecIT/IntelligenceX/pull/65#discussion_r2762677561]
- [ ] (inline, copilot-pull-request-reviewer): The fallback summary builder has multiple output branches (no threads, resolved-only, kept-only, resolved+kept), but this test only validates the resolved-only branch. Adding asser... [https://github.com/EvotecIT/IntelligenceX/pull/65#discussion_r2762677581]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The new test only covers the “resolved only” path. Please add cases for “kept only”, “resolved + kept”, and “none” to fully exercise the fallback summ... [https://github.com/EvotecIT/IntelligenceX/pull/65#discussion_r2762682946]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Clarity: this reads like “thread(s)” only applies to kept. Consider mirroring the wording used in other branches for consistency. ```suggestion return... [https://github.com/EvotecIT/IntelligenceX/pull/65#discussion_r2763001859]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Test only covers the “resolved only” case. Add assertions for “kept only” and “resolved + kept” to prevent regressions in summary formatting. --- [https://github.com/EvotecIT/IntelligenceX/pull/65#discussion_r2763001942]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Using reflection on a private method makes the test brittle to renames and refactors. Consider making `BuildFallbackTriageSummary` `internal` and add ... [https://github.com/EvotecIT/IntelligenceX/pull/65#discussion_r2763101031]
- [ ] (comment, claude): ## Code Review - PR #65: feat: always summarize thread auto-resolve ### Summary This PR adds a new configuration option `reviewThreadsAutoResolveSummaryAlways` that enables an alwa... [https://github.com/EvotecIT/IntelligenceX/pull/65#issuecomment-3845896751]
- [ ] (comment, claude): ## Code Review: Always Summarize Thread Auto-Resolve ### Summary This PR adds an optional always-on triage summary line in the main review comment via the new `reviewThreadsAutoRes... [https://github.com/EvotecIT/IntelligenceX/pull/65#issuecomment-3846371707]

### PR #64 feat: require diff evidence for thread auto-resolve
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Ensure evidence-failed resolves still get auto-reply** When `reviewThreadsAutoResolveRequireE... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762660550]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `var patchIndex = BuildInlinePatchIndex(files);` You build the patch index/lookup unconditionally even when evidence isn’t required. This adds work on... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762668143]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `var normalized = evidence.Trim().Trim('"');` [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762668192]
- [ ] (inline, copilot-pull-request-reviewer): When evidence is missing, you add a synthetic `keep` assessment to `kept`, but `byId` still maps the thread id to the original `resolve` assessment. `ReplyToKeptThreadsAsync(...)` ... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762674491]
- [ ] (inline, copilot-pull-request-reviewer): On resolve failure, a synthetic `keep` assessment is added to `failed`/`kept`, but `byId` still contains the original `resolve` action for that thread id. This means `ReplyToKeptTh... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762674516]
- [ ] (inline, copilot-pull-request-reviewer): New auto-resolve behavior is introduced here (requiring and validating diff evidence via `HasValidResolveEvidence`), but tests only cover JSON parsing of the `evidence` field. Cons... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762674532]
- [ ] (inline, copilot-pull-request-reviewer): `patchIndex`/`patchLookup` are built unconditionally, even when `ReviewThreadsAutoResolveRequireEvidence` is false. This duplicates work already done in `BuildThreadAssessmentPromp... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762674553]
- [ ] (inline, copilot-pull-request-reviewer): `HasValidResolveEvidence` currently validates by `context.Contains(normalized, OrdinalIgnoreCase)`. This can be trivially satisfied with very short/generic evidence (e.g., a single... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762674574]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Building patch indexes unconditionally can be expensive when `ReviewThreadsAutoResolveRequireEvidence` is false. Consider gating this work. ```suggest... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762991991]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `Evidence` appears optional and can be missing in input; keeping it non-nullable will force nulls or warnings. Make it nullable to reflect reality. ``... [https://github.com/EvotecIT/IntelligenceX/pull/64#discussion_r2762992078]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `9de5da56d5` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/64#pullrequestreview-3749503472]
- [ ] (comment, claude): ## Code Review: PR #64 - Require diff evidence for thread auto-resolve ### Summary This PR adds a safety mechanism to require explicit diff evidence before auto-resolving review th... [https://github.com/EvotecIT/IntelligenceX/pull/64#issuecomment-3845877827]
- [ ] (comment, claude): # Code Review - PR 64: feat: require diff evidence for thread auto-resolve ## Summary This PR adds a safety mechanism to require explicit diff evidence before auto-resolving review... [https://github.com/EvotecIT/IntelligenceX/pull/64#issuecomment-3846227032]

### PR #62 feat: block self-modifying workflow runs
- [ ] (inline, copilot-pull-request-reviewer): The guardrail description says it skips PRs that modify GitHub Actions workflows, but the implementation only treats `.github/workflows/*.yml|*.yaml` as workflow changes (and expli... [https://github.com/EvotecIT/IntelligenceX/pull/62#discussion_r2762617291]
- [ ] (inline, copilot-pull-request-reviewer): This bullet implies any `.github/workflows/*` change is allowed/blocked, but the code only considers `.yml`/`.yaml` files under `.github/workflows/`. Please reword to `.github/work... [https://github.com/EvotecIT/IntelligenceX/pull/62#discussion_r2762617312]
- [ ] (inline, copilot-pull-request-reviewer): The skip message is a bit ambiguous about where to set the config: the JSON key lives under the `review` object (i.e., `review.allowWorkflowChanges`) and there is also a GitHub Act... [https://github.com/EvotecIT/IntelligenceX/pull/62#discussion_r2762617330]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop [implicitly filters its target sequence](1) - consider filtering the sequence explicitly using '.Where(...)'. ```suggestion return files.Any(file => IsWorkflowPat... [https://github.com/EvotecIT/IntelligenceX/pull/62#discussion_r2762617350]
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub> Detect workflow renames out of .github/workflows** The workflow guard only checks `file.Filen... [https://github.com/EvotecIT/IntelligenceX/pull/62#discussion_r2762619543]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Add a `.yaml` case to ensure both extensions are covered by the guardrail. ```suggestion private static void TestWorkflowChangesDetection() { var with... [https://github.com/EvotecIT/IntelligenceX/pull/62#discussion_r2762942395]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `211d200645` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/62#pullrequestreview-3749459863]
- [ ] (comment, claude): ## Code Review - PR #62: feat: block self-modifying workflow runs ### Summary This PR implements a security guardrail to prevent self-modifying workflow runs by detecting and skipp... [https://github.com/EvotecIT/IntelligenceX/pull/62#issuecomment-3845832492]
- [ ] (comment, claude): ## Code Review: PR #62 - feat: block self-modifying workflow runs ### Summary This PR implements a security guardrail to prevent self-modifying workflow runs by detecting and skipp... [https://github.com/EvotecIT/IntelligenceX/pull/62#issuecomment-3846191650]

### PR #69 feat: explain kept threads on auto-resolve
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `var byId = assessments.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);` If this map is no longer used after switching to `replyMap`, remov... [https://github.com/EvotecIT/IntelligenceX/pull/69#discussion_r2762858023]
- [ ] (comment, claude): # Code Review - PR #69: feat: explain kept threads on auto-resolve ## Summary This PR improves the auto-resolve feature by ensuring that explanations are provided for both kept thr... [https://github.com/EvotecIT/IntelligenceX/pull/69#issuecomment-3846113774]

### PR #61 feat: add untrusted PR guardrails
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub> Delay secret loading until after untrusted check** The new guardrail says it is “skipping rev... [https://github.com/EvotecIT/IntelligenceX/pull/61#discussion_r2762598789]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Potential false positives when the base repo itself is a fork (head repo `fork=true` even if PR is same repo). Prefer comparing repo full names when a... [https://github.com/EvotecIT/IntelligenceX/pull/61#discussion_r2762604949]
- [ ] (inline, copilot-pull-request-reviewer): Condition is always true because of [access to local variable allowWrites](1). ```suggestion if (!allowWrites) { ``` [https://github.com/EvotecIT/IntelligenceX/pull/61#discussion_r2762620596]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `f216b94768` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/61#pullrequestreview-3749437979]
- [ ] (comment, claude): ## Code Review: feat: add untrusted PR guardrails This PR adds important security guardrails for handling untrusted pull requests from forks. The implementation is solid with good ... [https://github.com/EvotecIT/IntelligenceX/pull/61#issuecomment-3845811421]

### PR #60 feat: add GitHub API concurrency limit
- [ ] (inline, copilot-pull-request-reviewer): `githubMaxConcurrency` is now read from the JSON config, but there isn’t a corresponding config-based test (only env-var and constructor tests were added). Add a test that writes a... [https://github.com/EvotecIT/IntelligenceX/pull/60#discussion_r2761493334]
- [ ] (inline, copilot-pull-request-reviewer): `PatchJsonAsync` wraps a `Func<Task<int>>` and returns a dummy `0` just to satisfy `WithGateAsync<T>`. Consider adding a non-generic `WithGateAsync(Func<Task> action, CancellationT... [https://github.com/EvotecIT/IntelligenceX/pull/60#discussion_r2761493348]
- [ ] (inline, copilot-pull-request-reviewer): The new concurrency gate (`WithGateAsync`/`SemaphoreSlim`) is the core behavior change, but the tests only assert that settings/constructor values are plumbed through. Add a determ... [https://github.com/EvotecIT/IntelligenceX/pull/60#discussion_r2761493353]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Dispose `_requestGate` in `Dispose()` to avoid leaking wait handles. `SemaphoreSlim` is `IDisposable` and should be released when the client is dispos... [https://github.com/EvotecIT/IntelligenceX/pull/60#discussion_r2761499953]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> I don't see this wrapper being used in the diff. Ensure **all** outbound GitHub API calls are routed through `WithGateAsync` (or remove it). Otherwise... [https://github.com/EvotecIT/IntelligenceX/pull/60#discussion_r2761499973]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `private async Task<T> WithGateAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)` Make sure every outbound GitHub API call is routed... [https://github.com/EvotecIT/IntelligenceX/pull/60#discussion_r2762541122]
- [ ] (comment, claude): ## Code Review: GitHub API Concurrency Limit (PR #60) ### Summary This PR adds a configurable concurrency gate for GitHub API calls using `SemaphoreSlim` to prevent rate limiting. ... [https://github.com/EvotecIT/IntelligenceX/pull/60#issuecomment-3844366019]
- [ ] (comment, claude): ## Code Review: GitHub API Concurrency Limit ### Summary This PR adds a configurable concurrency gate for GitHub API calls using `SemaphoreSlim` to reduce rate limiting. The implem... [https://github.com/EvotecIT/IntelligenceX/pull/60#issuecomment-3845743256]

### PR #59 feat: add default redaction patterns
- [ ] (inline, copilot-pull-request-reviewer): The test only validates a single redaction pattern (Authorization header) and doesn't verify that other critical patterns work correctly. Consider adding test cases for other impor... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761486442]
- [ ] (inline, copilot-pull-request-reviewer): The documentation lists the secret types that are redacted but omits Slack tokens (xox[baprs]-...), which is one of the default patterns. Consider either adding Slack tokens to the... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761486456]
- [ ] (inline, copilot-pull-request-reviewer): AWS access keys (AKIA/ASIA prefix) are case-sensitive by design. Since Redaction.Apply uses RegexOptions.IgnoreCase, this pattern will match incorrectly formatted keys like "akia..... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761486472]
- [ ] (inline, copilot-pull-request-reviewer): The pattern assumes GitHub classic tokens (ghp_, gho_, ghu_, ghs_, ghr_) have exactly 36 characters after the prefix, but this may not be accurate for all GitHub token types. OAuth... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761486483]
- [ ] (inline, copilot-pull-request-reviewer): JWTs are base64url-encoded and case-sensitive. Using case-insensitive matching via RegexOptions.IgnoreCase in Redaction.Apply may cause false positives by matching strings that loo... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761486495]
- [ ] (inline, copilot-pull-request-reviewer): The inline case-insensitive flag (?i) is redundant since Redaction.Apply already uses RegexOptions.IgnoreCase. While this doesn't cause incorrect behavior, it's unnecessary and sho... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761486504]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This array is still mutable via cast (`string[]`) and shared across instances. Consider wrapping in `Array.AsReadOnly(...)` or using `ImmutableArray<s... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761488462]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Because this references the shared default list, any mutation will affect all settings instances. If you keep defaults as mutable, clone on assignment... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761488491]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Clarify how to *disable* defaults when `redactPii` is true (e.g., set `redactionPatterns` to an empty array). This is important for users who want opt... [https://github.com/EvotecIT/IntelligenceX/pull/59#discussion_r2761488520]
- [ ] (comment, claude): # Code Review: Add Default Redaction Patterns ## Summary This PR adds default redaction patterns for common secrets when `redactPii` is enabled. The implementation is solid and fol... [https://github.com/EvotecIT/IntelligenceX/pull/59#issuecomment-3844351238]

### PR #58 feat: reuse Azure DevOps HTTP handler
- [ ] (comment, claude): ## Code Review: PR #58 - feat: reuse Azure DevOps HTTP handler ### Summary This PR implements HTTP handler reuse for the `AzureDevOpsClient` to reduce socket churn and improve reso... [https://github.com/EvotecIT/IntelligenceX/pull/58#issuecomment-3844338455]

### PR #47 feat: tool docs and reviewer hints
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Keep tool execution parallel opt-in for ToolRunner** The new `runInParallel` flag is enabled ... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2760592201]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Parallel execution starts all tools even if one is unregistered. That can trigger side effects before the failure is surfaced. Consider pre-validating... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2760632780]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This always reserves a trailing newline even when `lastText` is empty, which can cause earlier truncation than necessary. Only reserve when there is t... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2760632836]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Keeping the last hunk seems useful even when there are exactly two hunks. With the current condition, a 2‑hunk patch won’t preserve the tail under tig... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2760697989]
- [ ] (inline, copilot-pull-request-reviewer): `runInParallel` is computed as an OR between `ToolRunnerOptions.ParallelToolCalls` and `ChatOptions.ParallelToolCalls`. This makes it impossible to force sequential local execution... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2761420779]
- [ ] (inline, copilot-pull-request-reviewer): Tail preservation currently depends on being able to fit the truncation marker as well (`CanAppendTail(..., includeMarker: true)` returns false if the marker can’t fit). In tight `... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2761420798]
- [ ] (inline, copilot-pull-request-reviewer): `CanAppendWithReserve` always reserves `newline.Length` for the marker and `lastText`, even when `sb.Length == 0` (in which case `TryAppendSegment` would not add a leading newline)... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2761420809]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop immediately [maps its iteration variable to another variable](1) - consider mapping the sequence explicitly using '.Select(...)'. [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2761420828]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Running a `.exe` on a Linux runner is likely to fail because .NET builds typically output a `.dll` (or a non-`.exe` host) on Linux. Prefer `dotnet <dl... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2761466216]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `repos` is nullable earlier in this method. If both API calls fail without throwing, this will throw `NullReferenceException` and return 500 instead o... [https://github.com/EvotecIT/IntelligenceX/pull/47#discussion_r2761466262]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `f9a3ec5ee5` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/47#pullrequestreview-3747083602]
- [ ] (comment, claude): ## Code Review: feat: tool docs and reviewer hints ### Summary This PR adds language detection hints to reviewer prompts, native tool-calling documentation, parallel tool execution... [https://github.com/EvotecIT/IntelligenceX/pull/47#issuecomment-3843155370]
- [ ] (comment, claude): # Code Review: PR #47 - feat: tool docs and reviewer hints ## Summary This PR adds several enhancements to the IntelligenceX project: 1. Language detection hints for reviewer promp... [https://github.com/EvotecIT/IntelligenceX/pull/47#issuecomment-3843175719]
- [ ] (comment, claude): # Code Review - PR #47: feat: tool docs and reviewer hints ## Summary This PR adds native tool calling documentation, implements parallel tool execution in `ToolRunner`, adds langu... [https://github.com/EvotecIT/IntelligenceX/pull/47#issuecomment-3843188688]
- [ ] (comment, claude): # Code Review: PR #47 - Tool docs and reviewer hints ## Summary This PR adds several valuable features including native tool-calling documentation, language hints for the reviewer,... [https://github.com/EvotecIT/IntelligenceX/pull/47#issuecomment-3843262901]
- [ ] (comment, claude): # Code Review - PR #47: Tool docs and reviewer hints ## Summary This PR adds several important features: language detection hints for reviewer prompts, native tool-calling document... [https://github.com/EvotecIT/IntelligenceX/pull/47#issuecomment-3843276480]
- [ ] (comment, claude): # Code Review - PR #47 ## Summary This PR adds several features including language hints for the reviewer, native tool-calling improvements, smart patch chunking, and extensive web... [https://github.com/EvotecIT/IntelligenceX/pull/47#issuecomment-3844318651]

### PR #57 feat: cache GitHub review context
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> These caches are plain `Dictionary` and not thread-safe. If `GitHubClient` can be used concurrently, this can race or throw. Consider `ConcurrentDicti... [https://github.com/EvotecIT/IntelligenceX/pull/57#discussion_r2761321821]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> You’re caching and returning a mutable `List<T>`. Callers could mutate it, affecting the cache. Consider caching an array or read-only wrapper. ```sug... [https://github.com/EvotecIT/IntelligenceX/pull/57#discussion_r2761321891]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Same issue as above: cached list is mutable and shared. Prefer storing an immutable snapshot. ```suggestion var result = files.ToArray(); _compareFile... [https://github.com/EvotecIT/IntelligenceX/pull/57#discussion_r2761321974]
- [ ] (comment, claude): ## Code Review - PR #57: Cache GitHub Review Context ### Overview This PR adds in-memory caching for GitHub PR metadata, file lists, and compare results to improve performance by a... [https://github.com/EvotecIT/IntelligenceX/pull/57#issuecomment-3844141283]

### PR #56 feat: add review budget summary note
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `maxFiles <= 0` currently yields zero files in GitHub flow, while Azure DevOps treats `<= 0` as “no limit.” This inconsistency can produce empty revie... [https://github.com/EvotecIT/IntelligenceX/pull/56#discussion_r2761232635]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This helper is now used across runners (and tests) but lives on the main app class. Consider moving it to a small utility/static helper class to avoid... [https://github.com/EvotecIT/IntelligenceX/pull/56#discussion_r2761232671]
- [ ] (inline, copilot-pull-request-reviewer): This call mixes named and positional arguments (`autoResolveNote: ... , budgetNote, usageLine: ...`), which is a compile-time error in C#. Pass `budgetNote` as a named argument (e.... [https://github.com/EvotecIT/IntelligenceX/pull/56#discussion_r2761239095]
- [ ] (inline, copilot-pull-request-reviewer): `PrepareFiles` breaks out immediately when `maxFiles` is 0 (because `count` starts at 0), resulting in zero files being included and a truncation note like "first 0 of N". In contr... [https://github.com/EvotecIT/IntelligenceX/pull/56#discussion_r2761239111]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `if (count >= maxFiles) {` When `maxFiles <= 0`, this breaks immediately and returns an empty list, and now also emits a misleading budget note (“firs... [https://github.com/EvotecIT/IntelligenceX/pull/56#discussion_r2761285105]
- [ ] (comment, claude): # Code Review: PR #56 - feat: add review budget summary note ## Summary This PR adds a helpful feature to inform users when review context is truncated due to file or patch charact... [https://github.com/EvotecIT/IntelligenceX/pull/56#issuecomment-3843993199]

### PR #55 fix: sanitize Azure DevOps error payloads
- [ ] (inline, copilot-pull-request-reviewer): The test coverage for SanitizeErrorContent is insufficient. The current test only covers one scenario (JSON with Authorization Bearer token), but the sanitization function has mult... [https://github.com/EvotecIT/IntelligenceX/pull/55#discussion_r2761147398]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Reflection in tests is brittle and bypasses compile-time safety. Consider making the method `internal` and adding `InternalsVisibleTo` for the test pr... [https://github.com/EvotecIT/IntelligenceX/pull/55#discussion_r2761155399]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Consider adding `RegexOptions.CultureInvariant` to avoid locale-sensitive behavior (e.g., Turkish-i). ```suggestion new Regex("(?i)(authorization|toke... [https://github.com/EvotecIT/IntelligenceX/pull/55#discussion_r2761257098]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Same as above; add `RegexOptions.CultureInvariant` for consistency and correctness across locales. ```suggestion new Regex("(?i)\\b(bearer|basic)\\s+[... [https://github.com/EvotecIT/IntelligenceX/pull/55#discussion_r2761257171]
- [ ] (comment, claude): ## Code Review: PR #55 - Sanitize Azure DevOps Error Payloads ### Summary This PR adds security sanitization to Azure DevOps API error responses before logging/throwing exceptions.... [https://github.com/EvotecIT/IntelligenceX/pull/55#issuecomment-3843860203]
- [ ] (comment, claude): ## Code Review: PR #55 - Sanitize Azure DevOps Error Payloads ### Summary This PR implements security-focused sanitization of Azure DevOps API error messages before logging or thro... [https://github.com/EvotecIT/IntelligenceX/pull/55#issuecomment-3844037825]

### PR #54 feat: add language hints to review prompts
- [ ] (inline, copilot-pull-request-reviewer): The new language-hints toggle is wired into both config loading (review.languageHints) and environment/input parsing (language_hints / REVIEW_LANGUAGE_HINTS), but the added tests o... [https://github.com/EvotecIT/IntelligenceX/pull/54#discussion_r2761070489]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop immediately [maps its iteration variable to another variable](1) - consider mapping the sequence explicitly using '.Select(...)'. [https://github.com/EvotecIT/IntelligenceX/pull/54#discussion_r2761070504]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Consider guarding the call when `IncludeLanguageHints` is false to avoid unnecessary work scanning files. This keeps prompt building cheaper for the d... [https://github.com/EvotecIT/IntelligenceX/pull/54#discussion_r2761160486]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This test only validates one language even though two languages are provided. Add an assertion for the other language to ensure multi-language hints a... [https://github.com/EvotecIT/IntelligenceX/pull/54#discussion_r2761160564]
- [ ] (comment, claude): # Code Review: Add Language Hints to Review Prompts ## Summary This PR adds a language-aware hints feature that automatically detects the programming languages in a PR and includes... [https://github.com/EvotecIT/IntelligenceX/pull/54#issuecomment-3843755198]
- [ ] (comment, claude): # Code Review: Add language hints to review prompts ## Summary This PR adds a language-aware hints feature to the code reviewer that detects file types in PRs and includes relevant... [https://github.com/EvotecIT/IntelligenceX/pull/54#issuecomment-3843778614]

### PR #53 feat: smart patch chunking
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Ensure truncation marker is present when middle hunks are dropped** When `needsTruncation` is... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761016316]
- [ ] (inline, copilot-pull-request-reviewer): When there are exactly 2 hunks and the second hunk doesn't fit within maxPatchChars, a truncation marker is added even though no content is actually being skipped. The marker sugge... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761024075]
- [ ] (inline, copilot-pull-request-reviewer): Both branches of this conditional execute the same statement to append the last hunk. The code can be simplified by removing the conditional or moving the common statement outside ... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761024098]
- [ ] (inline, copilot-pull-request-reviewer): The call to TryAppendSegment is redundant after CanAppendWithReserve returns true. CanAppendWithReserve already verifies there's sufficient space for the segment (plus marker and l... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761024111]
- [ ] (inline, copilot-pull-request-reviewer): When no middle hunks can be included (includedMiddle == 0), the code attempts to append the marker and last hunk without verifying there's sufficient space. The CanAppendWithReserv... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761024122]
- [ ] (inline, copilot-pull-request-reviewer): The new test covers the 3-hunk case where middle hunks are excluded, but lacks coverage for edge cases: (1) the 2-hunk case where the second hunk doesn't fit (lines 1254-1259), and... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761024136]
- [ ] (inline, copilot-pull-request-reviewer): The truncation marker string is hardcoded in three locations with inconsistent formats: "... (truncated) ..." in lines 1244 and 1296, and "... (truncated)" in line 1335 (TrimHard).... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761024150]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This branch prefers the truncation marker over the last hunk when the second hunk doesn’t fit. That breaks the “keep last hunk” intent. Consider alway... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761027317]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If the marker fits but the last hunk doesn’t, you can end up with a marker but no last hunk. This violates the “keep last hunk” requirement. Only add ... [https://github.com/EvotecIT/IntelligenceX/pull/53#discussion_r2761027395]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `e5f24e13bb` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/53#pullrequestreview-3747608480]
- [ ] (comment, claude): # Code Review: Smart Patch Chunking (PR #53) ## Summary This PR implements smart patch chunking that preserves first and last hunks when trimming large patches. The implementation ... [https://github.com/EvotecIT/IntelligenceX/pull/53#issuecomment-3843699578]

### PR #52 feat: usage summary line
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Distinguish code review limits in usage line** The new code review rate-limit entries can be ... [https://github.com/EvotecIT/IntelligenceX/pull/52#discussion_r2760983899]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The checklist item has an extra leading space before the dash, which breaks standard markdown list formatting. Remove the leading space. ```suggestion... [https://github.com/EvotecIT/IntelligenceX/pull/52#discussion_r2760996386]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Leading space makes this a nested list item in Markdown; likely unintended and inconsistent with the surrounding items. ```suggestion - [x] Add option... [https://github.com/EvotecIT/IntelligenceX/pull/52#discussion_r2761010897]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `a01478a8d5` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/52#pullrequestreview-3747567429]
- [ ] (comment, claude): ## Code Review: Usage Summary Line ### Summary This PR successfully implements the usage summary as a single line format, adding code review rate limits to the output. The implemen... [https://github.com/EvotecIT/IntelligenceX/pull/52#issuecomment-3843659505]
- [ ] (comment, claude): # Code Review: PR #52 - feat: usage summary line ## Summary This PR converts the usage information display from a multi-line bulleted list to a single-line pipe-delimited format, a... [https://github.com/EvotecIT/IntelligenceX/pull/52#issuecomment-3843684674]

### PR #41 feat: copilot direct transport + unified roadmap
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Removing `Environment.Clear()` means the child process will still inherit *all* host env vars, so `envAllowlist` no longer acts as an allowlist and ma... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2758053661]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> For direct transport, a missing model will flow through and can generate invalid requests. Consider enforcing a non-empty model when `_transport == Di... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2758053698]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The direct URL is only checked for empty. If it’s malformed/relative, the failure will occur deeper (and potentially less clear). Consider validating ... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2759361856]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Removing `startInfo.Environment.Clear();` changes the default behavior to inherit **all** environment variables for every Copilot CLI run. That’s a po... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2759372450]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `CopilotDirectOptions.Validate()` isn’t called, so malformed/relative URLs or negative timeouts can slip through until deeper failures. Consider valid... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2759383311]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Clearing the entire environment can break process startup on some platforms (e.g., missing `SystemRoot`, `PATH`, or locale variables). Consider preser... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2759581261]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The parser accepts `api`/`http` as aliases, but the schema rejects them. Align schema or parser to avoid confusing config validation failures. ```sugg... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2759581329]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Defaulting `InheritEnvironment` to `false` changes previous behavior and clears the environment for any callers that don’t set it explicitly. This is ... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2760000109]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `ReadStringMap` is used for both headers and environment variables. Case-insensitive keys are correct for headers but can be incorrect for env vars on... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2760142597]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If `directHeaders` contains `Authorization`, you still pass `Token` to `CopilotDirectOptions`. This can lead to conflicting auth headers depending on ... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2760377528]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Clearing the environment can break CLI startup when `CliPath` is relative or the CLI needs PATH/SystemRoot/HOME. Consider validating that `CliPath` is... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2760377570]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `TimeSpan.Zero` currently passes validation but likely causes immediate timeouts. Consider requiring a positive timeout to avoid surprising behavior. ... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2760497393]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If both `Token` and `Authorization` are supplied (via `directHeaders`), the client may send conflicting auth headers depending on `CopilotDirectClient... [https://github.com/EvotecIT/IntelligenceX/pull/41#discussion_r2760497442]
- [ ] (comment, claude): ## Code Review: Copilot Direct Transport + Unified Roadmap ### Summary This PR adds experimental direct HTTP transport for Copilot alongside the existing CLI transport, consolidate... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841630948]
- [ ] (comment, claude): ## Code Review: Copilot Direct Transport + Unified Roadmap ### Summary This PR adds experimental direct HTTP transport for Copilot alongside the existing CLI transport, with a unif... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841703143]
- [ ] (comment, claude): ### 🔍 Issues & Recommendations #### Critical: Security Concern - Environment Variable Inheritance **IntelligenceX/Providers/Copilot/CopilotClient.cs:710** The removal of startInfo... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841703899]
- [ ] (comment, claude): #### Medium: JSON Response Parsing Robustness **IntelligenceX/Providers/Copilot/Direct/CopilotDirectClient.cs:814-819** JsonLite.Parse() can throw on malformed JSON, providing poor... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841704455]
- [ ] (comment, claude): ### 🔐 Security Review **Secure Patterns:** - Tokens read from environment variables (not hardcoded) - Bearer token auth properly implemented - Timeout configured to prevent hangin... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841704981]
- [ ] (comment, claude): ### ✨ Overall Assessment **Grade: B+ (Very Good with minor issues)** This is a well-implemented feature that follows project conventions and provides good flexibility. The main con... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841705461]
- [ ] (comment, claude): # Code Review: PR #41 - Copilot Direct Transport + Unified Roadmap ## Summary This PR adds experimental direct HTTP transport for Copilot alongside the existing CLI transport, intr... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841721368]
- [ ] (comment, claude): # Code Review: PR #41 - Copilot Direct Transport ## Summary This PR adds experimental direct HTTP transport for Copilot alongside the existing CLI transport, plus enhanced environm... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3841947803]
- [ ] (comment, claude): ## Code Review: PR #41 - Copilot Direct Transport + Unified Roadmap ### Summary This PR adds experimental direct HTTP transport for Copilot, allowing bypass of the CLI for custom e... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3842427860]
- [ ] (comment, claude): ## Code Review: PR #41 - Copilot Direct Transport + Unified Roadmap ### Summary This PR adds experimental direct HTTP transport for Copilot alongside the existing CLI transport. Th... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3842577484]
- [ ] (comment, claude): # Code Review: PR #41 - Copilot Direct Transport + Unified Roadmap ## Summary This PR adds experimental direct HTTP transport for Copilot, a unified high-level CopilotChatClient, a... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3842883233]
- [ ] (comment, claude): ## Code Review: Copilot Direct Transport + Unified Roadmap Overall, this is a well-structured PR that adds experimental direct HTTP transport for Copilot alongside the existing CLI... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3842940882]
- [ ] (comment, claude): # Pull Request Review: feat: copilot direct transport + unified roadmap ## Summary This PR adds experimental direct HTTP transport support for Copilot alongside the existing CLI tr... [https://github.com/EvotecIT/IntelligenceX/pull/41#issuecomment-3843019976]

### PR #51 docs: Azure DevOps auth + changes notes
- [ ] (inline, copilot-pull-request-reviewer): The wording "(cumulative diff)" is a bit inaccurate/misleading here: the Azure DevOps PR `/changes` endpoint returns an aggregated/current change list (latest state across iteratio... [https://github.com/EvotecIT/IntelligenceX/pull/51#discussion_r2760951738]
- [ ] (inline, copilot-pull-request-reviewer): "PR-level changes endpoint (cumulative diff for the PR)" may be misleading: the ADO PR `/changes` API is used to get the aggregated/current set of changed files (latest state acros... [https://github.com/EvotecIT/IntelligenceX/pull/51#discussion_r2760951776]
- [ ] (comment, claude): ## Code Review - PR #51: docs: Azure DevOps auth + changes notes ### Summary This is a documentation-only PR that adds clarity around Azure DevOps authentication schemes and PR cha... [https://github.com/EvotecIT/IntelligenceX/pull/51#issuecomment-3843612652]

### PR #50 feat: summary stability, reasoning label, and ADO tests
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Using an existing summary as prompt context is a potential prompt-injection vector if the comment can be edited or spoofed. Please ensure `FindExistin... [https://github.com/EvotecIT/IntelligenceX/pull/50#discussion_r2760880519]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The previous summary is injected directly into the prompt. Because this content is model-generated, it can contain prompt-like text and inadvertently ... [https://github.com/EvotecIT/IntelligenceX/pull/50#discussion_r2760918628]
- [ ] (comment, claude): # Code Review: feat: summary stability, reasoning label, and ADO tests ## Summary This PR adds three features to the reviewer: summary stability to reduce noisy rewording on reruns... [https://github.com/EvotecIT/IntelligenceX/pull/50#issuecomment-3843549218]

### PR #49 fix: tool definitions determinism
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The current ordering uses `StringComparer.OrdinalIgnoreCase`. If two tool names differ only by case, the sort order can be nondeterministic across run... [https://github.com/EvotecIT/IntelligenceX/pull/49#discussion_r2760800583]
- [ ] (comment, claude): # Code Review: PR #49 - Tool Definitions Determinism ## Summary This PR addresses two important issues: 1. **Snake_case field parsing**: Adds support for parsing `response_id` in s... [https://github.com/EvotecIT/IntelligenceX/pull/49#issuecomment-3843431039]

### PR #48 feat: structured findings block
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If the comment body is empty/whitespace, this still emits an item with an empty `message`. That makes automation harder to consume and can lead to “em... [https://github.com/EvotecIT/IntelligenceX/pull/48#discussion_r2760785164]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This produces a string longer than `MaxMessageChars` (adds 3 chars). If the max length is a hard cap, trim to `MaxMessageChars - 3` before appending. ... [https://github.com/EvotecIT/IntelligenceX/pull/48#discussion_r2760785225]
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Honor structuredFindings in Azure DevOps runner** When `structuredFindings` is enabled, Azure... [https://github.com/EvotecIT/IntelligenceX/pull/48#discussion_r2760786094]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `24333c37ee` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/48#pullrequestreview-3747322842]
- [ ] (comment, claude): ## Code Review - PR #48: feat: structured findings block ### Summary This PR adds an optional structured findings JSON block for automation, making it easier for external tools to ... [https://github.com/EvotecIT/IntelligenceX/pull/48#issuecomment-3843402333]

### PR #39 Website
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The `nul` entry looks accidental (and there’s no trailing newline). It will ignore a literal `nul` file everywhere. If that’s not intentional, remove ... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2755860054]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Hard-coding a Windows-specific path makes the script non-portable and brittle for other developers. Consider making this configurable via an env var/p... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2755860077]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Using `10.0.x` assumes a preview SDK is installed on the self-hosted runner. If the website build doesn’t require .NET 10 specifically, consider using... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2755860116]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Installing the CLI without pinning a version can introduce breaking changes. Consider locking to a known-good version for reproducibility. [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2756037284]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `$PowerForgeRoot` is hardcoded to a Windows-specific absolute path. This makes the script brittle for other dev environments and CI. Consider making t... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2756037324]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The `nul` entry looks accidental and will ignore any file literally named `nul`. If not intentional, remove it. --- [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2756037424]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Tool install isn’t version-pinned, which makes builds non-reproducible and could break on upstream changes. Pin a specific version or use a tool manif... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2758185375]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Allowing plain HTTP for GitHub API base URLs can leak tokens over the wire. Consider rejecting non-HTTPS except for loopback hosts (Enterprise instanc... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2758402851]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The fetch handlers assume JSON on any status; error responses (e.g., 4xx/5xx or non-JSON) will throw and surface as a generic “Unexpected token” messa... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2758402900]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Same as above; without `res.ok` checks, errors will show as JSON parse failures instead of meaningful feedback. [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2758838181]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> On Linux self-hosted runners, the build output is typically `PowerForge.Web.Cli.dll` (and a host binary without `.exe`). Calling `.exe` is Windows-spe... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2759458079]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This hardcodes a Windows executable. On macOS/Linux, the CLI is a `.dll` (or a host binary without `.exe`). The script will fail when run cross-platfo... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2759458189]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If `$PowerForgeRoot` is still null (env var not set and local PSPublishModule missing), `Join-Path` will throw before you can fall back to the global ... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2759510060]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `Test-Path` on a null path throws. Add a null check so the script can fall back to a global tool cleanly. ```suggestion if (-not $SkipBuildTool -and $... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2759510151]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The script is cross-platform (`pwsh`), but it hardcodes the Windows `.exe`. On Linux/macOS builds this will fail. Consider resolving `.dll` (and invok... [https://github.com/EvotecIT/IntelligenceX/pull/39#discussion_r2760555046]
- [ ] (comment, claude): # Code Review: Website Branch ## Overview This is a substantial PR with **75 files changed** (+7,100 / -1,542 lines) that introduces a complete website infrastructure for Intellige... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3840315187]
- [ ] (comment, claude): ## Code Review - PR #39: Website This PR adds a comprehensive website for the IntelligenceX project along with significant enhancements to the CLI web setup wizard. The changes inc... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3840497045]
- [ ] (comment, claude): ## Code Review: PR #39 - Website Infrastructure ### Overview This PR introduces comprehensive website infrastructure for IntelligenceX with **75 files changed** (+7,195 / -1,549 li... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3841011635]
- [ ] (comment, claude): ## Code Review - PR #39: Website ### Overview This PR adds a comprehensive documentation website and enhances the web-based setup wizard. It introduces 76 changed files with 7,201 ... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3841136848]
- [ ] (comment, claude): ## Code Review - PR #39: Website ### Overview This is a substantial PR adding a complete website deployment pipeline and enhanced wizard functionality. The changes include 32 commi... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3841753380]
- [ ] (comment, claude): # Code Review - PR #39: Website ## Summary This PR adds a comprehensive documentation website with build infrastructure and enhances the web-based setup wizard. It introduces 76 fi... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3841822439]
- [ ] (comment, claude): ## Code Review - PR #39: Website ### Overview This PR adds a comprehensive website with documentation, a new web-based setup wizard UI, and deployment workflows. The changes span ~... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3843085752]
- [ ] (comment, claude): # Security & Code Review - PR #39 This PR adds substantial website infrastructure (7,311 additions). I have identified several security issues requiring attention before merge. ## ... [https://github.com/EvotecIT/IntelligenceX/pull/39#issuecomment-3843227915]

### PR #46 fix: use PR-level ADO changes
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The PR-level changes endpoint is paginated in ADO. If `GetJsonAsync` doesn’t follow continuation tokens, large PRs will silently drop files. Please en... [https://github.com/EvotecIT/IntelligenceX/pull/46#discussion_r2760465346]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `iterationIds[0]`/`latestIterationId` assume the list is ordered. If the API returns unsorted IDs, the displayed range will be misleading. Consider us... [https://github.com/EvotecIT/IntelligenceX/pull/46#discussion_r2760465402]
- [ ] (comment, claude): Code Review: PR #46 - Use PR-level ADO Changes Endpoint Summary: This PR simplifies the Azure DevOps integration by switching from iteration-level change aggregation to using the P... [https://github.com/EvotecIT/IntelligenceX/pull/46#issuecomment-3842984409]
- [ ] (comment, claude): ## Code Review: PR #46 - Fix: Use PR-level ADO Changes ### Summary This PR improves Azure DevOps integration by switching from iteration-based change retrieval to the PR-level chan... [https://github.com/EvotecIT/IntelligenceX/pull/46#issuecomment-3843062232]

### PR #45 OpenAI native tool calling
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Preserve tool-chain turns in thread history** When `options.PreviousResponseId` is set (e.g.,... [https://github.com/EvotecIT/IntelligenceX/pull/45#discussion_r2760416393]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Public constructor signature changed. This is a breaking API change for external callers. Consider adding an overload with the old signature (or make ... [https://github.com/EvotecIT/IntelligenceX/pull/45#discussion_r2760439958]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Parsing arbitrary tool input can throw if the model emits invalid JSON. Wrap this in a try/catch and fall back to `arguments = null` (or keep raw inpu... [https://github.com/EvotecIT/IntelligenceX/pull/45#discussion_r2760440000]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Register silently overwrites tools with the same name. This can hide misconfiguration. Consider rejecting duplicates (throw) or exposing an explicit “... [https://github.com/EvotecIT/IntelligenceX/pull/45#discussion_r2760440046]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Consider also reading snake_case if the server returns it; otherwise `ResponseId` may stay null in some environments. ```suggestion var responseId = t... [https://github.com/EvotecIT/IntelligenceX/pull/45#discussion_r2760517192]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Dictionary iteration order is not guaranteed; this can lead to non-deterministic tool definition ordering. Consider a stable order (e.g., by name) to ... [https://github.com/EvotecIT/IntelligenceX/pull/45#discussion_r2760517229]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `072a372121` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/45#pullrequestreview-3746866047]
- [ ] (comment, claude): # Code Review: OpenAI Native Tool Calling ## Overview This PR adds native tool calling support to the IntelligenceX library. The implementation is well-structured and follows good ... [https://github.com/EvotecIT/IntelligenceX/pull/45#issuecomment-3842959432]
- [ ] (comment, claude): # Code Review: OpenAI Native Tool Calling ## Summary This PR adds comprehensive OpenAI native tool-calling infrastructure, including tool definitions, tool calls parsing, a tool re... [https://github.com/EvotecIT/IntelligenceX/pull/45#issuecomment-3843045118]

### PR #43 Reviewer: Azure DevOps summary-only support
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Fetch full PR changes, not only latest iteration** The ADO client is calling the iteration-sc... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759172226]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `if (!await ValidateAuthAsync(settings).ConfigureAwait(false)) {` This unconditional GitHub auth validation likely fails for Azure DevOps runs (no Git... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759178670]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Invalid `azureAuthScheme` values silently fall back to `Bearer` while also setting `AzureAuthSchemeSpecified = true`. This makes typos hard to detect ... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759265503]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Unknown values silently fall back to GitHub, which can mask misconfiguration. Consider failing fast (or at least logging) to make invalid `codeHost` v... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759570379]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Trim user-provided strings to avoid subtle whitespace issues (org/project/repo/baseUrl/token env). ```suggestion settings.AzureOrganization = org.Trim... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759570435]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The `codeHost` parser silently falls back to GitHub on unknown values. That can mask typos in config and lead to running against the wrong host. Consi... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759985525]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> You call `ValidateAuthAsync` before routing to Azure DevOps. If `ValidateAuthAsync` is GitHub-specific, Azure runs will fail even when ADO auth is con... [https://github.com/EvotecIT/IntelligenceX/pull/43#discussion_r2759985597]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `aa5687e5da` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/43#pullrequestreview-3745374874]
- [ ] (comment, claude): # Code Review - PR #43: Azure DevOps Summary-Only Support ## Summary This PR adds Azure DevOps integration for summary-only code reviews. The implementation includes a new HTTP cli... [https://github.com/EvotecIT/IntelligenceX/pull/43#issuecomment-3841428137]
- [ ] (comment, claude): # Code Review: Azure DevOps Summary-Only Support ## Summary This PR adds Azure DevOps support for summary-only code reviews, extending the reviewer beyond GitHub Actions. The imple... [https://github.com/EvotecIT/IntelligenceX/pull/43#issuecomment-3841551936]
- [ ] (comment, claude): # Code Review - PR #43: Reviewer: Azure DevOps summary-only support ## Summary This PR adds Azure DevOps support for the reviewer tool in summary-only mode. The implementation is c... [https://github.com/EvotecIT/IntelligenceX/pull/43#issuecomment-3841903553]
- [ ] (comment, claude): # Code Review: Azure DevOps Summary-Only Support ## Summary This PR adds Azure DevOps support for summary-only code review comments. The implementation follows the existing GitHub ... [https://github.com/EvotecIT/IntelligenceX/pull/43#issuecomment-3842383697]

### PR #44 Consolidate TODO roadmaps
- [ ] (comment, claude): # Code Review - PR #44: Consolidate TODO roadmaps ## Summary This PR consolidates `TODO.engine.md` and `TODO.md` into a single roadmap file, updates completion status for implement... [https://github.com/EvotecIT/IntelligenceX/pull/44#issuecomment-3841570100]

### PR #42 Reviewer improvements: config validation + triage-only
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Respect triage comment-posting settings** In triage-only mode, the code always posts `triageO... [https://github.com/EvotecIT/IntelligenceX/pull/42#discussion_r2758537890]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Config validation runs *after* loading settings. If the JSON is malformed or contains invalid types, `ReviewSettings.Load()` could throw before the va... [https://github.com/EvotecIT/IntelligenceX/pull/42#discussion_r2758719003]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Avoid sharing static arrays and ensure mutability by copying into a new list. ```suggestion settings.Focus = new List<string>(focus); ``` [https://github.com/EvotecIT/IntelligenceX/pull/42#discussion_r2758802439]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Normalize the intent before storing it so diagnostics/logging don’t show whitespace/casing variants. This also keeps config and env behavior consisten... [https://github.com/EvotecIT/IntelligenceX/pull/42#discussion_r2758856847]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Same normalization issue for environment-provided intent; store canonical values to avoid confusing output. ```suggestion var intent = GetInput("inten... [https://github.com/EvotecIT/IntelligenceX/pull/42#discussion_r2758856920]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `b59869881b` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/42#pullrequestreview-3744638314]
- [ ] (comment, claude): # Code Review: PR #42 - Reviewer improvements: config validation + triage-only ## Summary This PR adds three significant improvements to the reviewer: 1. **Config validation** agai... [https://github.com/EvotecIT/IntelligenceX/pull/42#issuecomment-3840667321]
- [ ] (comment, claude): ## Pull Request Review - PR #42 ### Summary This PR adds several valuable improvements to the IntelligenceX Reviewer: 1. **Config validation** against JSON schema with warnings/err... [https://github.com/EvotecIT/IntelligenceX/pull/42#issuecomment-3840878993]
- [ ] (comment, claude): ## Code Review - PR #42: Reviewer improvements: config validation + triage-only ### Overall Assessment This is a well-structured PR that adds valuable features to the reviewer syst... [https://github.com/EvotecIT/IntelligenceX/pull/42#issuecomment-3840933829]
- [ ] (comment, claude): ## Code Review: PR #42 - Reviewer improvements: config validation + triage-only ### Summary This PR adds significant improvements to the IntelligenceX reviewer system with configur... [https://github.com/EvotecIT/IntelligenceX/pull/42#issuecomment-3840969482]
- [ ] (comment, claude): # Code Review: PR #42 - Reviewer improvements: config validation + triage-only ## Summary This PR adds schema-based configuration validation, triage-only mode for thread resolution... [https://github.com/EvotecIT/IntelligenceX/pull/42#issuecomment-3841027176]
- [ ] (comment, claude): ## Code Review for PR #42: Reviewer improvements: config validation + triage-only ### Overview This PR introduces three significant improvements to the reviewer engine: 1. **Config... [https://github.com/EvotecIT/IntelligenceX/pull/42#issuecomment-3841073391]

### PR #40 Docs: polish reviewer config and CLI usage
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The docs elsewhere use `--github-token` (and env `INTELLIGENCEX_GITHUB_TOKEN`). If the CLI flag is `--github-token`, this line is inconsistent and cou... [https://github.com/EvotecIT/IntelligenceX/pull/40#discussion_r2755952111]

### PR #37 Docs: clarify config load exception wording
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The XML doc now claims `FileNotFoundException` is thrown when parsing fails. That’s misleading for consumers and can hide parse errors. Consider throw... [https://github.com/EvotecIT/IntelligenceX/pull/37#discussion_r2755826722]
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Document parse failures with correct exception type** If the JSON is malformed, `JsonLite.Par... [https://github.com/EvotecIT/IntelligenceX/pull/37#discussion_r2755827023]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `956fedf3d5` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/37#pullrequestreview-3741353779]

### PR #35 Docs: add XML docs for config models
- [ ] (inline, copilot-pull-request-reviewer): The documentation states the method throws when the configuration "cannot be found or parsed", but the exception documentation only mentions FileNotFoundException for when the file... [https://github.com/EvotecIT/IntelligenceX/pull/35#discussion_r2754532717]

### PR #36 Add Claude Code GitHub Workflow
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Add guard for missing secret/drafts to avoid failing PR checks** The job no longer has an `if... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755477096]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> You removed the guard that checked for `CLAUDE_CODE_OAUTH_TOKEN`. Without it, this workflow will run and fail on repos/environments missing the secret... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755478836]
- [ ] (inline, copilot-pull-request-reviewer): The PR description states "Only users with write access to the repository can trigger the workflow", but this is not enforced in the workflow configuration. Any user who can commen... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483309]
- [ ] (inline, copilot-pull-request-reviewer): The permissions for `pull-requests` and `issues` have been reduced from `write` to `read`. This means the Claude Code Review workflow will not be able to post review comments or cr... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483337]
- [ ] (inline, copilot-pull-request-reviewer): The plugin_marketplaces URL uses 'https://github.com/anthropics/claude-code.git' which is a git repository URL. This should typically be a marketplace URL or registry endpoint, not... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483352]
- [ ] (inline, copilot-pull-request-reviewer): The prompt uses GitHub repository and PR number to construct a URL-like path, but it's unclear if this format is correct for the code-review plugin. The format '${{ github.reposito... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483368]
- [ ] (inline, copilot-pull-request-reviewer): The `additional_permissions` parameter is redundant because `actions: read` is already declared in the job's permissions block (line 26). This duplication could cause confusion. Ei... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483385]
- [ ] (inline, copilot-pull-request-reviewer): The permissions configuration lacks write access for `pull-requests` and `issues`. For Claude to perform actions like creating comments, branches, and commits as described in the P... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483397]
- [ ] (inline, copilot-pull-request-reviewer): The workflow uses `ubuntu-latest` (GitHub-hosted runner) while all other workflows in this repository use `[self-hosted, ubuntu]`. This inconsistency may indicate a configuration e... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483414]
- [ ] (inline, copilot-pull-request-reviewer): The workflow uses `ubuntu-latest` (GitHub-hosted runner) while all other workflows in this repository use `[self-hosted, ubuntu]`. This inconsistency may indicate a configuration e... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483430]
- [ ] (inline, copilot-pull-request-reviewer): The original workflow had a conditional check that prevented it from running on draft PRs and when the PR title contained '[skip-review]'. This safeguard has been removed, which me... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755483443]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The trigger is currently open to any commenter who can mention `@claude`. If the intent is “write access only,” add an `author_association` check (e.g... [https://github.com/EvotecIT/IntelligenceX/pull/36#discussion_r2755699906]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `fe0139126b` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/36#pullrequestreview-3740923281]

### PR #32 Reviewer: add include/exclude path filters
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `allFiles` is still the unfiltered list. If any downstream logic uses `allFiles` for triage/summary/context, excluded files may still be processed. Co... [https://github.com/EvotecIT/IntelligenceX/pull/32#discussion_r2753654960]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Patterns are not normalized, but filenames are normalized to `/`. If a config uses backslashes, the patterns won’t match. Normalize include/exclude pa... [https://github.com/EvotecIT/IntelligenceX/pull/32#discussion_r2753655046]

### PR #33 Reviewer: add review diff-range selection
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `b935519ec2` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/33#pullrequestreview-3738081668]

### PR #31 Reviewer: add native connectivity preflight
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `cd35526066` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/31#pullrequestreview-3737984785]

### PR #29 Reviewer: add error classification + retry diagnostics
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> HttpClient timeouts commonly surface as `TaskCanceledException` without a cancellation request. Currently these get classified as `Cancelled` (non‑tra... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753053096]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `Unwrap` currently falls through to `ex.InnerException` even when an `AggregateException` has multiple inner exceptions, which effectively discards th... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753053192]
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub> Preserve cancellation when classifying nested exceptions** Because `Classify` first calls `Un... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753053393]
- [ ] (inline, copilot-pull-request-reviewer): The new Classify method introduces significant error categorization logic but lacks test coverage. Given that the repository has comprehensive tests for ReviewDiagnostics (e.g., Te... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069281]
- [ ] (inline, copilot-pull-request-reviewer): The ClassifyStatusCode method lacks test coverage. Since the repository has comprehensive tests for ReviewDiagnostics, tests should be added to verify that different HTTP status co... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069312]
- [ ] (inline, copilot-pull-request-reviewer): The ReviewRetryState tracking logic lacks test coverage. Since the repository has comprehensive tests for ReviewRetryPolicy (e.g., TestReviewRetryTransient, TestReviewRetryExtraAtt... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069329]
- [ ] (inline, copilot-pull-request-reviewer): Adding a required parameter to the RunAsync method is a breaking API change. This will break existing test code that calls this method (e.g., TestReviewRetryTransient, TestReviewRe... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069344]
- [ ] (inline, copilot-pull-request-reviewer): Adding a required parameter to BuildFailureBody is a breaking API change. This will break existing test code that calls this method (e.g., TestReviewFailureMarker in IntelligenceX.... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069366]
- [ ] (inline, copilot-pull-request-reviewer): Adding a required parameter to LogFailure is a breaking API change. Consider making the parameter optional with a default value of null: 'ReviewRetryState? retryState = null' to ma... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069406]
- [ ] (inline, copilot-pull-request-reviewer): The Unwrap method could cause infinite recursion if an exception has a circular reference (where an exception's InnerException chain eventually refers back to itself). While this i... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069426]
- [ ] (inline, copilot-pull-request-reviewer): The string matching here is overly broad. The word "invalid" appears in many error messages that aren't configuration-related (e.g., "Invalid state", "Invalid token"). This could l... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753069439]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Unwrapping down to the innermost exception can drop `HttpRequestException.StatusCode` (the outer exception often carries the HTTP status). That will m... [https://github.com/EvotecIT/IntelligenceX/pull/29#discussion_r2753093846]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `1777b55f8c` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/29#pullrequestreview-3737858615]

### PR #28 Reviewer: improve usage summary output
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If `LimitWindowSeconds` is 0/negative, the label becomes `0s limit`, which is misleading. Consider returning null to fall back to the generic label. `... [https://github.com/EvotecIT/IntelligenceX/pull/28#discussion_r2753011643]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Rounding can overstate the duration (e.g., 59.6m → 60m). Prefer floor to avoid inflating the label. ```suggestion return $"{(int)Math.Floor(span.Total... [https://github.com/EvotecIT/IntelligenceX/pull/28#discussion_r2753011687]
- [ ] (inline, copilot-pull-request-reviewer): The modulo operator on double values (span.TotalDays % 1 and span.TotalHours % 1) may not work reliably due to floating point precision issues. For example, a value that should be ... [https://github.com/EvotecIT/IntelligenceX/pull/28#discussion_r2753020239]

### PR #26 Docs: add engine roadmap
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The `nul` entry looks accidental and will ignore a file literally named `nul` on non-Windows platforms. If this was meant to ensure a newline, please ... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752961195]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> ```suggestion cancel-in-progress: true ``` [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752961230]
- [ ] (inline, copilot-pull-request-reviewer): The PR title/description indicate this is only adding an engine roadmap doc, but this change set introduces a full Website (theme/layout/assets) plus a GitHub Pages deployment work... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977828]
- [ ] (inline, copilot-pull-request-reviewer): `dotnet tool install --global PowerForge.Web.Cli` can fail on self-hosted runners where the global tool is already installed (state persists between jobs). Consider switching to `d... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977840]
- [ ] (inline, copilot-pull-request-reviewer): This section says the tool optionally adds `.intelligencex/config.json`, while other docs in this PR (and the reviewer config schema) reference `.intelligencex/reviewer.json`. Plea... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977850]
- [ ] (inline, copilot-pull-request-reviewer): The mobile nav toggle button is missing `aria-expanded` state and an `aria-controls` relationship to the nav element. Since JS toggles visibility, please update the button + script... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977862]
- [ ] (inline, copilot-pull-request-reviewer): This partial (`toc.html`) isn’t referenced anywhere in the theme/layouts (no `include "toc"` usages found), so it will be dead code as-is. Either wire it into the docs/page layouts... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977876]
- [ ] (inline, copilot-pull-request-reviewer): Same issue as above: `runs-on: [self-hosted, linux]` may not match the repository’s existing runner labels (which appear to use `ubuntu`). If the label doesn’t exist, the deploy jo... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977894]
- [ ] (inline, copilot-pull-request-reviewer): This section instructs users to create `.intelligencex/reviewer.json`, but the current setup tooling also uses `.intelligencex/config.json` for some scenarios. Please align this pa... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977905]
- [ ] (inline, copilot-pull-request-reviewer): This page says the reviewer is configured via `.intelligencex/reviewer.json`, but the CLI setup flow currently writes `.intelligencex/config.json` with different keys (e.g., `opena... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977913]
- [ ] (inline, copilot-pull-request-reviewer): This workflow uses `runs-on: [self-hosted, linux]`, but the repo’s other workflows consistently target `[self-hosted, ubuntu]`. If your runner is labeled `ubuntu` (and not `linux`)... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977927]
- [ ] (inline, copilot-pull-request-reviewer): `build.ps1` contains hard-coded, machine-specific fallback paths under `C:\Support\GitHub\...`. Even as a fallback, this is likely to confuse other contributors and won’t work on n... [https://github.com/EvotecIT/IntelligenceX/pull/26#discussion_r2752977935]

### PR #24 Add optional review usage line + auto-resolve bot defaults
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `e06539798c` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/24#pullrequestreview-3735972608]

### PR #22 Add review thread resolver and simplify release notes
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Fail-open returns a “normal” review body. Make sure downstream logic explicitly detects `FailureMarker` to suppress inline comments / thread resolutio... [https://github.com/EvotecIT/IntelligenceX/pull/22#discussion_r2751294007]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The compare API can return truncated results for large diffs. If this flag isn’t surfaced to callers, auto-resolve decisions based on diff range may b... [https://github.com/EvotecIT/IntelligenceX/pull/22#discussion_r2751294014]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `393dca2bc9` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/22#pullrequestreview-3730932552]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage Resolved: - PRRT_kwDORAe5k85r_V6D: The macOS job is now gated by a workflow variable, addressing the concern ab... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3827846234]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `e97a1d2`_ Needs attention: - PRRT_kwDORAe5k85r9Zzk: No updated diff provided showing GraphQL... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3827917732]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `3b99865`_ Needs attention: - PRRT_kwDORAe5k85r9Zzk: No updated diff or confirmation showing ... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3827957192]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `0b4d51b`_ Needs attention: - PRRT_kwDORAe5k85r9Zzk: No update shown deriving GraphQL endpoin... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828051528]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `0b4d51b`_ Needs attention: - PRRT_kwDORAe5k85r9Zzk: No diff shown deriving GraphQL endpoint ... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828122402]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `d7ee6a2`_ Needs attention: - PRRT_kwDORAe5k85r9Zz5: macOS/Windows coverage rationale or guar... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828141105]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `309d93f`_ Needs attention: - PRRT_kwDORAe5k85r9aTw: No change shown separating inline capabi... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828159453]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `309d93f`_ Needs attention: - PRRT_kwDORAe5k85r9aTw: No change shown separating inline capabi... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828166117]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `ad18607`_ Needs attention: - PRRT_kwDORAe5k85r9aTw: Diff does not show separation of inline ... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828196591]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `ad18607`_ Needs attention: - PRRT_kwDORAe5k85r9aTw: No change shown that separates inline ca... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828216111]
- [ ] (comment, intelligencex-review): <!-- intelligencex:thread-triage --> ### IntelligenceX thread triage _Assessed commit: `e040e3a`_ Needs attention: - PRRT_kwDORAe5k85r_V6D: macOS job is gated by vars.INTELLIGENCEX... [https://github.com/EvotecIT/IntelligenceX/pull/22#issuecomment-3828329280]

### PR #20 Setup wizard + web onboarding
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `980481d3a5` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/20#pullrequestreview-3727594977]

### PR #21 Auto-resolve missing inline review threads
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `0adc6a077f` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/21#pullrequestreview-3730237487]

### PR #19 Reviewer: add summary parser test
- [ ] (inline, copilot-pull-request-reviewer): This adds a duplicate InternalsVisibleTo attribute. The project already has InternalsVisibleTo configured in IntelligenceX.Reviewer.csproj (line 19-21). Having the same attribute i... [https://github.com/EvotecIT/IntelligenceX/pull/19#discussion_r2745482229]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If this assembly is strong-named, `InternalsVisibleTo` requires the public key; otherwise builds/tests will fail in signed configurations. Please conf... [https://github.com/EvotecIT/IntelligenceX/pull/19#discussion_r2745486672]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `return false` on a malformed marker line prevents scanning later lines. Consider `continue` so a later valid marker can still be parsed. --- [https://github.com/EvotecIT/IntelligenceX/pull/19#discussion_r2745486720]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Add a case where a malformed marker line appears before a valid one to ensure the parser scans all lines and doesn’t fail early. --- [https://github.com/EvotecIT/IntelligenceX/pull/19#discussion_r2745513847]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The new parser now **continues** after encountering a marker with malformed backticks, whereas the old inline logic in `Program.ExtractReviewedCommit`... [https://github.com/EvotecIT/IntelligenceX/pull/19#discussion_r2745548861]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> ```suggestion var slice = line.Substring(index + marker.Length); var start = slice.IndexOf('`'); if (start < 0) { return false; } slice = slice.Substr... [https://github.com/EvotecIT/IntelligenceX/pull/19#discussion_r2745553930]

### PR #16 Add review-thread triage context
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Silent capping at 100 means `reviewThreadsMaxComments > 100` is ignored without notice, which can mislead users and affect auto-resolve logic. Conside... [https://github.com/EvotecIT/IntelligenceX/pull/16#discussion_r2745402245]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Hard-coded page size always fetches 50 threads even when `maxThreads` is small. Consider passing a `$threadLimit` variable set to `min(50, maxThreads ... [https://github.com/EvotecIT/IntelligenceX/pull/16#discussion_r2745402274]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Auto-resolve bots-only checks can be wrong when comments are truncated. Guard against partial data using `TotalComments`. ```suggestion if (thread.Tot... [https://github.com/EvotecIT/IntelligenceX/pull/16#discussion_r2745402320]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The loop condition can still allow `threads` to exceed `maxThreads` within a single page if you add all nodes without checking. Consider breaking insi... [https://github.com/EvotecIT/IntelligenceX/pull/16#discussion_r2745635934]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Auto-resolving threads mutates server state, but `threads` still reflects the pre-resolve status. The rendered section can show threads as unresolved ... [https://github.com/EvotecIT/IntelligenceX/pull/16#discussion_r2745635998]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Casting `long` to `int` without bounds checking can overflow for large values in config. Guard against values > `int.MaxValue` or fall back when out o... [https://github.com/EvotecIT/IntelligenceX/pull/16#discussion_r2745636040]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `cec698d898` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/16#pullrequestreview-3725032530]

### PR #17 Release notes: add skip-review controls
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `079c7ce9d2` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/17#pullrequestreview-3726899135]

### PR #18 Reviewer: show commit in summary
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `summary.Body` can be null (Octokit models are nullable at runtime). This will throw before `ExtractReviewedCommit` can guard. Add a null/whitespace c... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745334252]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> As written, old summaries without a commit line are never overwritten when `overwriteSummaryOnNewCommit` is true, leaving stale summaries indefinitely... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745334314]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> You now always call `FindExistingSummaryAsync` for sticky comments even when both overwrite flags are false. That’s an unnecessary API call and can sl... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745351267]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> --- [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745351300]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The new stale-check won’t overwrite older summaries that don’t include the marker (i.e., all pre-change comments). That leaves stale summaries even wh... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745418613]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Current flow likely falls through to create a new summary comment when `existing` is found but `shouldOverwrite` is false (e.g., `overwriteSummaryOnNe... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745427199]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Parsing requires backticks. If a custom template removes backticks (or formats differently), this returns `null` and triggers perpetual overwrites. Co... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745444649]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> If the line with the marker is malformed (no backticks), returning `null` stops the scan and forces an overwrite even if a valid marker exists later. ... [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745460941]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Same issue as above for a missing closing backtick; continue scanning to avoid false negatives. ```suggestion if (end < 0) { continue; } ``` [https://github.com/EvotecIT/IntelligenceX/pull/18#discussion_r2745460980]

### PR #15 Add retry tests + release-notes PR mode
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `1390c132df` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/15#pullrequestreview-3724132222]

### PR #10 Review smoke test (2026-01-29)
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> The method currently returns a non-null string but can throw on null; clarify behavior (return empty string vs throw) and document/handle accordingly.... [https://github.com/EvotecIT/IntelligenceX/pull/10#discussion_r2740783227]

### PR #14 Add default context guardrails for reviewer
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Avoid default deny patterns that match domain terms** These defaults include broad domain wor... [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741983445]
- [ ] (inline, copilot-pull-request-reviewer): The MatchesDenyPatterns method creates new Regex instances on every call, which can be inefficient since regex patterns are static. Consider using compiled static Regex instances s... [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741991982]
- [ ] (inline, copilot-pull-request-reviewer): The PR description (context.Body) is passed directly to the prompt without applying the deny-pattern filtering, but comments are filtered. This creates an inconsistency where malic... [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741991999]
- [ ] (inline, copilot-pull-request-reviewer): The catch block silently ignores all exceptions when compiling or matching regex patterns. This makes it difficult to diagnose issues with user-provided patterns. Consider logging ... [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741992021]
- [ ] (inline, copilot-pull-request-reviewer): The regex pattern at line 37 has a subtle issue. The pattern "\\bdiagnos(e|is)\\b" won't match "diagnosis" correctly because the word boundary \b expects a non-word character after... [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741992058]
- [ ] (inline, copilot-pull-request-reviewer): The new MatchesDenyPatterns functionality lacks test coverage. Given that the codebase includes comprehensive tests for other reviewer functionality (e.g., TestCleanupNormalizeAllo... [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741992082]
- [ ] (inline, copilot-pull-request-reviewer): These 'if' statements can be combined. [https://github.com/EvotecIT/IntelligenceX/pull/14#discussion_r2741992099]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `1327e2d172` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/14#pullrequestreview-3723146462]

### PR #13 Add release notes generator + workflow
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Fix area summary when no --from tag is provided** When the repo has no tags (or the user omit... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741819177]
- [ ] (inline, copilot-pull-request-reviewer): Using 'Substring' is less efficient and more error-prone than using span-based alternatives in modern C#. Consider using 'AsSpan()' or string indexing with ranges to avoid allocati... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847790]
- [ ] (inline, copilot-pull-request-reviewer): Potential command injection vulnerability. The 'range' parameter is constructed from user-supplied 'fromTag' and 'toRef' values and directly interpolated into a git command without... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847828]
- [ ] (inline, copilot-pull-request-reviewer): File write operations (WriteAllText) can fail due to permissions, disk space, or file locks, but these exceptions are only caught by the top-level catch block which only prints ex.... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847848]
- [ ] (inline, copilot-pull-request-reviewer): The PrintHelp method does not document several options that are actually parsed by the argument parser: '--retry-count', '--retry-delay-seconds', '--retry-max-delay-seconds', and '... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847864]
- [ ] (inline, copilot-pull-request-reviewer): The namespace is declared as 'IntelligenceX.Cli.Release' but the file is in the 'ReleaseNotes' directory. This creates an inconsistency between the filesystem structure and namespa... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847887]
- [ ] (inline, copilot-pull-request-reviewer): The ParseTransport method is duplicated in both ReleaseNotesOptions (lines 367-377) and OpenAiReleaseNotesClient (lines 467-477) with identical implementation. This violates the DR... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847911]
- [ ] (inline, copilot-pull-request-reviewer): Lines 402-405 are unreachable code. If a successful attempt occurs, the method returns at line 390. If all attempts fail with transient exceptions, the last attempt will not be cau... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847934]
- [ ] (inline, copilot-pull-request-reviewer): Potential command injection vulnerability. The 'range' parameter is constructed from user-supplied 'fromTag' and 'toRef' values and directly interpolated into a git command without... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847959]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop [implicitly filters its target sequence](1) - consider filtering the sequence explicitly using '.Where(...)'. ```suggestion foreach (var output in outputs.Where(o... [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741847980]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop immediately [maps its iteration variable to another variable](1) - consider mapping the sequence explicitly using '.Select(...)'. [https://github.com/EvotecIT/IntelligenceX/pull/13#discussion_r2741848001]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `9b4a54e934` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/13#pullrequestreview-3722940466]

### PR #12 Review suggestion smoke test
- [ ] (inline, copilot-pull-request-reviewer): The null check pattern is inconsistent with the codebase conventions. Throughout the codebase, string parameters are checked using `string.IsNullOrWhiteSpace` rather than checking ... [https://github.com/EvotecIT/IntelligenceX/pull/12#discussion_r2741593134]
- [ ] (inline, copilot-pull-request-reviewer): This method will perform unnecessary operations on empty or whitespace-only strings. When input is an empty string or contains only whitespace, the method will call Trim() and ToLo... [https://github.com/EvotecIT/IntelligenceX/pull/12#discussion_r2741593159]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Prefer `string.Empty` and consider treating empty/whitespace as empty to keep normalization consistent. ```suggestion return string.Empty; ``` [https://github.com/EvotecIT/IntelligenceX/pull/12#discussion_r2741598795]

### PR #11 Fix inline comment parsing for claude output
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This treats any line containing backticks as a new comment header. That will prematurely terminate comment bodies that include inline code. Consider r... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740893761]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Using the first and last backtick will swallow multiple inline code spans and produce an oversized snippet. Prefer capturing the first complete inline... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740893791]
- [ ] (inline, copilot-pull-request-reviewer): The patch parsing logic has an off-by-one error. When a hunk header like `@@ -10,3 +10,4 @@` is parsed, `newLine` is set to 10, which should be the line number of the first line in... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740905688]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop [implicitly filters its target sequence](1) - consider filtering the sequence explicitly using '.Where(...)'. [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740905697]
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop [implicitly filters its target sequence](1) - consider filtering the sequence explicitly using '.Where(...)'. [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740905716]
- [ ] (inline, copilot-pull-request-reviewer): These 'if' statements can be combined. ```suggestion !string.IsNullOrWhiteSpace(inline.Snippet) && !TryResolveSnippet(inline.Snippet!, patchIndex, normalizedPath, out normalizedPat... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740905733]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Ensure the resolved path is normalized before lookup in `lineMap`. If `TryResolveSnippet` returns a raw path with backslashes or `./`, it may fail to ... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2740999645]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This fallback will treat any list-prefixed line containing backticks as a new inline header. That means bullet lists in comment bodies (e.g., “- use `... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2741158152]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> `patchIndex` is built even when `lineMap` is empty. Move this after the `lineMap.Count == 0` early return to avoid extra work when no inline-commentab... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2741158206]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> TaskCanceledException from HttpClient timeouts is also an OperationCanceledException; this currently prevents retries for transient timeouts. Consider... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2741588433]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Throwing the captured exception this way resets the stack trace and makes diagnosing failures harder. Use ExceptionDispatchInfo to preserve the origin... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2741588497]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> Empty responses are often transient; this currently won’t retry. Consider throwing a transient exception type so the retry loop handles it. ```suggest... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2742603611]
- [ ] (inline, intelligencex-review): <!-- intelligencex:inline --> This retries on all `HttpRequestException`s, including non-retriable 4xx (e.g., 400/401). Consider checking `StatusCode` and only retrying on 408/429/... [https://github.com/EvotecIT/IntelligenceX/pull/11#discussion_r2742603679]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `2c59bb39b9` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/11#pullrequestreview-3721791102]

### PR #6 fix(reviewer): honor openai_transport input
- [ ] (inline, copilot-pull-request-reviewer): The default value for OpenAITransport is set to AppServer, which is inconsistent with the rest of the codebase where Native is typically the default (e.g., IntelligenceXClientOptio... [https://github.com/EvotecIT/IntelligenceX/pull/6#discussion_r2737309632]

### PR #5 fix(reviewer): add PR fetch + safe event parsing
- [ ] (inline, copilot-pull-request-reviewer): This foreach loop immediately [maps its iteration variable to another variable](1) - consider mapping the sequence explicitly using '.Select(...)'. [https://github.com/EvotecIT/IntelligenceX/pull/5#discussion_r2737285888]

### PR #4 fix(reviewer): input-only workflow runs
- [ ] (inline, copilot-pull-request-reviewer): The StringBuilder type is imported but never used in this file. Remove this unused import to keep the code clean. ```suggestion ``` [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281359]
- [ ] (inline, copilot-pull-request-reviewer): The error message should specify which inputs are expected. Since the code accepts multiple alternative input names (repo/repository for repo, and pr_number/pull_request/number for... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281396]
- [ ] (inline, copilot-pull-request-reviewer): Writing sensitive authentication data to disk without validation of the content could pose a security risk. Consider validating that the decoded content is valid JSON before writin... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281421]
- [ ] (inline, copilot-pull-request-reviewer): This GetInput method duplicates the functionality of the existing GetInput method in ReviewSettings.cs (line 306). Consider reusing the existing method by making it internal instea... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281445]
- [ ] (inline, copilot-pull-request-reviewer): The method GitHubEventParser.TryParsePullRequest does not exist in the codebase. The existing method is GitHubEventParser.ParsePullRequest which throws exceptions instead of return... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281476]
- [ ] (inline, copilot-pull-request-reviewer): The method GitHubClient.GetPullRequestAsync does not exist in the GitHubClient class. You need to implement this method to fetch pull request details from the GitHub API, including... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281499]
- [ ] (inline, copilot-pull-request-reviewer): This SplitRepo method is a duplicate of the private SplitRepo method already present in GitHubEventParser (lines 36-42). To avoid code duplication and maintain a single source of t... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281523]
- [ ] (inline, copilot-pull-request-reviewer): The null-forgiving operator is unnecessary here since the condition on line 41 already ensures that repoName is not null or whitespace before reaching this line. Remove the exclama... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281547]
- [ ] (inline, copilot-pull-request-reviewer): The file write operation (File.WriteAllText) is not wrapped in a try-catch block. If the write fails due to permissions or IO errors, it will propagate an unhandled exception. Cons... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281566]
- [ ] (inline, copilot-pull-request-reviewer): This method reimplements integer parsing logic that is similar to ReviewSettings.ParsePositiveInt (line 340). Consider consolidating this logic to avoid duplication. The existing P... [https://github.com/EvotecIT/IntelligenceX/pull/4#discussion_r2737281579]

### PR #2 Add IntelligenceX review automation
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub> Fix reusable workflow path** The `uses` value repeats `.github/workflows/review-intelligencex... [https://github.com/EvotecIT/IntelligenceX/pull/2#discussion_r2736780278]
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub> Use GitHub expression syntax for secrets** The secrets are written as `${ secrets.GITHUB_TOKE... [https://github.com/EvotecIT/IntelligenceX/pull/2#discussion_r2736780282]
- [ ] (inline, copilot-pull-request-reviewer): The reusable workflow reference in `uses:` looks malformed (the path is duplicated). This will cause the job to fail to resolve the workflow. It should reference the workflow file ... [https://github.com/EvotecIT/IntelligenceX/pull/2#discussion_r2736783713]
- [ ] (inline, copilot-pull-request-reviewer): Secret interpolation syntax is incorrect here: `${ secrets.X }` is not a valid GitHub Actions expression and will be passed literally. Use the `${{ secrets.X }}` expression syntax ... [https://github.com/EvotecIT/IntelligenceX/pull/2#discussion_r2736783738]
- [ ] (inline, copilot-pull-request-reviewer): The previous version explicitly granted `pull-requests: write`, `issues: write`, etc. This job now has no `permissions` block, so the default GITHUB_TOKEN permissions may be read-o... [https://github.com/EvotecIT/IntelligenceX/pull/2#discussion_r2736783747]
- [ ] (inline, copilot-pull-request-reviewer): This workflow file appears to use CRLF line endings (visible as `\r` in diffs/tool output), while other workflows in the repo use LF. Consider normalizing to LF to avoid noisy diff... [https://github.com/EvotecIT/IntelligenceX/pull/2#discussion_r2736783762]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `8f077e7f66` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/2#pullrequestreview-3716873486]

### PR #1 Add IntelligenceX review automation
- [ ] (inline, chatgpt-codex-connector): **<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub> Move reviewer settings into .intelligencex/reviewer.json** These review settings are saved in... [https://github.com/EvotecIT/IntelligenceX/pull/1#discussion_r2736592323]
- [ ] (review, chatgpt-codex-connector, COMMENTED): ### 💡 Codex Review Here are some automated review suggestions for this pull request. **Reviewed commit:** `430d1afe7c` <details> <summary>ℹ️ About Codex in GitHub</summary> <br/> ... [https://github.com/EvotecIT/IntelligenceX/pull/1#pullrequestreview-3716653453]


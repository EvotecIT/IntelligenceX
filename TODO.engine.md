# IntelligenceX Engine Roadmap

Status: Draft (needs priorities + owners)

## Now — Phase 1–2 (concrete)
- [ ] Add error classification enum + mapping (S)
- [x] Add diagnostic context in reviewer output (request id, retry count, provider) (S)
- [ ] Add retry policy options in config (backoff + max attempts) (M)
- [ ] Gate fail-open to transient errors only (S)
- [ ] Add connectivity preflight (DNS/TLS) with actionable errors (M)
- [ ] Add diff-range selection + default (current/pr-base/first-review) (M)
- [ ] Add include/exclude glob filters for files (S)
- [x] Add smart chunking (group related hunks) (M)
- [ ] Add review intent presets (security/perf/maintainability) (S)

## Phase 0 — Scope + success criteria
- [ ] Define "engine" scope (review pipeline, providers, context builder, formatter, thread triage).
- [ ] Capture success metrics (review latency, failure rate, reviewer usefulness score).
- [ ] Decide default review mode + model policy (safe defaults).

## Phase 1 — Reliability + diagnostics
- [ ] Classify error types (transient vs auth vs config vs provider) with explicit codes.
- [x] Add structured diagnostics block to reviewer output (request id, retry count, provider).
- [ ] Add connectivity preflight (DNS/TLS) with actionable error messages.
- [ ] Add configurable retry policy with exponential backoff + jitter.
- [ ] Add fail-open gating for transient errors only (explicit config).

## Phase 2 — Context quality
- [ ] Add diff-range strategy options (current/pr-base/first-review) with default.
- [ ] Add file filters (include/exclude globs, binary skip, generated skip).
- [x] Add smart chunking (keep related hunks together; avoid orphaned changes).
- [ ] Add language-aware hints (formatters and rule presets).
- [ ] Add "review intent" presets (security/perf/maintainability).

## Phase 3 — Review output + UX
- [ ] Add optional "reasoning level" label in header (low/medium/high) when provider supports it.
- [ ] Add optional usage/limits line near model header (opt-in; ChatGPT auth).
- [ ] Add structured findings schema for bots/automation (severity + file + line).
- [ ] Add summary stability (avoid noisy rewording across reruns).
- [x] Add “triage mode” that only checks open threads.

## Phase 4 — Thread triage + auto-resolve
- [ ] Keep thread triage in main review comment (configurable placement).
- [ ] Support "explain why not resolved" replies (optional).
- [ ] Add diff-based auto-resolve checks (explicit evidence required).
- [ ] Add per-bot policies (auto-resolve only for our bot by default).
- [ ] Add PR comment summarizing what was auto-resolved.

## Phase 5 — Provider abstraction
- [ ] Centralize provider contracts (capabilities, limits, streaming, auth).
- [ ] Add provider capability flags (usage API, reasoning level, streaming).
- [ ] Add opt-in provider fallback (e.g., OpenAI → Copilot).
- [ ] Add provider health checks and circuit breaker.

## Phase 5.5 — Code host support (Azure DevOps Services)
- [ ] Define code-host interface (PR metadata, files, diff, comments, threads).
- [ ] Add ADO auth options (PAT, System.AccessToken) + env var mapping.
- [ ] Phase 1: summary-only PR comments (no inline) using ADO REST APIs.
- [ ] Phase 2: inline comments with iteration + line mapping support.
- [ ] Phase 3: thread triage + auto-resolve via thread status updates.
- [ ] Add CLI flags/config: `provider=azure`, `azureOrg`, `azureProject`, `azureRepo`, `azureBaseUrl`, `azureTokenEnv`.
- [ ] Add ADO pipeline templates + docs (onboarding, permissions, secrets).

## Phase 6 — Performance + cost
- [ ] Add response streaming where supported (show partial progress).
- [ ] Add cache for context artifacts (diff, file lists, PR metadata).
- [ ] Add concurrency controls to avoid API throttling.
- [ ] Add token budgeting per file/group with hard caps.
- [ ] Add optional "budget exceeded" summary behavior.

## Phase 7 — Security + trust
- [ ] Redact sensitive data before prompt (secrets, tokens, private keys).
- [ ] Add "untrusted PR" guardrails (no secret access, no write actions).
- [ ] Add workflow integrity check (block self-modifying workflow runs).
- [ ] Add audit logging for secrets usage.

## Phase 8 — Testing + validation
- [ ] Add deterministic test harness with recorded provider responses.
- [ ] Add golden-file tests for formatter output stability.
- [ ] Add smoke tests for thread triage + auto-resolve.
- [ ] Add integration test for usage/limits display.

## Phase 9 — Developer experience
- [ ] Provide local "engine replay" CLI (load PR snapshot + run offline).
- [ ] Provide structured JSON output mode for integrations.
- [x] Add config validator with helpful errors + schema links.

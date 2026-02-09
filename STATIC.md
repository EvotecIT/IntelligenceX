# Static Analysis Policy (Roadmap)

This repo aims to provide a first-party static policy experience similar to Sonar (rule packs, quality gates, security hotspots),
but expressed in IntelligenceX terms and enforced via our own GitHub App + GitHub Actions.

For the deep-dive design notes (catalog schema, packs, config modes), see:
- `Docs/reviewer/static-analysis.md`

## Current State (Already Implemented)
- Policy + rule catalog:
  - Rules: `Analysis/Catalog/rules/**.json`
  - Overrides (our wording/tags layer): `Analysis/Catalog/overrides/**.json`
  - Packs (tiers): `Analysis/Packs/*.json` (example: `all-50` = “All Essentials (50)”)
- Repo config:
  - `.intelligencex/reviewer.json` is the source-of-truth for enabled packs, disabled rules, and severity overrides.
- CLI plumbing:
  - `intelligencex analyze validate-catalog`
  - `intelligencex analyze list-rules`
  - `intelligencex analyze run` (produces SARIF and/or IntelligenceX findings artifacts)
  - `intelligencex analyze gate` (turn findings into CI pass/fail, optionally scoped to changed files)
  - `intelligencex analyze hotspots sync-state --check` (stateful “security hotspots” review)
- CI integration:
  - `.github/workflows/review-intelligencex.yml` runs catalog validation + analysis + gate before running the reviewer.

## Goals
- First-party feel:
  - One configuration file (`.intelligencex/reviewer.json`) drives behavior.
  - Consistent, curated defaults (packs) with stable IDs.
  - Deterministic output and reproducible catalog updates.
- CI that can actually gate:
  - A small, stable “Essentials” pack that stays on.
  - Optional stricter tiers that teams can adopt as they mature.
  - Clear “what failed and why” (policy block + findings + rule outcomes).
- Sonar-like structure without Sonar UI:
  - Rule types: `bug`, `vulnerability`, `code-smell`, `security-hotspot`.
  - Security hotspots tracked over time (state file in repo, reviewable diffs).
- Self-hosted by default while private:
  - Jobs should run on org runners and not consume GitHub-hosted minutes.

## Non-Goals (For Now)
- Recreating SonarQube’s web UI or project dashboards.
- Full IDE experience (committed analyzer configs) by default.
  - We can support this later via an explicit export command and opt-in commits.

## Principles
- One source of truth: `.intelligencex/reviewer.json`.
- Determinism first: gates must not depend on AI availability.
- Safe-by-default automation:
  - no write tokens for untrusted PRs/forks
  - no silent config writes into user repos
- Minimal repo footprint: generate analyzer configs at runtime unless explicitly exported.

## Roadmap

### Phase 0: Baseline Policy (Keep It Boring)
- Keep `all-50` stable and quiet enough to run on every PR.
- Ensure the policy block is always present and shows enabled rules, packs, and outcomes.

### Phase 1: Catalog Quality + Expansion
- Expand PowerShell rules beyond `powershell-default` toward an actual `powershell-50` list.
- Expand internal maintainability rules beyond `IXLOC001`.
- Add consistent metadata:
  - `docs`, `tags`, `category`, default severity, and (where applicable) CWE/OWASP mappings.
- Provide “our style” overrides for titles/descriptions when upstream wording is weak.

### Phase 2: Quality Gates That Teams Can Live With
- Make `analyze gate` configurable per repo:
  - changed-files scoping (default on for PRs)
  - severity thresholds per pack/rule
  - “new issues only” baselining mode (for adopting strict packs gradually)

### Phase 3: Security Hotspots (Stateful Review)
- Define “hotspot” rule list (IDs/tags) and state file format.
- Workflow:
  - New hotspots fail or warn (configurable).
  - Existing acknowledged hotspots don’t block, but remain visible.

### Phase 4: AI Assist (No Code Changes Yet)
- Add an optional “AI assessment” stage that:
  - explains findings (context + risk)
  - proposes fixes
  - links to rule docs and repository conventions
- Output is a report artifact + optional PR comment (no patching).

### Phase 5: AI Codefix (Opt-In Auto PR)
- Add an explicit opt-in (label or config flag) to generate patches for a small allowlist of rules.
- Must run using the IntelligenceX GitHub App token (first-party identity).
- Guardrails:
  - only “safe” rule fixes by default
  - run tests after patch
  - open a separate PR per fix batch with clear provenance

## Decisions (Need Defaults)
- What blocks merges by default:
  - `vulnerability` only, or also `bug`, or all enabled rules at `warning+`.
- How hotspots affect gating:
  - block on “to review”, or warn only, or block only on explicit “accepted risk” policy violations.
- Baselines for adoption:
  - “new issues only” mode vs “pay down the debt” mode.

## GitHub App Permissions (Recommended)
- Read-only baseline:
  - `contents:read`, `pull_requests:write`, `issues:write`
- Needed for SARIF upload:
  - `security_events:write` (GitHub Code Scanning)
- Needed for auto-PR/codefix:
  - `contents:write`, `workflows:read`

## Open Issues / Hygiene
- If CodeQL “default setup” is enabled while “Code security” is disabled, CodeQL check runs can fail noisily.
  - Recommendation: either enable Code Security or fully disable CodeQL default setup until public.

# Static Policy Analysis Roadmap

This document tracks our end-to-end plan for first-party static policy analysis in IntelligenceX: catalog + packs, CI gates, triage workflows, issue tracking, AI assessment, and (optionally) auto-fix PRs.

Last updated: 2026-02-08

## Goals

- Provide Sonar-like static analysis policy, but in IntelligenceX style: repo-clean, config-driven, and GitHub-native.
- Make static outcomes first-class: clear gating rules, stable identifiers, and deterministic outputs.
- Run as our GitHub App identity (BYO App supported) with least privilege and an auditable action trail.
- Keep AI additive: AI helps triage and suggest fixes, but static policy remains deterministic and enforceable.

## Principles

- Single source of truth: `.intelligencex/reviewer.json`.
- Determinism first: static gates should not depend on AI availability.
- Security boundaries: never allow untrusted inputs to write/escape; do not run write tokens on untrusted PRs.
- Minimal repo footprint: generate analyzer configs at runtime unless explicitly exported.

## Current State (What We Have)

- [x] Catalog + packs (rules described as metadata files; packs enable sets of rules).
- [x] Pack includes resolution (packs can include packs; policy enablement reflects includes).
- [x] Rule typing: `bug`, `vulnerability`, `code-smell`, `security-hotspot`.
- [x] Overrides layer: `Analysis/Catalog/overrides/**` can adjust generated metadata without editing base rules.
- [x] Security hotspots.
- [x] Reviewer renders `### Security Hotspots 🔥` with persisted triage state in `.intelligencex/hotspots.json`.
- [x] Hotspot keys are bounded and safe (hashed fingerprints) and reviewer output is sanitized.
- [x] Hotspot state path is workspace-bounded in both CLI and reviewer (no arbitrary file reads in CI).
- [x] Workflow integration.
- [x] `.github/workflows/review-intelligencex.yml` runs analysis, uploads artifacts, runs reviewer.
- [x] GitHub App token is supported via `actions/create-github-app-token@v1` when secrets are present.
- [x] Static Analysis Policy block shows enabled rules, pack selection, and load/parse status.

Reference PR: merged PR #148 (Static analysis: security hotspots + state workflow).

## Roadmap

### Milestone 1: Static Gate (CI-blocking)

Outcome: a required check that can block merges without AI.

- [ ] Add `intelligencex analyze gate` to evaluate outcomes and exit non-zero on violations.
- [ ] Add gate configuration in `.intelligencex/reviewer.json` (thresholds by severity/type, allowlists, hotspots handling).
- [ ] Add GitHub Actions step that runs the gate and produces clear failure output.
- [ ] Decide default gate policy for the repo:
- [ ] Block on `vulnerability` at `warning+`.
- [ ] Optional: block on `bug` at `error+`.
- [ ] Optional: block on `security-hotspot` when `to-review` exists.

Definition of done:

- Gate produces stable output (counts + top offenders + deterministic ordering).
- Gate is deterministic from artifacts and config, no network calls required.
- Gate is safe on fork PRs (read-only tokens, no secrets used).

### Milestone 2: GitHub-native Surfacing (Annotations)

Outcome: “first-party feel” in PR UI.

- [ ] Option A: upload SARIF to GitHub Code Scanning (best UI).
- [ ] Option B: emit GitHub Actions annotations (lighter).
- [ ] Ensure findings are limited to changed files by default, with an opt-in for full repo scans.

Definition of done:

- PR shows annotations at the right lines for supported tools.
- No duplicate noise across multiple result sources.

### Milestone 3: Issue Targeting + Sync

Outcome: policy outcomes are trackable beyond a single PR.

- [ ] Add `intelligencex analyze issues sync` (creates/updates/closes issues).
- [ ] Define grouping key strategy:
- [ ] One issue per rule.
- [ ] One issue per rule + path group.
- [ ] One issue per hotspot key.
- [ ] Add a scheduled workflow on the default branch to sync issues (recommended).
- [ ] Add labels and templates (for example `static-analysis`, `security-hotspot`, `accepted-risk`).

Definition of done:

- Issue keys are stable across runs.
- Sync is idempotent and safe to re-run.
- Sync runs as GitHub App identity.

### Milestone 4: AI Assessment Stage (Advisory)

Outcome: AI operates on the deterministic static outputs and adds triage value.

- [ ] Add an “AI Assessment” stage that consumes policy + findings + hotspots.
- [ ] Produce consistent output structure (triage summary, false-positive suspicion, suggested remediation).
- [ ] Keep this non-blocking initially.

Definition of done:

- AI stage never changes gate outcomes.
- AI stage is bounded (max items, max tokens) and reproducible enough for audit.

### Milestone 5: Auto-fix PRs (Opt-in)

Outcome: safe automation that proposes fixes via PRs under GitHub App identity.

- [ ] Add `intelligencex autofix` runner.
- [ ] Define “autofixable rules” pack (small initial set).
- [ ] Implement deterministic fixes first (formatters/codemods), AI patching second.
- [ ] Add workflow triggers restricted to trusted contexts:
- [ ] `workflow_dispatch` only, or
- [ ] same-repo PRs only (no forks).

Definition of done:

- Creates PRs with clear scope, runs tests, links back to issues/findings.
- Never runs with write permissions on untrusted PRs.

## GitHub App Scope (Recommended)

Required:

- Pull requests: read/write
- Issues: read/write
- Contents: read (write only for auto-PR/autofix)

Optional:

- Checks: read/write (if we publish check-runs directly)
- Code scanning: write (if uploading SARIF)

## Open Questions (Need Decisions)

- What should block merges by default:
- `vulnerability` only
- `vulnerability + bug`
- “all enabled rules at warning+”
- How should hotspots affect gating:
- gate on `to-review` hotspots
- gate only on explicit `accepted-risk` policy violations
- do not gate, only report
- Issue grouping strategy (rule vs rule+path vs hotspot key).

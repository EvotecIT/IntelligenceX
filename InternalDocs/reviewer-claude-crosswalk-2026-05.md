# IX Reviewer vs Claude Review Crosswalk

Date: 2026-05-04

Reviewed examples:
- https://github.com/EvotecIT/PSPublishModule/pull/341
- https://github.com/EvotecIT/PSPublishModule/pull/331
- https://github.com/EvotecIT/HtmlForgeX/pull/173
- https://github.com/EvotecIT/HtmlForgeX/pull/167
- https://github.com/EvotecIT/HtmlForgeX.Email/pull/14
- https://github.com/EvotecIT/IntelligenceX/pull/1298

## What Claude Does Well

- It writes a review as a lifecycle artifact, not only a findings dump. Later comments compare new commits against prior findings and explicitly mark items as resolved, still outstanding, or newly introduced.
- It uses domain conventions from the target repo. The HtmlForgeX reviews judged changes against "no exposed HTML/CSS/JS" principles; the email review evaluated table/spacing behavior through email-client compatibility.
- It separates positives, blockers, lower-priority suggestions, and test coverage gaps. This makes "approve with notes" feel safe when no blockers remain.
- It gives fix direction close to the finding. Several comments include exact file/line context and a practical remediation shape.
- It can produce a clean approval-style summary when the PR is good, instead of staying silent.

## Where IX Already Overlaps

- IX already has structured blocker sections, sticky summaries, summary stability, review-thread history, external bot context toggles, CI context, static-analysis integration, and stale-thread auto-resolution.
- IX's static-analysis path is stronger than Claude's examples for deterministic policy: rule catalog, packs, gate behavior, SARIF/results ingestion, hotspots, duplication metrics, and baseline support.
- IX already supports Claude as a provider, plus OpenAI/Copilot/OpenAI-compatible fallback routes.

## Gaps Worth Porting

1. Commit-round lifecycle summary
   - Keep a normalized "prior findings ledger" by commit and render a short table: resolved, still open, newly introduced.
   - IX has enough history primitives to do this, but the output should be more explicit and less prompt-dependent.

2. Domain convention packs
   - Add repo-configurable review principles, for example "HtmlForgeX fluent API rules" or "email-client compatibility checks".
   - Feed them as structured context, not free-form issue-comment text.

3. Approval posture without auto-merge
   - Add an explicit `recommendation` field/section: `approve`, `needs-work`, `manual-review`, `skipped`.
   - Do not let the LLM directly approve by default. Treat auto-approval as a separate policy gate that can apply to any PR when enabled, but may be narrowed by labels, allowed authors, or file policy; require green required checks, no IX blockers, no unresolved review threads, and maintainer opt-in.

4. Low-cost bot PR handling
   - Dependabot PRs are usually better handled by tests, dependency metadata, advisory checks, and static analysis than by full LLM review.
   - The reviewer should skip the LLM step by default but keep the workflow check alive, with an opt-in label for full review.

5. Whole-repository quality posture
   - GitHub code scanning quality views are repository-level, not only PR-level.
   - IX analysis can already run over the whole checkout. A scheduled job should publish SARIF/findings and use baselines/new-only gates for adoption.

## Implemented In This Change

- Dependabot author skip moved into the reviewer before provider auth.
- Default skipped authors: `dependabot[bot]`, `app/dependabot`.
- Default force-review label: `needs-ai-review`.
- The wrapper workflow now runs for same-repo Dependabot PRs instead of skipping the entire job; the reviewer exits early without LLM/auth unless forced.
- Static-analysis documentation now calls out whole-repository quality runs.
- Sticky review comments now carry a hidden `intelligencex:history:v1` base64url JSON marker so prior IX rounds survive sticky comment updates.
- The hidden history marker and expanded artifacts now track recommendation, positive highlights, risk notes, follow-ups, and current-head open/resolved blocker state.
- The visible history block now renders latest review posture and blocker lifecycle as compact tables while preserving prior-head findings as prompt context only.

## Recommended Next Features

1. Add a deterministic review recommendation block and machine-readable metadata.
2. Add first-class domain convention packs so the positive/risk highlights can be evaluated against repo-specific rules.
3. Add repo convention packs as first-class config.
4. Add an opt-in auto-approval command or workflow, policy-gated outside the reviewer comment path.
5. Add a scheduled repository-quality workflow template that runs `analyze run`, uploads SARIF, and compares against baselines.

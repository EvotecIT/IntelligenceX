# RFC: IX Triage Graph (PR + Issue De-duplication and Prioritization)

## Status

Draft (first implementation slice started in `intelligencex todo build-triage-index`).

## Problem

At high repository velocity, open PRs and issues grow faster than humans can triage.
This causes:
- duplicate work across PRs/issues,
- difficulty identifying the best candidate PR to merge,
- review churn from re-triaging similar items repeatedly,
- weak alignment checks against project vision and scope.

## Goal

Add a cross-work-item triage layer on top of IntelligenceX Reviewer that:
- indexes open PRs and issues,
- groups likely duplicates with confidence and evidence,
- ranks best PR candidates using explicit signals,
- supports a vision-guidance mode that flags likely out-of-scope changes.

## Non-Goals (V1)

- automatic merge decisions,
- auto-closing PRs/issues without human confirmation,
- replacing per-PR deep review performed by IntelligenceX Reviewer.

## Why This Fits IX

IntelligenceX already provides:
- structured merge-blocking review sections (`Todo List`, `Critical Issues`),
- reviewer thread triage and auto-resolve primitives,
- deterministic analysis gate/baseline concepts,
- related PR retrieval hooks in review context.

The triage graph should extend these strengths, not replace them.

## Architecture

1. Ingestion
- Source: GitHub GraphQL and REST (open PRs/issues, labels, comments, review states, CI states).
- Cadence: on-demand CLI, scheduled CI workflow, and optional webhook-triggered refresh.

2. Normalization
- Canonical work-item record:
  - `kind`, `number`, `title`, `body`, `labels`, `updatedAt`, `url`
  - PR-only signals: `mergeable`, `reviewDecision`, `changedFiles`, `additions`, `deletions`, `commitCount`
  - Issue/PR shared: comment counts, author, activity recency.
- Deterministic token normalization for explainable matching.

3. Duplicate Graph
- Deterministic edge score:
  - title token Jaccard
  - context token Jaccard
  - exact/near title canonicalization
- Cluster output:
  - cluster id
  - member item ids
  - canonical representative
  - confidence score
  - reason string

4. Best PR Ranking
- Score dimensions:
  - mergeability and review decision,
  - change size and churn,
  - recency/activity,
  - blocking/ready labels.
- Output:
  - ranked candidate list,
  - score reasons,
  - duplicate-cluster representative handling.

5. Vision Guard (Assistive)
- Input: `VISION.md` (or configured document path).
- Output:
  - alignment notes with confidence and evidence,
  - recommend: `aligned`, `needs-human-review`, or `likely-out-of-scope`.
- No automatic rejection in V1.

## Delivery Plan

Phase 1 (Implemented)
- CLI command:
  - `intelligencex todo build-triage-index`
- Outputs:
  - JSON index (`intelligencex.triage-index.v1`)
  - Markdown summary (best PR candidates + duplicate clusters)
- Scope:
  - open PRs + open issues
  - deterministic duplicate clustering
  - assistive PR ranking

Phase 2 (Implemented)
- Pagination and larger-scale ingestion (up to configured `--max-prs` / `--max-issues`).
- CI status-check rollup signal in PR scoring.
- Scheduled workflow template:
  - `IntelligenceX.Cli/Templates/triage-index-scheduled.yml`

Phase 3 (Initial slice implemented)
- `intelligencex todo vision-check` for assistive scope alignment using `VISION.md`.
- JSON + markdown outputs for maintainer triage loops.

Phase 4 (Next)
- Semantic dedupe layer (embeddings) behind a feature flag.
- Stronger vision policy parser with explicit acceptance/rejection guidance.
- Optional auto-comment modes for PR threads (guarded by maintainer opts).

## Guardrails

- Never auto-merge.
- Never auto-close without explicit maintainer opt-in.
- Keep duplicate scoring explainable in output.
- Use reviewer output as evidence, not as autonomous policy.

## Success Metrics

- duplicate cluster precision on maintainer validation,
- time-to-first-triage reduction,
- reduction in redundant PR review cycles,
- improved merge throughput for top-ranked PRs.

## Risks

- false positives in dedupe on short titles,
- ranking bias toward small PRs,
- stale data if ingestion cadence is low.

Mitigation:
- expose threshold tuning,
- include score reasons for every ranking,
- keep human-in-the-loop as the decision authority.

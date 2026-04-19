# RFC Follow-Up: Review History Snapshot and Copilot Model Orchestration

## Status

Draft follow-up to `ix-review-swarm.md`.

This document narrows the next implementation slices so IntelligenceX Reviewer can:
- understand repeat review rounds in a structured way,
- support multi-provider and multi-model review fan-out,
- and validate modern GitHub Copilot CLI launch/auth behavior before depending on it in reviewer workflows.

## Why a follow-up RFC is needed

The existing swarm RFC correctly identifies the missing orchestration layer, but there are now three concrete realities in the codebase:
- reviewer config already exposes `review.swarm.*`,
- reviewer runtime now consumes those settings for diagnostic-only shadow sub-reviewer execution,
- and rerun awareness now has a bounded finding-first `ReviewHistorySnapshot` path for IX-owned summaries and review threads.

There is also a transport risk:
- the current Copilot integration launches the CLI as an embedded server process,
- while the current Copilot CLI help surface emphasizes `--acp` and the current GitHub CLI (`gh copilot`) is a wrapper entrypoint whose installer/bootstrap behavior is not guaranteed on every runner.

That means the next work should not jump straight to "publish swarm comments." The next work should establish stable building blocks first.

## Current baseline

Today the reviewer can already:
- reuse the previous sticky summary for the same head SHA,
- build a normalized review-history snapshot from IX-owned summary rounds and review-thread state,
- write bounded JSON/Markdown review-history artifacts when `review.history.artifacts` is enabled,
- include bounded external Claude/Copilot bot summary excerpts as supporting context when explicitly enabled,
- include issue comments, review comments, and review threads in prompt context,
- include CI/check summary and bounded failed-run evidence,
- run one provider with optional sequential fallback,
- run opt-in shadow sub-reviewers diagnostically without changing the public PR comment,
- execute shadow sub-reviewer lanes with bounded concurrency from `review.swarm.maxParallel`,
- expose history, Copilot launcher, and swarm-shadow settings through the managed workflow wrappers/templates for manual rollout experiments,
- run Copilot via the current embedded CLI transport.

Today the reviewer cannot yet:
- consume external bot summaries as trusted structured history sources,
- aggregate multiple provider/model reviewer outputs into one final swarm result,
- publish an aggregated swarm result as the single IX review comment,
- rely on `gh copilot` as a validated reviewer launcher path.

## Problem 1: Prior review context is prose-first, not state-first

`summaryStability` helps reduce noisy rewrites, but it does not answer higher-value questions such as:
- Which blocker first appeared in round 2?
- Which blocker was resolved by the latest commit?
- Which prior blocker is still open but now stale/outdated?
- Which repeated finding is actually the same underlying issue with new wording?

This is why a Claude-style "round history" summary can feel smarter on repeat passes. It is not merely rereading comments; it is presenting a compact state transition model.

## Proposal 1: ReviewHistorySnapshot

Introduce a shared normalized history object built before prompt generation.

Suggested shape:

```json
{
  "history": {
    "currentHeadSha": "abc1234",
    "rounds": [
      {
        "round": 1,
        "reviewedSha": "def5678",
        "source": "intelligencex",
        "summaryId": 123456789,
        "findings": [
          {
            "fingerprint": "basicelement-tr-heuristic-docs",
            "severity": "low",
            "section": "Todo List",
            "title": "BasicElement heuristic undocumented",
            "status": "resolved",
            "resolvedBySha": "abc1234",
            "evidence": "XML docs added at BasicElement.cs:82-83"
          }
        ]
      }
    ],
    "openFindings": [],
    "resolvedSinceLastRound": []
  }
}
```

Normative rules:
- findings are keyed by stable fingerprints, not raw prose
- status is computed from current summary markers, thread state, and optional diff evidence
- "outdated" is not treated as "resolved" unless explicit evidence or resolution exists
- external bot summaries are opt-in sources, never default sources
- prompt context should prefer compact normalized lines over raw historical markdown

## Suggested config for review history

```json
{
  "review": {
    "history": {
      "enabled": true,
      "includeIxSummaryHistory": true,
      "includeReviewThreads": true,
      "includeExternalBotSummaries": false,
      "externalBotLogins": ["copilot-pull-request-reviewer", "claude"],
      "maxRounds": 6,
      "maxFindingsPerRound": 20,
      "emitResolvedMatrix": true
    }
  }
}
```

Recommended V1 behavior:
- default off for external bot summaries
- default on for IX-owned sticky summary history when `summaryStability` is enabled
- keep history loading bounded by rounds and findings
- emit history as prompt input and optional internal artifacts before changing public comment shape

## Problem 2: Swarm settings are scalar-first, not reviewer-first

Current `review.swarm.reviewers` is a list of role ids. That is enough for role selection, but not enough for mixed-model orchestration.

It cannot express:
- correctness on `gpt-5.4`
- tests on Copilot `gpt-5.2`
- security on Claude
- aggregation on a separate model/provider

## Proposal 2: Reviewer object config

Keep the current string list as a compatibility alias, but add an object form for explicit reviewer definitions.

Suggested shape:

```json
{
  "review": {
    "swarm": {
      "enabled": true,
      "shadowMode": true,
      "maxParallel": 4,
      "reviewers": [
        {
          "id": "correctness",
          "provider": "openai",
          "model": "gpt-5.4",
          "reasoningEffort": "high"
        },
        {
          "id": "tests",
          "provider": "copilot",
          "model": "gpt-5.2"
        },
        {
          "id": "security",
          "provider": "claude",
          "model": "claude-opus-4-1"
        }
      ],
      "aggregator": {
        "provider": "openai",
        "model": "gpt-5.4",
        "reasoningEffort": "high"
      }
    }
  }
}
```

Compatibility rules:
- `reviewers: ["correctness", "tests"]` expands using repo defaults
- object form overrides defaults per reviewer
- aggregator defaults to main review provider/model when omitted
- sub-reviewers remain read-only and never post directly to GitHub

## Problem 3: Copilot launch path needs explicit validation

The current reviewer transport launches Copilot as a local CLI process. That remains useful, but the surrounding ecosystem changed:
- `gh copilot` can launch Copilot CLI when the wrapper can already find or provision it,
- Copilot CLI model selection is explicit in the public CLI surface,
- and the CLI help surface now centers `--acp`.

We should not assume that:
- `copilot` on PATH,
- `gh copilot`,
- and the reviewer's embedded transport

all represent the same supported server protocol.

## Proposal 3: Copilot launcher modes

Add an explicit launcher mode before changing default behavior.

Suggested shape:

```json
{
  "review": {
    "provider": "copilot"
  },
  "copilot": {
    "transport": "cli",
    "launcher": "auto",
    "cliPath": "copilot"
  }
}
```

Suggested semantics:
- `binary`: execute `copilot` directly
- `gh`: execute `gh copilot --`
- `auto`: use the direct binary when present, otherwise choose `gh copilot --` only after a wrapper capability probe succeeds

Validation requirements before enabling `gh` by default:
- confirm server/protocol compatibility for reviewer transport mode
- confirm model selection works in reviewer mode
- confirm auth works without API keys using Copilot subscription on supported runners
- confirm failure modes are diagnosable and fail-open behavior remains clear

## Recommended rollout order

### Phase 0: Reality-check and guardrails

- add internal validation notes for current Copilot CLI protocol expectations
- add a reviewer startup diagnostic when Copilot CLI/server capabilities look incompatible
- avoid changing public docs to imply swarm runtime is already active

### Phase 1: ReviewHistorySnapshot shadow mode

- build normalized IX summary/thread history
- emit internal artifact only
- feed compact history block into prompt
- keep public comment format unchanged except optional "resolved since last round" experiment behind a flag

### Phase 2: Swarm shadow execution

- shared context builder
- role-specific sub-reviewers (initial diagnostic execution now present)
- structured result contract
- aggregator pass (initial diagnostic execution now present)
- metrics/artifacts only

### Phase 3: Per-reviewer provider/model selection

- object-form reviewer config
- aggregator config object
- provider/model/reasoning overrides per role
- fallback semantics per role and for aggregator

### Phase 4: Public swarm output

- publish one aggregated IX review comment
- keep current merge-blocker sections
- keep thread auto-resolve post-aggregation
- compare against single-review baseline before flipping defaults anywhere

## Acceptance criteria

Review history slice is ready when:
- repeated reviewer runs can distinguish open vs resolved prior findings
- identical findings across rounds collapse to one fingerprint
- resolved matrix output can be generated without rereading raw prior markdown in the prompt

Swarm shadow slice is ready when:
- multiple reviewer roles run from one shared context
- aggregator emits one deterministic structured result
- latency/cost metrics are recorded
- no additional public PR noise is introduced

Current status:
- external bot summaries can be included as bounded supporting excerpts only when `review.history.includeExternalBotSummaries` is enabled
- sub-reviewer diagnostic execution is present behind `review.swarm.enabled` + `review.swarm.shadowMode`
- sub-reviewer lanes honor `review.swarm.maxParallel` while preserving deterministic result ordering for aggregation
- aggregator diagnostic execution is present and remains non-public
- bounded review-history JSON/Markdown artifacts are written under `artifacts/reviewer/history/` when `review.history.artifacts` is enabled
- bounded JSON/Markdown comparison artifacts plus append-only JSONL metrics are written under `artifacts/reviewer/swarm-shadow/` when `review.swarm.metrics` is enabled
- managed workflow wrappers/templates pass through history, Copilot model/launcher, and swarm-shadow overrides to the reusable workflow
- `copilot.launcher=auto` now probes whether `gh copilot -- --version` can actually launch the CLI before selecting the wrapper path
- public output still comes from the single-review path
- deeper longitudinal history analysis across uploaded workflow artifacts remains pending

Copilot launcher slice is ready when:
- reviewer can intentionally select `binary` or `gh`
- `auto` behavior is deterministic and logged
- subscription auth works without API keys on supported environments
- unsupported or drifting protocol modes fail with actionable diagnostics

## Suggested first implementation tasks

1. Add `ReviewHistorySettings` + schema/env/config plumbing.
2. Build `ReviewHistorySnapshot` from IX sticky summary + review threads.
3. Replace `previousSummary` string-only prompt injection with a history section builder.
4. Add internal artifact output for history normalization.
5. Add Copilot launcher capability probe and diagnostics.
6. Continue swarm executor shadow work by adding longitudinal rollout metrics.

## Non-goals for the first slice

- no public multi-comment swarm output
- no automatic use of external bot summaries by default
- no direct assumption that `gh copilot` is a drop-in reviewer transport until validated
- no autonomous fix engine behavior

# RFC: IX Reviewer Swarm Mode (Opt-In Multi-Reviewer First-Pass Coverage)

## Status

Draft.

Proposed as an opt-in extension to IntelligenceX Reviewer, not a default behavior change.

Current implementation reality as of 2026-04-17:
- `review.swarm.*` config/schema/workflow plumbing exists, including managed wrapper/template pass-through for manual rollout overrides.
- reviewer runtime still uses the single primary provider path for the public comment, but opt-in shadow mode can now execute selected read-only sub-reviewer lanes with bounded `maxParallel` concurrency and a diagnostic aggregator after the primary review succeeds.
- shadow rollout artifacts now include bounded JSON/Markdown comparison output and append-only JSONL metrics under `artifacts/reviewer/swarm-shadow/` when `review.swarm.metrics` is enabled.
- CI/check-aware reviewer context has landed in the current reviewer runtime and should be treated as baseline capability, not a future-only swarm dependency.
- structured review history now builds a bounded cross-round ledger from IX-owned sticky summaries and review-thread state, with optional bounded artifacts under `artifacts/reviewer/history/`; external bot summaries can be included as opt-in supporting excerpts but are not treated as trusted IX blocker state.

Follow-up detail for phased implementation lives in `InternalDocs/rfcs/ix-review-history-copilot-orchestration.md`.

## Problem

The current IntelligenceX reviewer is a strong single-pipeline reviewer:
- it builds one review context,
- makes one primary provider call,
- optionally falls back to another provider,
- optionally includes thread context and auto-resolve triage,
- optionally incorporates static-analysis results before posting the final summary.

This works well, but the practical PR loop still often looks like:
- reviewer finds a first set of blockers,
- author fixes them,
- next review finds a different class of blockers,
- repeat until merge-ready.

That loop is not necessarily a bug in the current reviewer. It is a consequence of asking one review pass to notice multiple issue classes at once under bounded context and bounded reasoning time.

## Goal

Introduce an opt-in "swarm mode" for IntelligenceX Reviewer that:
- runs multiple specialized read-only review passes in parallel against the same diff/context,
- aggregates them into one final IntelligenceX review comment,
- allows the reviewer to consume GitHub check/test failure context when available,
- improves first-pass blocker recall,
- reduces repeat review churn across correctness, security, reliability, and coverage gaps,
- preserves the existing public review contract (`Todo List`, `Critical Issues`, `Other Issues`, optional triage sections).

## Non-Goals (V1)

- guaranteeing that one review run always makes a PR merge-ready,
- autonomous code changes or auto-fixes,
- posting multiple public bot comments per PR update,
- replacing deterministic static analysis gates,
- changing the default reviewer mode for existing repositories.

Important clarification:

Swarm mode should improve first-pass coverage, but it will not eliminate all follow-up review loops. New fixes can introduce new defects, and some issues only become visible after the first blocker set is addressed.

Separate but related recommendation:

GitHub check/test awareness should become a base reviewer capability for both single-review and swarm-review modes. It should not be swarm-only.

## Optionality Principle

Everything proposed in this RFC must be explicitly enableable and disableable.

That applies at all levels:
- repository level
- workflow-input override level
- config-file policy level
- feature-subcomponent level

No repository should receive swarm review or CI-aware review context unless maintainers opt in.

Recommended default stance:
- existing reviewer behavior remains the default
- CI-aware reviewer context defaults to off
- swarm mode defaults to off
- failure-snippet capture defaults to off or `auto`, never forced on globally

This is both a product decision and a trust decision:
- some teams want deeper review context,
- some teams want the lowest-cost, fastest reviewer,
- some teams do not want workflow/test failures fed into model prompts,
- some teams only want deterministic static analysis plus diff review.

## Why This Fits IX Reviewer

IntelligenceX already has pieces that make this a natural extension rather than a separate product:
- static analysis runs before review in the workflow,
- sticky summaries and `summaryStability` reduce noisy rewrites,
- thread context and triage already help the model understand prior review history,
- auto-resolve requires evidence before closing bot threads,
- structured findings can already be emitted for automation,
- workflow-vs-JSON ownership boundaries are already established.

The missing piece is not "more review infrastructure." The missing piece is a multi-reviewer orchestration layer plus a deterministic aggregator contract.

There is also a second missing piece:
- structured cross-round review history inside the reviewer itself, so the reviewer can reason over previously reported blockers and verified resolutions instead of only reusing prior prose.

## Current Reviewer Baseline

Today, the reviewer effectively behaves like:
1. resolve PR context and changed files,
2. optionally load analysis results and thread context,
3. generate one prompt,
4. run one provider call,
5. parse/format one final review output,
6. optionally triage/auto-resolve existing threads.

That is the correct baseline to preserve for non-swarm runs.

## Proposed Design

### 1) Shared Context Builder

Build review context once per run:
- PR metadata,
- diff range,
- filtered changed files,
- CI/check snapshot,
- static-analysis results,
- selected issue/review/thread context,
- previous summary when `summaryStability` is enabled.

This shared context should be immutable for all sub-reviewers in the same run.

Why:
- keeps reviewer comparisons fair,
- reduces duplicated GitHub/API/file-loading work,
- ensures disagreements are due to review perspective, not different evidence.

### 1.1) CI / Test Awareness as Shared Context

This capability is now largely present in the single-review path and should be preserved as shared infrastructure for future swarm execution, not reimplemented separately.

Reviewer context should optionally include bounded GitHub Actions and PR-check information.

Recommended levels:

1. Check summary
- passed / failed / pending counts
- required-check readiness signal
- failing check names and workflow names

2. Failed-run metadata
- failed run ids
- workflow names
- run URLs
- status / conclusion

3. Bounded failure evidence
- selected error snippets from failed jobs or test output
- test names, failing assertions, stack-frame excerpts, compile-error excerpts
- no raw full-log dump

This capability should be available to:
- the current single-review path
- swarm sub-reviewers
- the swarm aggregator

It should be implemented once and shared.

### 1.2) Reuse Existing IX Check/Run Plumbing

This should continue reusing the logic already present in PR-watch flows rather than building a second drifting GitHub checks implementation.

Existing IX CLI logic already collects:
- PR check summaries via `gh pr checks`
- failed workflow runs for the current head SHA
- actionable-vs-retry-vs-stop semantics for failed checks

Reviewer should either:
- consume a shared snapshot object/factory extracted from PR-watch logic, or
- call a shared GitHub checks helper that both PR-watch and reviewer use.

Do not keep two separate implementations of:
- check-summary parsing
- failed-run discovery
- run classification
- infra-vs-actionable heuristics

### 2) Specialized Reviewer Roles

Swarm mode should fan out to a small fixed set of specialized reviewers.

Recommended default roles:
- `correctness`: behavioral regressions, contract drift, null/error path defects, broken assumptions
- `security`: secrets exposure, authz/authn regressions, unsafe trust boundaries, data leakage
- `reliability`: brittleness, maintainability hazards likely to cause future defects, race/error handling gaps
- `tests`: missing regression coverage, contract-test gaps, missing harness updates

Optional later roles:
- `performance`
- `api-compat`
- `workflow-policy`

V1 should not try to run too many roles. Four is a reasonable default ceiling.

### 3) Parallel Read-Only Reviewer Execution

For each enabled role:
- reuse the shared context,
- inject a role-specific prompt contract,
- request only role-relevant findings,
- return a structured reviewer result rather than final Markdown.

Suggested structured result shape:
- `reviewerId`
- `status`
- `findings[]`
- `summary`
- `confidenceNotes`
- `contextLimits`

Each finding should include:
- `category`
- `severity`
- `title`
- `body`
- `file`
- `line`
- `suggestedSection`
- `evidence`
- `fingerprint`

Sub-reviewers must remain read-only. They do not comment on GitHub directly and do not resolve threads directly.

### 4) Aggregator / Judge Pass

After sub-reviewers finish, run one aggregator pass that:
- merges all candidate findings,
- de-duplicates near-identical items,
- collapses cross-role duplicates into one final item,
- ranks findings by merge relevance,
- maps findings into the existing IX output contract,
- decides which items should become inline comments,
- records discarded findings for metrics/debugging, but does not publish them by default.

The aggregator is the only component allowed to produce the public review comment.

This is the most important design rule in the RFC:

- many internal reviewers,
- one public reviewer voice.

Without a single aggregator, swarm mode would increase noise instead of reducing it.

### 5) Public Output Contract

The public comment should remain compatible with current IX expectations:
- `Todo List ✅`
- `Critical Issues ⚠️`
- `Other Issues 🧯`
- optional `Next Steps 🚀`
- optional thread-triage section

Sub-reviewer role labels may appear as small evidence tags inside findings, but the top-level comment should still look like normal IntelligenceX Reviewer output.

Example:
- `Correctness + Tests: API now returns nullable result but no regression coverage proves callers handle null.`

Not:
- one heading per sub-reviewer,
- four separate comments,
- reviewer-to-reviewer debate in the PR thread.

### 6) Thread-Triage Integration

Swarm mode should consume prior review context, but it must not multiply thread churn.

Rules:
- only the aggregator decides whether a prior issue is still blocked,
- auto-resolve logic remains post-aggregation,
- evidence requirements for thread resolution remain unchanged,
- stale/bot-only thread handling continues to operate once the final no-blockers conclusion is known.

### 7) Static Analysis Relationship

Static analysis should stay outside the swarm and run before it.

Reason:
- static analysis is deterministic and should act as evidence, not as another "opinionated reviewer,"
- swarm reviewers should consume analysis findings/policy as input,
- the aggregator should merge deterministic analyzer findings with model findings into one final blocker picture.

In short:
- analysis is a signal source,
- CI/test failures are another signal source,
- swarm reviewers are reasoning sources,
- the aggregator is the output source.

### 8) CI / Test Failure Semantics

The reviewer should not treat every failed check as a review finding.

Instead, it should classify failed checks into buckets:
- actionable code failure
- likely test regression
- compile/build failure
- infra-blocked / unavailable
- unknown

Normative behavior:
- actionable test/build failures should be available as review evidence,
- infra-blocked failures should be called out as environment blockers, not code findings,
- the reviewer should avoid duplicating a failing-test message as a separate blocker unless it adds diagnosis or a fix path,
- the reviewer should preferentially connect failing tests to touched files or changed contracts when evidence supports that connection,
- if failing tests clearly explain the regression, the reviewer may reduce redundant speculative findings.

This is important for trust:
- maintainers should feel the reviewer understands the current CI state,
- but the reviewer should not simply paraphrase logs back at the user.

## Proposed Config Shape

Suggested `reviewer.json` shape:

```json
{
  "review": {
    "mode": "hybrid",
    "length": "long",
    "ciContext": {
      "enabled": false,
      "includeCheckSummary": true,
      "includeFailedRuns": true,
      "includeFailureSnippets": "off",
      "maxFailedRuns": 3,
      "maxSnippetCharsPerRun": 4000,
      "classifyInfraFailures": true
    },
    "swarm": {
      "enabled": false,
      "shadowMode": false,
      "reviewers": ["correctness", "security", "reliability", "tests"],
      "maxParallel": 4,
      "publishSubreviews": false,
      "aggregatorModel": "gpt-5.4",
      "failOpenOnPartial": true,
      "metrics": true
    }
  }
}
```

Recommended V1 semantics:
- `ciContext.enabled`: enables reviewer-side PR check awareness
- `includeCheckSummary`: include status counts and failing check names
- `includeFailedRuns`: include failed workflow run metadata for the current head SHA
- `includeFailureSnippets`: `off|auto|always`; `auto` includes bounded snippets only for clearly actionable failures
- `maxFailedRuns`: cap failing-run context
- `maxSnippetCharsPerRun`: cap log/test snippet size
- `classifyInfraFailures`: separate runner/provider/outage style failures from actionable code failures
- `enabled`: turns on swarm orchestration
- `shadowMode`: run swarm internally, but publish the existing single-review path or store swarm output only for evaluation
- `reviewers`: explicit role list
- `maxParallel`: concurrency ceiling
- `publishSubreviews`: must default to `false`
- `aggregatorModel`: optional override for the final judge pass
- `failOpenOnPartial`: allow aggregator to proceed when one sub-reviewer fails
- `metrics`: record internal run metrics/artifacts for comparison

Workflow inputs should only expose a narrow override surface, for example:
- `ci_context_enabled`
- `ci_context_include_failure_snippets`
- `swarm_enabled`
- `swarm_shadow_mode`
- `swarm_reviewers`

Stable team policy should still live in `.intelligencex/reviewer.json`.

Recommended operator guidance:
- repo default lives in `reviewer.json`
- workflow inputs are temporary per-run overrides
- disabling via workflow input must always be able to override an enabled JSON policy for one-off debugging or cost control

Example temporary override cases:
- keep swarm enabled in repo policy, but disable it for a hotfix PR
- keep CI context disabled by default, but enable it for one failing PR
- enable CI summaries, but keep failure snippets off
- enable swarm, but only for `correctness` + `tests`

## Feature Toggle Matrix

The proposal should support independent toggles, not one giant switch.

Suggested matrix:

- `review.ciContext.enabled`
  turns reviewer-side CI/check awareness on or off
- `review.ciContext.includeCheckSummary`
  include only status counts and failing check names
- `review.ciContext.includeFailedRuns`
  include failed-run metadata for current SHA
- `review.ciContext.includeFailureSnippets`
  include bounded compile/test/log snippets
- `review.swarm.enabled`
  turns swarm orchestration on or off
- `review.swarm.shadowMode`
  runs swarm without publishing its output
- `review.swarm.reviewers`
  selects which reviewer roles participate
- `review.swarm.maxParallel`
  controls concurrency/cost

This separation matters because teams may want combinations like:
- CI-aware single reviewer, no swarm
- swarm reviewer, no CI snippets
- check-summary only, no run metadata
- correctness/tests swarm only, not security/performance

## Concrete Config Proposal

This section narrows the proposal to a specific V1 shape that matches current reviewer naming conventions.

### Proposed `review.ciContext` object

```json
{
  "review": {
    "ciContext": {
      "enabled": false,
      "includeCheckSummary": true,
      "includeFailedRuns": true,
      "includeFailureSnippets": "off",
      "maxFailedRuns": 3,
      "maxSnippetCharsPerRun": 4000,
      "classifyInfraFailures": true
    }
  }
}
```

Suggested schema shape:
- `enabled`: `boolean`
- `includeCheckSummary`: `boolean`
- `includeFailedRuns`: `boolean`
- `includeFailureSnippets`: `string`, enum `off|auto|always`
- `maxFailedRuns`: `integer`, minimum `0`
- `maxSnippetCharsPerRun`: `integer`, minimum `0`
- `classifyInfraFailures`: `boolean`

### Proposed `review.swarm` object

```json
{
  "review": {
    "swarm": {
      "enabled": false,
      "shadowMode": false,
      "reviewers": ["correctness", "security", "reliability", "tests"],
      "maxParallel": 4,
      "publishSubreviews": false,
      "aggregatorModel": "gpt-5.4",
      "failOpenOnPartial": true,
      "metrics": true
    }
  }
}
```

Suggested schema shape:
- `enabled`: `boolean`
- `shadowMode`: `boolean`
- `reviewers`: `array<string>`
- `maxParallel`: `integer`, minimum `1`
- `publishSubreviews`: `boolean`
- `aggregatorModel`: `string`
- `failOpenOnPartial`: `boolean`
- `metrics`: `boolean`

### Reviewer Role Enum Recommendation

Suggested allowed reviewer role values for V1:
- `correctness`
- `security`
- `reliability`
- `tests`

Keep the initial enum small. Extra roles should be added only after we have role-specific prompts, parser coverage, and comparison metrics.

## Proposed Workflow / Env Surface

The reviewer already follows the pattern:
- workflow input
- `INPUT_*` mapping
- `REVIEW_*` environment override
- `.intelligencex/reviewer.json` fallback

New proposal should follow the same pattern.

### Recommended workflow inputs

- `ci_context_enabled`
- `ci_context_include_check_summary`
- `ci_context_include_failed_runs`
- `ci_context_include_failure_snippets`
- `ci_context_max_failed_runs`
- `ci_context_max_snippet_chars_per_run`
- `ci_context_classify_infra_failures`
- `swarm_enabled`
- `swarm_shadow_mode`
- `swarm_reviewers`
- `swarm_max_parallel`
- `swarm_publish_subreviews`
- `swarm_aggregator_model`
- `swarm_fail_open_on_partial`
- `swarm_metrics`

### Recommended reviewer env aliases

- `REVIEW_CI_CONTEXT_ENABLED`
- `REVIEW_CI_CONTEXT_INCLUDE_CHECK_SUMMARY`
- `REVIEW_CI_CONTEXT_INCLUDE_FAILED_RUNS`
- `REVIEW_CI_CONTEXT_INCLUDE_FAILURE_SNIPPETS`
- `REVIEW_CI_CONTEXT_MAX_FAILED_RUNS`
- `REVIEW_CI_CONTEXT_MAX_SNIPPET_CHARS_PER_RUN`
- `REVIEW_CI_CONTEXT_CLASSIFY_INFRA_FAILURES`
- `REVIEW_SWARM_ENABLED`
- `REVIEW_SWARM_SHADOW_MODE`
- `REVIEW_SWARM_REVIEWERS`
- `REVIEW_SWARM_MAX_PARALLEL`
- `REVIEW_SWARM_PUBLISH_SUBREVIEWS`
- `REVIEW_SWARM_AGGREGATOR_MODEL`
- `REVIEW_SWARM_FAIL_OPEN_ON_PARTIAL`
- `REVIEW_SWARM_METRICS`

### Ownership boundary recommendation

Keep the existing repo convention:
- stable defaults in `.intelligencex/reviewer.json`
- temporary toggles in workflow `with:`
- runtime normalization in `ReviewSettings` / env parsing

Do not bury swarm or CI-awareness defaults directly inside workflow scripts without matching `reviewer.json` support.

## Modes

### Mode A: Disabled

Current reviewer behavior. No change.

Recommended future nuance:
- swarm disabled should still be allowed to use CI/test-aware review context when configured.

### Mode A1: CI-Aware Single Reviewer

Single-review path remains active, but CI/check context is enabled.

This should be the lowest-risk first rollout option for many repositories.

### Mode B: Shadow

Run swarm in parallel with the current single-review path, but do not expose swarm findings publicly by default.

Purpose:
- compare recall,
- compare noise,
- measure cost and latency,
- verify aggregator quality before rollout.

This should be the first production rollout mode.

### Mode C: Publish Aggregated Swarm Output

Replace the single-review generation path with:
- shared context,
- sub-reviewer fan-out,
- aggregator final comment.

Still one public comment.

### Mode D: Future Assist Loop

Out of scope for V1, but worth naming:
- swarm review identifies blockers,
- a separate assist/fix loop proposes or applies narrow fixes,
- verification reruns,
- final aggregator confirms residual blockers.

This mode is closer to "end to end in one check," but it is a different product surface than read-only swarm review.

## How This Reduces Repeat Review Churn

Swarm mode can reduce follow-up loops in four concrete ways:

1. Parallel specialization
- one role can stay focused on contracts/correctness,
- another can look for security/privacy,
- another can look for reliability/maintainability,
- another can focus on regression coverage.

2. Better first-pass blocker coverage
- issues that a single general-purpose pass might defer or miss are more likely to surface together.

3. Aggregated prioritization
- the final public comment can group related items into a coherent fix path instead of surfacing them piecemeal across reruns.

4. Stronger continuity across reruns
- prior summary, thread context, and finding fingerprints can help the reviewer avoid "rediscovering" the same blocker with different wording.

5. CI-aware prioritization
- when tests already fail, the reviewer can connect diff changes to those failures in the same pass,
- when checks are infra-blocked, the reviewer can avoid pretending the code is proven broken,
- when build/test output already points to the root cause, the reviewer can spend its reasoning budget on diagnosis and fix guidance instead of rediscovering the symptom.

## Important Limitation

Swarm mode will not make every PR one-pass merge-ready.

Reasons:
- fixes can introduce new issues,
- some defects are only visible after the first blocker is removed,
- limited diff context can still hide downstream interactions,
- large PRs can still exceed effective review bandwidth,
- deterministic gates may surface after model findings are fixed.

The correct product promise is:

"Swarm mode improves first-pass coverage and reduces repeat review churn."

Not:

"Swarm mode guarantees one review check always finishes the PR end to end."

## Implementation Strategy

### Phase 0: RFC Alignment

Decide:
- config shape,
- role list,
- aggregator output contract,
- artifact/metrics schema,
- workflow placement.

### Phase 1: Shared Context + Structured Sub-Reviewer Results

Add:
- shared review snapshot model,
- shared CI/check snapshot model,
- role-specific prompt builders,
- structured result parser,
- finding fingerprinting helper.

No GitHub output change yet.

### Phase 2: Shadow Mode

Run:
- current single-review path,
- internal swarm path.

Persist comparison artifacts:
- findings overlap,
- unique findings by path/severity,
- overlap between reviewer findings and failing-test evidence,
- latency,
- token/usage cost,
- false-positive review samples.

No public behavior change by default.

### Phase 3: Aggregated Publish Mode

Enable opt-in repositories to publish the aggregated swarm result as the single IX review comment.

Guardrails:
- one comment only,
- no per-role comment spam,
- fail-open behavior preserved,
- partial swarm failures tolerated when configured.

### Phase 4: Continuity Improvements

Add:
- cross-run finding fingerprints,
- "already surfaced in previous run" suppression,
- stronger mapping between fixed findings and unresolved threads.

This phase is where the churn reduction gets materially better.

## Concrete Next Steps

Recommended execution order:

1. Extract shared GitHub checks helpers
- move PR-check and failed-run collection into reusable code shared by PR-watch and reviewer
- keep parsing/classification logic in one place

2. Add `ReviewCiContextSettings`
- wire config loader
- wire environment loader
- add schema entries
- keep feature disabled by default

3. Add CI-aware single-review context
- inject check summary and failed-run metadata into the prompt when enabled
- do not add snippet capture yet
- verify comment quality on failing-test PRs

4. Add infra classification
- separate actionable failures from infra-blocked states
- ensure reviewer wording does not misclassify runner/provider outages as code defects

5. Add optional bounded snippet capture
- only after summary-level CI awareness proves useful
- start with compile/test assertion excerpts
- keep logs aggressively truncated/redacted

6. Add swarm config/settings plumbing
- schema
- config loader
- env parsing
- no publish behavior yet

7. Add structured sub-reviewer result contract
- role-specific prompts
- parser
- fingerprinting

8. Add shadow-mode aggregator
- run beside current single-review path
- emit artifacts only
- compare signal quality and cost

9. Add opt-in publish mode
- one public comment only
- preserve current output contract

## Suggested Improvement Pack (Post-V1)

Once base CI-awareness and shadow swarm are stable, the next worthwhile improvements are:

### Improvement A: Failure-to-Diff Mapping

When CI failures exist, attempt to map them to:
- touched files
- touched test files
- changed namespaces/types/methods
- known rule/contract ids

This should help the reviewer say:
- "these two failing tests likely tie to the nullable contract introduced in this file"

instead of:
- "tests are failing somewhere."

### Improvement B: Finding De-Dupe Across Runs

Persist finding fingerprints so the reviewer can distinguish:
- previously surfaced blocker
- newly introduced blocker
- blocker likely resolved by current diff

This is one of the biggest churn-reduction opportunities.

### Improvement C: Test-Aware Fix Guidance

When failing tests already identify the regression, the reviewer should bias toward:
- the most likely root cause
- the minimum validating fix path
- the tests or checks that should pass after the fix

This keeps the comment actionable without turning the reviewer into a repair bot.

### Improvement D: Per-Repo Presets

Offer opt-in reviewer presets such as:
- `ci-aware-light`
- `ci-aware-tests`
- `swarm-shadow`
- `swarm-correctness-tests`

These presets would help maintainers adopt advanced behavior without hand-tuning every field.

### Improvement E: Reviewer/Babysitter Handoff

Allow the reviewer to emit structured signals that PR-watch can consume directly, for example:
- `ciInfraBlocked`
- `likelyTestRegression`
- `needsMaintainerDecision`
- `repeatFindingFingerprint[]`

That would tighten the loop between deep review and operational PR babysitting.

## Workflow Recommendation

Keep the existing workflow structure:
- static analysis first,
- reviewer after analysis,
- final fail-open summary handling.

Do not add a separate required GitHub check for each sub-reviewer role.

Recommended workflow behavior:
- still expose one required reviewer check,
- run sub-reviewers inside that check,
- store internal artifacts under `artifacts/`,
- optionally upload them for maintainers during shadow rollout.

This preserves a clean branch-protection story.

For CI-aware review context:
- default to check summaries and failed-run metadata first,
- make log/test snippet capture opt-in or `auto`,
- redact/bound snippets aggressively,
- never stuff full workflow logs into the review prompt.

## Metrics

Swarm mode should be judged on outcomes, not novelty.

Primary metrics:
- reduction in repeat review cycles per PR,
- increase in first-pass blocker recall,
- reduction in newly surfaced blocker categories on second review,
- reduction in cases where reviewer output ignores already-failing tests/checks,
- false-positive rate compared with single-review baseline,
- added latency per review run,
- added token/provider cost per review run.

Suggested success threshold for rollout:
- materially better first-pass blocker recall,
- no major increase in false positives,
- acceptable latency/cost increase for opt-in repos.

## Risks

### 1) More noise instead of less

Risk:
- multiple reviewers surface overlapping or low-signal items.

Mitigation:
- strict aggregator dedupe,
- small role set,
- one public voice only.

### 2) Cost and latency inflation

Risk:
- four reviewer calls plus aggregation cost more than one pass.

Mitigation:
- opt-in only,
- shadow measurement first,
- allow per-repo role selection,
- allow smaller models for sub-reviewers and stronger model for aggregator if needed.

CI-aware review also adds cost if failure snippets are loaded, so snippet capture should stay bounded and selective.

### 3) Contradictory findings

Risk:
- sub-reviewers disagree on severity or necessity.

Mitigation:
- aggregator resolves contradictions,
- artifacts record disagreement for debugging,
- public comment shows only final merged guidance.

### 4) Swarm becomes pseudo-fix engine by accident

Risk:
- users assume swarm means autonomous end-to-end repair.

Mitigation:
- keep V1 explicitly review-only,
- document future assist/repair loop separately.

### 5) Reviewer gets noisy from CI logs

Risk:
- full workflow logs drown the actual diff review,
- unrelated infra noise gets misreported as code blockers,
- sensitive output accidentally enters the prompt.

Mitigation:
- start with summaries + failed-run metadata,
- add snippet capture only behind explicit config,
- classify infra failures separately,
- redact and truncate aggressively,
- prefer test/assertion/compile excerpts over raw log streams.

## Open Decisions

| Decision | Owner | Notes |
| --- | --- | --- |
| Default role set for V1 | IX Reviewer Maintainers | Proposed: correctness, security, reliability, tests |
| Whether shadow mode runs beside or instead of single review in early rollout | IX Reviewer Maintainers | Recommended: beside, for comparison |
| Whether CI/check awareness ships before swarm publish mode | IX Reviewer Maintainers | Recommended: yes, as a base reviewer capability |
| Default toggle values for new features | IX Reviewer Maintainers | Recommended: all off by default |
| Whether failed-run snippets should be summary-only in V1 | IX Reviewer Maintainers | Recommended: yes |
| Whether CI-aware single-review should be documented as a first-class preset | IX Reviewer Maintainers | Recommended: yes |
| Aggregator model selection policy | IX Reviewer Maintainers | Same model as review vs dedicated override |
| Finding fingerprint schema | Reviewer + CLI Maintainers | Needed for cross-run churn suppression |
| Artifact retention and visibility | Reviewer Maintainers | Internal only vs uploaded workflow artifact |
| Partial-failure semantics | Reviewer Maintainers | Recommended: configurable fail-open on sub-reviewer failure |
| Failed-run snippet source | Reviewer + CLI Maintainers | `gh pr checks` only vs Actions runs/jobs/log excerpts |

## Recommendation

Build this as:
- CI-aware reviewer context first,
- an opt-in swarm review mode,
- review-only in V1,
- one public IX comment,
- shadow-first rollout,
- metrics-driven promotion.

The design should explicitly aim to reduce repeat-review churn, but it should not promise one-pass completion. If we later want true "one check does it end to end," that should be a separate assist/repair RFC built on top of this swarm review foundation.

The opt-in rule should be non-negotiable:
- no silent enablement,
- no repo-wide behavior change without explicit maintainer choice,
- and no forced coupling where enabling one advanced feature automatically enables all the others.

## Recommended Immediate Follow-Up Deliverables

The next small, concrete deliverables after this RFC should be:

1. Schema/config PR
- add `review.ciContext.*`
- add `review.swarm.*`
- add docs examples with all defaults off

2. Shared GitHub checks helper PR
- extract reusable check-summary and failed-run collection from PR-watch code

3. CI-aware single-review PR
- prompt/context support for check summary + failed-run metadata only
- no swarm behavior yet

4. Shadow swarm prototype PR
- correctness + tests roles only
- artifact-only output
- no public comment replacement yet

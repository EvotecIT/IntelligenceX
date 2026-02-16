<INSTRUCTIONS>
# IntelligenceX.Chat Agent Playbook

This file defines how automated agents should operate in this repo.

## Core Rules
- Use `gh` CLI for PR, review, and CI workflows.
- Work in a dedicated worktree + branch per task.
- Do not use destructive git commands (`reset --hard`, `checkout --`).

## Worktree + Branch
1. Create a worktree for every task: `git worktree add -b <branch> <path> origin/main`
2. Make changes only in that worktree.
3. Keep branches focused to a single change set.

## PR Review and CI
1. Create PRs from the worktree branch.
2. Check CI: `gh pr checks <num> --repo EvotecIT/IntelligenceX.Chat`
3. Merge only when required checks pass and the PR is mergeable.
4. Merge with squash + delete branch:
   `gh pr merge <num> --repo EvotecIT/IntelligenceX.Chat --squash --delete-branch`

## Documentation Standards
- Prefer Markdown docs under `Docs/`.
- Keep architecture decisions explicit and versioned.
- Avoid nested bullets in `TODO.md`.

## Language-Neutral Chat Routing (Strict)
- Do not introduce hardcoded natural-language keyword gates for routing/safety (for example English-only trigger/deny words).
- Do not special-case compact follow-ups using literal phrases (for example "go ahead", "do it", "run it"); use structural/context signals instead.
- Use structured machine fields for action safety classification (`ix_action_selection.mutating`, `ix:action:v1` `mutating: true|false`) instead of lexical intent guessing.
- If message-text matching is unavoidable for provider compatibility, keep it structured-first and isolated from chat routing/confirmation decisions.
- Any change to action-selection, pending-action confirmation, or execution-contract logic must include regression tests that are language-agnostic.

## Build/Quality Standards (when code exists)
- Nullable enabled
- Warnings as errors
- XML docs for public APIs
- Aim for AOT-safe patterns where feasible

## Duplication / Layering
- Prefer adding reusable logic to the engine/tooling repos rather than duplicating it inside Chat.
- Tool implementations live in `EvotecIT/IntelligenceX.Tools` (and upstream engines like `ADPlayground`, `ComputerX`, `EventViewerX`).
- `IntelligenceX.Chat` should focus on host/service UX, streaming/progress, policy banners, response shaping, and docs.

### Layering Rules (Strict)
- `IntelligenceX.Chat`: orchestration + UX only. Do not add AD parsing, LDAP helpers, DN helpers, or event log parsing here.
- `IntelligenceX.Tools.*`: thin wrappers with stable schemas. Prefer composing existing engine APIs over implementing protocols directly.
- Engines (`TestimoX/ADPlayground`, `TestimoX/ComputerX`, `PSEventViewer/EventViewerX`): the source of truth for domain logic, parsing, and low-level access.

### Duplication Rules (Practical)
- Before adding a helper, search for it in the relevant engine first (e.g., DN helpers or `SearchResult` accessors in `ADPlayground`).
- If the logic is AD-specific (DN parsing, LDAP filter escaping, RootDSE reads, ranged member retrieval, LDAP diagnostics), it belongs in `ADPlayground` not in Tools or Chat.
- If the logic is tool-output-specific (JSON shaping, truncation rules, table envelopes), it belongs in `IntelligenceX.Tools` (or `Tools.Common` if shared).
- Avoid `TryGetString`/`TryGetInt32` style re-implementations when an engine already exposes typed accessors (e.g., `SearchResultExtensions.GetString/GetInt/GetFileTime` in `ADPlayground`).

### Canonical AD Engine
- The canonical AD engine we depend on is `TestimoX/ADPlayground` (project reference), not the standalone `ADPlayground` repo.

</INSTRUCTIONS>

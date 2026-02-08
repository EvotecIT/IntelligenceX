# TODO Backlog Sync (Bot Feedback)

Maintainers can aggregate explicit checklist items from IntelligenceX bot reviews into `TODO.md` under:

`## Review Feedback Backlog (Bots)`

## Sync

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX
```

Notes:
- Only explicit markdown task list items are imported (`- [ ] ...`, `- [x] ...`).
- Tasks are grouped by PR and kept in collapsible `<details>` blocks.
- Re-running the sync should be safe and should avoid noisy diffs.
- This is a repo-level backlog file (not “a TODO for a single PR”). Each PR gets its own collapsible block.
- Existing PR blocks are matched by PR number and updated in-place.
- Task items are merged by task text (case-insensitive) so manual checkbox state in `TODO.md` is preserved. If a bot rewords an item, it will appear as a new task.
- The sync does not delete tasks that disappeared from a PR review; remove them manually if they become stale/noise.

## Optional: create issues for unchecked items

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX --create-issues
```

Notes:
- Issue creation is opt-in. It creates issues only for unchecked tasks after merging with existing `TODO.md` state, so manually checking a task in `TODO.md` suppresses issue creation for that task.
- Issues are deduplicated using a stable `ix-bot-feedback-id:<id>` marker embedded in the issue body.
- Issues are labeled with `--label` (default: `ix-bot-feedback`).

## Options

```bash
intelligencex todo sync-bot-feedback \
  --repo EvotecIT/IntelligenceX \
  --todo TODO.md \
  --max-prs 30 \
  --bot intelligencex-review \
  --create-issues \
  --label ix-bot-feedback \
  --max-issues 20
```

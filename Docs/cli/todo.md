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

## Optional: create issues for unchecked items

```bash
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX --create-issues
```


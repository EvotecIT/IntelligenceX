# TODO Backlog Sync (Bot Reviews)

This repo tracks only explicit checklist items from bot reviews/comments in `TODO.md` under:

`## Review Feedback Backlog (Bots)`

## Sync Command

Run:

```bash
python3 scripts/sync_bot_feedback_todo.py --repo EvotecIT/IntelligenceX
```

Notes:
- This reads open PR reviews and issue comments authored by the bot login(s) (default: `intelligencex-review`).
- It only imports explicit markdown task list items (`- [ ] ...`, `- [x] ...`).
- Each imported task includes a link back to the originating review/comment.


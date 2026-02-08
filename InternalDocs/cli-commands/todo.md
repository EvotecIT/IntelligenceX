# TODO Backlog Sync (Bot Reviews)

This repo tracks only explicit checklist items from bot reviews/comments in `TODO.md` under:

`## Review Feedback Backlog (Bots)`

## Sync Command (Recommended)

Run:

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- todo sync-bot-feedback --repo EvotecIT/IntelligenceX
```

Notes:
- This reads open PR reviews and issue comments authored by the bot login(s) (default: `intelligencex-review`).
- It only imports explicit markdown task list items (`- [ ] ...`, `- [x] ...`).
- Each imported task includes a link back to the originating review/comment.

## GitHub Issues (Optional)

To create GitHub issues for unchecked items:

```bash
dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -- todo sync-bot-feedback --repo EvotecIT/IntelligenceX --create-issues
```

This will add `ix-bot-feedback-id:<id>` markers to issue bodies to avoid duplicates on re-runs.

## Legacy Script

The legacy helper is still available:

```bash
python3 scripts/sync_bot_feedback_todo.py --repo EvotecIT/IntelligenceX
```

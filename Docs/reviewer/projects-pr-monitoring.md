# Projects + PR Monitoring

IntelligenceX includes a GitHub-native maintainer control plane for PR and issue backlog monitoring using the `intelligencex todo ...` command family.

## Important disclaimer

These commands are assistive. They are not an autonomous production decision system.

- Treat `category`, `tags`, `IX Suggested Decision`, duplicate clusters, and scope classification as recommendations.
- Keep final decisions human-owned (`Maintainer Decision`, merge, close, defer).
- Do not wire this into unattended production mutation flows without explicit human review gates.

## What this gives you

- Bot-review checklist sync into `TODO.md` (`sync-bot-feedback`).
- Backlog indexing and duplicate clustering across open PRs/issues (`build-triage-index`).
- Scope alignment checks against `VISION.md` (`vision-check`).
- Issue applicability review for stale/no-longer-applicable infra blockers (`issue-review`).
- GitHub Project field sync for triage at scale (`project-init`, `project-sync`, `project-bootstrap`).
- Maintainer-assist view checklist and apply plan generation (`project-view-checklist`, `project-view-apply`).
- Signal quality grading (`high`/`medium`/`low`) to separate strong recommendations from weak-context items.
- Operational PR signals (`PR Size`, `PR Churn Risk`, `PR Merge Readiness`, `PR Freshness`, `PR Check Health`, `PR Review Latency`, `PR Merge Conflict Risk`) for faster triage.

## Recommended end-to-end flow

1. Pull explicit bot checklist items into `TODO.md`.
2. Build triage index artifacts.
3. Run issue applicability review.
4. Run vision alignment check.
5. Bootstrap or initialize GitHub Project.
6. Sync triage + vision into project fields.
7. Generate project view checklist and apply plan for maintainers.

## End-to-end example

```bash
# 1) Sync explicit bot checklist items to TODO.md
intelligencex todo sync-bot-feedback --repo EvotecIT/IntelligenceX

# 2) Build triage index artifacts
intelligencex todo build-triage-index --repo EvotecIT/IntelligenceX

# 3) Review infra blocker issue applicability (dry-run)
intelligencex todo issue-review --repo EvotecIT/IntelligenceX

# 4) Check backlog against VISION.md
intelligencex todo vision-check --repo EvotecIT/IntelligenceX --vision VISION.md

# 5) Bootstrap project + workflow + vision scaffold
intelligencex todo project-bootstrap --repo EvotecIT/IntelligenceX --owner EvotecIT

# 6) Sync triage + vision into project fields
intelligencex todo project-sync --config artifacts/triage/ix-project-config.json --max-items 500

# 7) Generate a maintainer view checklist
intelligencex todo project-view-checklist --config artifacts/triage/ix-project-config.json --create-issue
```

## Command quick map

| Command | Purpose | Typical output |
| --- | --- | --- |
| `sync-bot-feedback` | Extract explicit bot checklist tasks and keep them tracked in `TODO.md` | Updated `TODO.md` (optional GitHub issues) |
| `build-triage-index` | Build PR/issue inventory, duplicate clusters, and best PR ranking | `artifacts/triage/ix-triage-index.json`, `.md` |
| `issue-review` | Detect stale/no-longer-applicable infra blocker issues and optionally auto-close | `artifacts/triage/ix-issue-review.json`, `.md` |
| `vision-check` | Compare backlog against `VISION.md` scope | `artifacts/triage/ix-vision-check.json`, `.md` |
| `project-init` | Create/initialize GitHub Project fields + metadata | `artifacts/triage/ix-project-config.json` |
| `project-sync` | Push triage/vision signals to project items and optional labels/comments | Project field updates, optional comment/label updates |
| `project-bootstrap` | First-run bootstrap for project + workflow + vision scaffold | Project config + workflow + `VISION.md` scaffold |
| `project-view-checklist` | Build checklist of recommended project views | `artifacts/triage/ix-project-view-checklist.md` |
| `project-view-apply` | Build deterministic plan to apply missing views | `artifacts/triage/ix-project-view-apply.md` |

## Permissions and safety

- Project setup and sync requires GitHub `project` scope (`read:project` also required for sync reads).
- Issue-posting helpers require issue write permission.
- Use `--dry-run` on sync commands before enabling mutating operations.
- Treat low-signal items (`Signal Quality = low`, `ix/signal:low`) as context-gathering tasks, not decision-ready recommendations.

## Related docs

- [Project Bootstrap and Sync](/docs/reviewer/project-bootstrap-sync/)
- [Project Views and Operations](/docs/reviewer/project-views-and-ops/)
- [Reviewer Overview](/docs/reviewer/overview/)

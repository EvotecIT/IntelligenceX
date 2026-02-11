# IntelligenceX Repo Skills

Repo-local skills that make agents more consistent across onboarding, setup, analysis, and PR unblock work.

## Skill Catalog
- `intelligencex-onboarding-setup`: Use for CLI wizard + setup web onboarding changes.
- `intelligencex-analysis-gate`: Use for analysis catalog/packs/gate/run behavior and reliability work.
- `intelligencex-pr-unblock-loop`: Use for PR triage, CI failure classification, and merge-blocker closure loops.
- `intelligencex-reviewer-bootstrap`: Use for reviewer bootstrap (`reviewer.json` + workflow YAML) dry-runs and validation.
- `intelligencex-first-pr-rollout`: Use to verify first post-onboarding PR rollout signals (workflow, checks, sticky summary, analysis sections).

## Shared Rules
- Always use a dedicated `codex/*` branch and worktree.
- Never edit Website files unless explicitly requested.
- Run the minimum command set needed to prove the change.
- Use `gh` for PR/checks/comment workflows.

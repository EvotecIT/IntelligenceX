# Onboarding Unification Tracker

This file tracks onboarding/cleanup/maintenance unification across CLI, Web, and Bot surfaces.

## Scope

- Path-first onboarding with explicit flow definitions.
- Early auto-detection before repo selection.
- Shared setup argument/operation model across CLI and Web.
- Bot/tooling readiness for onboarding/fix/cleanup flows.
- Documentation and website FAQ alignment.

## Progress

| Workstream | Status | Owner | Notes |
|---|---|---|---|
| Add shared onboarding path catalog | Completed | IX CLI | Added runtime catalog in `IntelligenceX.Cli/Setup/Onboarding/SetupOnboardingPaths.cs` and reused in Web API path payloads. |
| Add auto-detect preflight (doctor-based) | Completed | IX CLI | Added `setup autodetect` + Web API endpoint `/api/setup/autodetect` + Manage menu entry. |
| Web: path-first + maintenance + auto-detect panel | Completed | IX CLI Web | Added maintenance path card and auto-detect panel before repo selection. |
| Web/CLI: centralize setup arg building | Completed | IX CLI | Web now builds args via `SetupPlan` + `SetupArgsBuilder`. |
| CLI wizard: honor preselected operation | Completed | IX CLI | `--operation` now skips re-prompt. |
| Manage hub: include cleanup and auto-detect | Completed | IX CLI | Setup menu now includes both flows. |
| .Tools: reviewer onboarding tool definitions | Completed | IX Tools | Added `IntelligenceX.Tools.ReviewerSetup` with `reviewer_setup_pack_info` and path/command contract data. |
| .Chat: expose richer tool metadata | Completed | IX Chat | Added `parametersJson` + `requiredArguments` in tool discovery response for better agent planning. |
| .Chat: register reviewer setup pack by default | Blocked | IX Chat + IX Tools | Pending after `IntelligenceX.Tools.ReviewerSetup` is available to Chat dependency graph in default checkout. |
| Docs: diagrams and flow updates | Completed | Docs | Added path-first flow docs and Mermaid diagrams in onboarding pages. |
| Website FAQ/data updates | Completed | Website | Updated FAQ/features/how-it-works for path-first + auto-detect positioning. |

## Remaining Blockers

- Chat service cannot yet consume `IntelligenceX.Tools.ReviewerSetup` in the default sibling checkout until the new tools pack is merged/published and referenced by `IntelligenceX.Chat`.
- Full `IntelligenceX.Chat.sln` and `IntelligenceX.Tools.sln` verification in this environment is blocked by existing external dependency gaps (`ComputerX`, `ADPlayground`, `EventViewerX`, `TestimoX`) unrelated to this change set.

## Next Actions

1. Merge/publish `IntelligenceX.Tools.ReviewerSetup`.
2. Add the tools pack reference + registration in `IntelligenceX.Chat`.
3. Add an end-to-end chat scenario test: `list_tools` includes `reviewer_setup_pack_info`, then validate returned path ids/command templates.
4. Mark this tracker as done when Chat wiring and test pass.

## Exit Criteria

- CLI, Web, and Bot consume the same onboarding path contract.
- `setup autodetect` is available and surfaced in Web step 1.
- Cleanup and maintenance are first-class flows in CLI/Web docs and UI.
- Bot can discover and execute onboarding/fix/cleanup via dedicated tools.
- Website/docs reflect current behavior and include flow diagrams.

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
| CLI wizard: path-first + auto-detect first step | Completed | IX CLI | Wizard now runs doctor-based auto-detect summary and path selection before auth/repo steps, with `--path` support for non-interactive path preselection. |
| CLI wizard: recommendation-first UX + verbose preflight diagnostics | Completed | IX CLI | Wizard now prints path recommendation before path selection and supports `--verbose` to include full auto-detect exception details when preflight fails. |
| Web/CLI: centralize setup arg building | Completed | IX CLI | Web now builds args via `SetupPlan` + `SetupArgsBuilder`. |
| CLI wizard: honor preselected operation | Completed | IX CLI | `--operation` now skips re-prompt. |
| Manage hub: include cleanup and auto-detect | Completed | IX CLI | Setup menu now includes both flows. |
| .Tools: reviewer onboarding tool definitions | Completed | IX Tools | Added `IntelligenceX.Tools.ReviewerSetup` with `reviewer_setup_pack_info` and path/command contract data. |
| .Chat: expose richer tool metadata | Completed | IX Chat | Added `parametersJson` + `requiredArguments` in tool discovery response for better agent planning. |
| .Chat: register reviewer setup pack by default | Completed | IX Chat + IX Tools | Chat now registers the reviewer setup tool pack by default through `ToolRegistryReviewerSetupExtensions`. |
| Shared canonical onboarding contract | Completed | IX Core | Added `IntelligenceX/Setup/Onboarding/SetupOnboardingContract.cs` as the source of truth for path ids and command templates. |
| Drift guard contract tests | Completed | IX + IX.Tools + IX.Chat | Added tests for CLI contract parity and bot/tool contract payload parity (`IntelligenceX.Tests`, `IntelligenceX.Tools.Tests`, `IntelligenceX.Chat.Tests`). |
| Contract version/fingerprint surfaced by autodetect | Completed | IX Core + IX CLI Web | Added `ContractVersion` + deterministic contract fingerprint in `SetupOnboardingContract`, emitted by `setup autodetect` JSON and Web autodetect API/summary panel. |
| Shared core contract verifier API | Completed | IX Core | Added `SetupOnboardingContractVerification` for canonical autodetect/pack metadata drift checks used by setup surfaces and tools. |
| .Tools: setup pack exposes contract metadata | Completed | IX Tools | `reviewer_setup_pack_info` now emits `contractVersion` + `contractFingerprint` in `setup_hints` for bot-side parity checks. |
| .Tools: contract verifier tool for autodetect parity | Completed | IX Tools | Added `reviewer_setup_contract_verify` so agents can validate autodetect contract metadata before setup/update-secret/cleanup apply. |
| .Tools: verifier uses shared core API | Completed | IX Tools + IX Core | `reviewer_setup_contract_verify` now delegates to `SetupOnboardingContractVerification` to avoid duplicate drift logic. |
| .Chat: enforce contract parity in playbook | Completed | IX Chat | Host prompt now requires comparing pack contract metadata with autodetect output before apply/cleanup operations. |
| .Chat: execute parity via verifier tool | Completed | IX Chat | Host prompt and tests now require using `reviewer_setup_contract_verify` after autodetect and before mutating onboarding commands. |
| .Chat: autodetect-first onboarding playbook | Completed | IX Chat | Updated `Docs/HostSystemPrompt.md` to require `reviewer_setup_pack_info` + preflight autodetect before path execution. |
| Web UI: path hints from shared contract metadata | Completed | IX CLI Web | Step-1 path cards/hints/requirements now hydrate from autodetect `paths` + `contractVersion`/`contractFingerprint` instead of hardcoded path text. |
| Web API: include command templates in autodetect payload | Completed | IX CLI Web | `/api/setup/autodetect` now returns `commandTemplates` sourced from `SetupOnboardingContract` for CLI/Web/Bot parity. |
| Web API: autodetect response parity regression coverage | Completed | IX Tests + IX CLI Web | Added web-response contract parity tests (`BuildSetupAutodetectResponseJsonForTests`) and null-safe fallbacks for `paths`/`commandTemplates` projection. |
| Autodetect contract payload immutability hardening | Completed | IX CLI | Switched `SetupOnboardingAutoDetectResult` and `SetupOnboardingCheck` to `init`-based properties to reduce post-construction mutation risk. |
| Docs: canonical path matrix + Bot contract-check diagram | Completed | Docs | `Docs/reviewer/web-onboarding.md` now includes per-path auth/operation matrix and Mermaid flow for tool-driven parity verification. |
| Docs: diagrams and flow updates | Completed | Docs | Added path-first flow docs and Mermaid diagrams in onboarding pages. |
| Website FAQ/data updates | Completed | Website | Updated FAQ/features/how-it-works for path-first + auto-detect positioning. |
| CLI/Web clean-machine acceptance coverage | Completed | IX Tests | Added deterministic fake-GitHub acceptance tests for wizard (`--plain`) and web setup args paths to assert `"PR created"` flow without manual repo edits. |

## Remaining Blockers

None for this unification scope. As of February 12, 2026:
- `IntelligenceX.Tools` PR `#149` is merged.
- `IntelligenceX.Tools` PR `#150` is merged.
- `IntelligenceX.Chat` PR `#43` is merged.
- `IntelligenceX.Chat` PR `#44` is merged.
- `IntelligenceX.Chat` PR `#46` is merged.
- `IntelligenceX` PR `#248` is merged.
- `IntelligenceX` PR `#249` is merged.
- `IntelligenceX` PR `#250` is merged.
- `IntelligenceX` PR `#251` is merged.
- `IntelligenceX` PR `#252` is merged.
- `IntelligenceX` PR `#255` is merged.
- `IntelligenceX.Tools` PR `#151` is merged.
- `IntelligenceX.Tools` PR `#152` is merged.
- `IntelligenceX.Tools` PR `#153` is merged.
- `IntelligenceX.Chat` PR `#47` is merged.
- `IntelligenceX.Chat` PR `#48` is merged.

## Next Actions

1. Keep website/docs flow diagrams and FAQ data synchronized with future onboarding path or command-template changes.
2. Consider adding a CI automation that periodically verifies all repos consume the latest onboarding contract version.

## Exit Criteria

- CLI, Web, and Bot consume the same onboarding path contract.
- `setup autodetect` is available and surfaced in Web step 1.
- Cleanup and maintenance are first-class flows in CLI/Web docs and UI.
- Bot can discover and execute onboarding/fix/cleanup via dedicated tools.
- Website/docs reflect current behavior and include flow diagrams.

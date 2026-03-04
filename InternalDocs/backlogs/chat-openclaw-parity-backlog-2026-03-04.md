# Chat OpenClaw Parity Backlog (2026-03-04)

Scope: convert OpenClaw parity findings into concrete IntelligenceX.Chat engineering tasks.
Reference snapshot: `openclaw` commit `e6f0203ef395850fc459ce835f1a73c637ff03ca`.

## Parity Targets
- Keep Chat runtime autonomous under long turns without adding scripted lexical gates.
- Keep Tools fully pluggable and contract-driven (manifest-first, no implicit DLL fallbacks).
- Keep execution deterministic with explicit scheduling and resumable state.

## Observed OpenClaw Patterns (Source Review)
- Session+global queue lanes serialize agent work and avoid overlapping turn execution.
- Skills/runtime snapshot is persisted per session and reused across turns.
- Plugin loading is manifest-contract-first and explicit.
- Agent loop uses explicit run lifecycle hooks and status progression.

## IntelligenceX.Chat Gap-to-Action Items
- [x] Add per-session execution lane + optional global lane throttling in Chat service turn orchestration.
- [x] Persist a compact session capability snapshot (enabled packs, routing families, tool health) and reuse it for continuation turns.
- [x] Add explicit long-turn heartbeat/status events during model/tool orchestration phases.
- [x] Add structured continuation contract markers for "continue work" turns to reduce unnecessary clarification loops.
- [ ] Replace any remaining lexical fallback routing gates with structure-first contracts where still present.
- [x] Add end-to-end regression suite for continuation behavior: "continue", "keep going", and non-English continuation prompts with identical structured intent.
- [ ] Add a plugin lifecycle contract test suite: manifest validation, load order, health probe contract, and failure-mode telemetry.

## Progress In This Branch
- [x] Plugin folder discovery is manifest-first (`ix-plugin.json` required).
- [x] Manifestless plugin folders and archives are skipped with explicit warnings.
- [x] Plugin manifests are validated as strict contracts (`schemaVersion`, `pluginId`, `entryAssembly`, `entryType` required).
- [x] Absolute/escaping `entryAssembly` paths are rejected.
- [x] Service and Host now allow plugin-only toolless startup when zero packs are loaded (warning emitted, no hard startup failure).
- [x] Host profile switch now rolls back on plugin bootstrap configuration failures instead of leaving partially applied state.

# Private Tools Migration Playbook

## Goal

Keep `IntelligenceX` open-source and portable, while keeping private engines (`ComputerX`, `TestimoX`, `ADPlayground`) private and still easy to develop/test.

## Target Repository Split

1. `EvotecIT/IntelligenceX` (public)
2. Owns contracts, host/runtime, CLI, reviewer, docs, and portable tool packs.
3. Must build and test with no private feed and no sibling private repos.

4. `EvotecIT/TestimoX` (private)
5. Owns private-engine tool plugins for IntelligenceX.
6. Builds against private engines and runs private integration tests.

## What Stays Where

1. Public repo (`IntelligenceX`) keeps:
2. `IntelligenceX.Tools.Common`
3. `IntelligenceX.Tools.Email`
4. `IntelligenceX.Tools.ReviewerSetup`
5. Runtime plugin loader and plugin contract handling.

6. Private repo (`TestimoX`) moves to:
7. `IntelligenceX.Private.Tools.System` (depends on `ComputerX`)
8. `IntelligenceX.Private.Tools.FileSystem` (depends on `ComputerX`)
9. `IntelligenceX.Private.Tools.PowerShell` (depends on `ComputerX`)
10. `IntelligenceX.Private.Tools.EventLog` (depends on `EventViewerX` if you decide private for this one)
11. `IntelligenceX.Private.Tools.ActiveDirectory` (depends on `ADPlayground`)
12. `IntelligenceX.Private.Tools.TestimoX` (depends on `TestimoX`)

## Dependency Rules

1. Public `IntelligenceX` must not reference private projects, private feeds, or local private paths.
2. Private plugin projects may reference public `IntelligenceX` contracts (`ITool`, `IToolPack`).
3. Dependency direction is one-way:
4. `private plugins -> public IntelligenceX`
5. `public IntelligenceX -X-> private plugins`

## Runtime Loading Model

1. Add a plugin folder convention:
2. Default path: `./plugins`
3. Optional env var override: `INTELLIGENCEX_PLUGIN_PATH`

4. On startup:
5. Scan plugin assemblies in folder.
6. Find public, concrete `IToolPack` implementations.
7. Instantiate and register tool packs safely.
8. Log load failures without crashing host startup.

9. If a plugin is missing:
10. Host still starts and remains usable.
11. Missing tools are simply not registered.

## CI Model

1. Public CI in `IntelligenceX`:
2. Uses only public code and public NuGet.
3. Must include host + plugin loader tests with no plugins present.

4. Private CI in `TestimoX`:
5. Restores private dependencies.
6. Builds private tool plugins.
7. Runs integration tests with private engines.
8. Produces plugin artifacts (NuGet/zip/dll set).

## Publishing Model

1. Publish `Mailozaurr`, `EventViewerX`, `DbaClientX` updates to NuGet first.
2. Switch public repo from sibling source references to NuGet where possible.
3. Private plugins are shipped separately from `TestimoX` artifacts/packages.
4. End users can drop private plugin binaries into `plugins/` directory.

## First Migration PR Checklist (PR-1 in `IntelligenceX`)

1. Scope: remove open-source blockers and prepare plugin architecture.
2. Replace local source refs with NuGet where possible:
3. `IntelligenceX.Tools.Email` -> `Mailozaurr` package only.
4. `IntelligenceX.Chat.Service` -> `DbaClientX` package fallback or package-first.
5. `IntelligenceX.Tools.EventLog` -> `EventViewerX` package if published.

6. Add plugin loader scaffold in host/service:
7. Add plugin directory options and environment variable support.
8. Add safe load logging.
9. Add unit tests for discovery and failure handling.

10. Stop compiling private-engine packs in public default build:
11. Remove private-engine packs from public solution filter or gate them behind explicit opt-in.
12. Keep CI green with no private repos on disk.

13. Update docs:
14. Public quick start uses host only.
15. Add private plugin install doc section.
16. Document that private plugins are optional.

17. Acceptance criteria:
18. `dotnet build IntelligenceX.CI.slnf -c Release` passes on clean machine.
19. Host starts with empty plugins folder.
20. Host loads sample test plugin from plugins folder.
21. No `ProjectReference` in public repo points to private sibling paths.

## PR-2 Checklist (move private packs to `TestimoX`)

1. Create private plugin projects in `TestimoX`.
2. Port tool code from public repo into private plugin projects.
3. Add integration tests in `TestimoX` for private engines.
4. Produce plugin artifacts in private CI.
5. Validate plugin drop-in with public `IntelligenceX` host.
6. Remove migrated private pack source projects from public repo.

## Commands for Validation

1. Public clean build:
2. `dotnet build IntelligenceX.CI.slnf -c Release`

3. Public portable tool packing:
4. `pwsh ./scripts/pack-tools.ps1 -Profile Portable -Configuration Release`

5. Public host publish:
6. `pwsh ./scripts/publish-chat.ps1 -Mode Host -Runtime win-x64 -Configuration Release -Zip:$false`


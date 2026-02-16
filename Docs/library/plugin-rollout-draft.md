# Plugin Rollout Draft (Open + Private Packs)

This draft defines a practical model for next-week open source:
- one app to start (no host/service juggling by users)
- portable install option
- plugin packs that can be public or private
- CI-safe builds that do not depend on local/private source checkouts

## 1. Product UX Contract

Users should run one command/app only:

```powershell
intelligencex chat
```

Implementation detail:
- default mode: `IntelligenceX.Chat` starts service runtime in-process
- optional mode (internal/advanced): out-of-process worker for isolation
- both modes use the same plugin loader and same profile format

No user should need to manually start "host" and "service" as separate apps.

## 2. Plugin Model

A plugin is a NuGet package that ships one or more `IToolPack` implementations plus a small manifest.

Recommended manifest file: `ix-plugin.json` at package root.

```json
{
  "schemaVersion": 1,
  "pluginId": "evotec.testimox",
  "displayName": "TestimoX Pack",
  "packageId": "IntelligenceX.Tools.TestimoX",
  "version": "1.0.0",
  "entryAssembly": "IntelligenceX.Tools.TestimoX.dll",
  "entryType": "IntelligenceX.Tools.TestimoX.TestimoXToolPackFactory",
  "platform": "windows",
  "capabilityTier": "elevated",
  "requires": [
    {
      "id": "TestimoX",
      "minVersion": "1.0.0"
    }
  ]
}
```

If `entryType` is omitted, loader can fall back to discovered `IToolPack` types in `entryAssembly`.

## 3. Discovery and Load Order

Load order should be deterministic:
1. built-in packs (public, always available)
2. user plugin path: `%LOCALAPPDATA%\IntelligenceX.Chat\plugins`
3. portable plugin path: `./plugins` (relative to app executable)
4. optional extra paths from profile/config

On load failure, do not crash startup. Emit explicit diagnostics:
- plugin id
- package/version
- failure category (`dependency_missing`, `entry_not_found`, `init_failed`)
- actionable remediation

## 4. Public vs Private Packs

### Public (open-source friendly)
- `IntelligenceX.Tools.FileSystem`
- `IntelligenceX.Tools.EventLog` (after NuGet-ready dependency cleanup)
- `IntelligenceX.Tools.Email` (Mailozaurr-based)

### Private-capable (your use case)
- `IntelligenceX.Tools.System`
- `IntelligenceX.Tools.TestimoX`
- `IntelligenceX.Tools.ADPlayground` (if backed by private ADPlayground/TestimoX engines)
- future `IntelligenceX.Tools.ComputerX`

Recommendation:
- move private tool-pack projects and private engine dependencies to `TestimoX` repo
- publish those plugin packages to a private NuGet feed
- keep open-source `IntelligenceX` core independent from private package references

This lets you keep developing private packs without blocking open-source CI.

Default OSS profile guidance:
- include only public packs by default
- do not hard-fail when private pack ids are configured but unavailable
- show clear startup warning with feed/auth remediation

## 5. Development Against Private Repos

Use package-first development, not source-first coupling:
1. private repos publish prerelease packages to private feed on every merge (for example `-alpha.*`)
2. `IntelligenceX` consumes package versions via central package management (`Directory.Packages.props`) or explicit `PackageReference` versions
3. local override mode for maintainers:
   - optional `NuGet.Config` for private feed auth
   - optional local source mapping when intentionally developing pack + host together
4. OSS CI runs with public feeds only and skips private plugin integration tests
5. internal CI runs with private feed credentials and enables private plugin integration tests

## 6. Build/Publish Scripts (Recommended)

Add/standardize scripts in `Build/`:
- `Publish-Chat.ps1`
  - publishes single user-facing chat app (in-process runtime by default)
- `Package-Portable.ps1`
  - creates portable zip with `plugins/` folder and sample profile
- `Publish-Plugins.ps1`
  - packs/publishes plugin NuGets (public/private feed switch)
- `Verify-PluginContract.ps1`
  - validates `ix-plugin.json`, tool schema export, and startup load checks

Keep `Publish-Cli.ps1` for CLI distribution, but chat/open-source onboarding should point to one app path first.

## 7. Naming Guidance

Use `Contracts` only for shared abstractions/DTOs.

Suggested split:
- `IntelligenceX.Tools.Contracts` (interfaces + common contracts only)
- `IntelligenceX.Tools.<Domain>` (actual tool packs)

Avoid naming runtime/plugin packages as `Contract` unless they only contain contracts.

## 8. Migration Plan

1. Finalize plugin manifest + loader contract in IntelligenceX.
2. Move private packs (`System`, `TestimoX`, `AD`, `ComputerX`) to TestimoX repo and publish from there.
3. Replace local source dependencies in OSS tool packs with NuGet package refs.
4. Ship single-app startup (`intelligencex chat`) with built-in profile and plugin diagnostics.
5. Add portable package artifact and docs.

## 9. Release Gate Checklist

- [ ] `intelligencex chat` works without separately starting service
- [ ] plugin loader reports clear startup errors (no silent failures)
- [ ] OSS CI passes with only public feeds
- [ ] internal CI passes with private feeds and private plugins enabled
- [ ] EventViewerX/Mailozaurr/DbaClientX packages consumed from NuGet in plugin packs
- [ ] documentation includes private plugin recipe for downstream users

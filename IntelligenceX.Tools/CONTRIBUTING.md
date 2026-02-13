# Contributing

This repo contains **tool packs**: optional libraries implementing `ITool` for IntelligenceX tool calling.

## Rules (keep the ecosystem sane)

- Provider-agnostic: do not take dependencies on `IntelligenceX.OpenAI.*` or provider-specific packages.
- Keep dependencies isolated: each pack owns its dependency graph.
- Windows-only capabilities must not leak into cross-platform packs.

## Naming

- Repo: `IntelligenceX.Tools`
- Pack projects: `IntelligenceX.Tools.<Domain>`
  - Examples: `IntelligenceX.Tools.Email`, `IntelligenceX.Tools.FileSystem`
- Avoid embedding implementation names in the package (for example avoid `...Mailozaurr` in the package name).

## Target frameworks

- Default: `net8.0;net10.0`
- Windows-only tools: use a separate pack and target `net8.0-windows;net10.0-windows` (preferred).

## How to add a pack

1. Create a new folder `IntelligenceX.Tools.<Domain>/`.
2. Add a new SDK-style csproj with strict settings (nullable, warnings as errors).
3. Reference the tool contract:
   - For now, this repo uses `ProjectReference` to `../IntelligenceX/IntelligenceX/IntelligenceX.csproj`.
4. Implement tools via `IntelligenceX.Tools.ITool`.
5. Keep tools safe-by-default:
   - validate input schema
   - size/time limits
   - return machine-readable JSON where useful (plus a human summary if needed)

## Testing

- Add at least a smoke test for schema + happy path invocation if the pack has non-trivial behavior.
- If a tool hits external systems (IMAP/AD/EventLog), prefer mocked/unit tests and keep integration tests opt-in.


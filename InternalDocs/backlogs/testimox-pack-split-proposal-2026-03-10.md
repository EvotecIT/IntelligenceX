# TestimoX Pack Split Proposal

## Goal

Split the current `IntelligenceX.Tools.TestimoX` surface into two self-registering packs so the runtime, operator, and chat planner can distinguish between:

- rule/baseline/profile/execution workflows
- persisted monitoring/report/history artifact inspection

This keeps `.Chat` more honest about what is live execution versus local/shared artifact reading.

## Proposed Packs

### `IntelligenceX.Tools.TestimoX`

Purpose:

- rule discovery
- baseline/profile catalog lookups
- source/provenance
- baseline compare/crosswalk
- targeted rule execution
- stored run inspection

Suggested pack id:

- `testimox`

Suggested runtime intent:

- discovery -> selection -> execution -> run receipt follow-up

### `IntelligenceX.Tools.TestimoX.Monitoring`

Purpose:

- monitoring history inspection
- report job history
- monitoring diagnostics snapshots
- report data/html snapshot readers
- maintenance-window history
- probe index status

Suggested pack id:

- `testimox_monitoring`

Suggested runtime intent:

- persisted monitoring/report/history artifact inspection only

## Phase-1 File Split

### Keep in core `TestimoX`

- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRulesListTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRulesRunTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXProfilesListTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRuleInventoryTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXSourceQueryTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXBaselinesListTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXBaselineCompareTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXBaselineCrosswalkTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRunsListTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRunSummaryTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRuleSelectionHelper.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXRuleInventoryHelper.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXBaselineCatalogHelper.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXStoreCatalogHelper.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXPagingHelper.cs`

### Move to `TestimoX.Monitoring`

- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXReportJobHistoryTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXHistoryQueryTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXProbeIndexStatusTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXMonitoringDiagnosticsGetTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXMaintenanceWindowHistoryTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXReportDataSnapshotGetTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXReportSnapshotGetTool.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXMonitoringHistoryHelper.cs`

## Options Split

### Core options

Suggested type:

- `TestimoXCoreToolOptions`

Fields:

- `Enabled`
- `MaxRulesInCatalog`
- `MaxRulesPerRun`
- `DefaultConcurrency`
- `MaxConcurrency`
- `DefaultIncludeSupersededRules`
- `MaxResultRowsPerRule`
- `AllowedStoreRoots`

### Monitoring options

Suggested type:

- `TestimoXMonitoringToolOptions`

Fields:

- `Enabled`
- `MaxHistoryRowsInCatalog`
- `MaxSnapshotContentChars`
- `AllowedHistoryRoots`

### Compatibility shim

Suggested temporary bridge:

- keep `TestimoXToolOptions`
- map it into the two specialized option objects for one release

## Registration Split

Current monolithic files:

- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/ToolRegistryTestimoXExtensions.cs`
- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXToolPack.cs`

Target shape:

- keep `RegisterTestimoXPack(...)` for core
- add `RegisterTestimoXMonitoringPack(...)` for monitoring artifacts
- optionally keep a compatibility overload that registers both temporarily

## Pack-Info Split

Current:

- `IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/TestimoXPackInfoTool.cs`

Target:

- `testimox_pack_info`
  only rules, profiles, baselines, execution, stored runs
- `testimox_monitoring_pack_info`
  only monitoring diagnostics/history/report/snapshot/maintenance artifact surfaces

## Chat and Runtime Implications

- parity inventory should stop treating all TestimoX surfaces as one blended family
- `/toolhealth` should show at least:
  - `testimox_core`
  - `testimox_monitoring_artifacts`
  - `testimox_powershell`
- runtime enablement should become independently controllable:
  - `enable_testimox_pack`
  - `enable_testimox_monitoring_pack`

## Dependency Boundary

Current `IntelligenceX.Tools.TestimoX.csproj` mixes:

- `TestimoX`
- `ADPlayground.Monitoring`

Target split:

- core project references `TestimoX`
- monitoring project references `ADPlayground.Monitoring`

This makes the dependency story clearer and avoids conflating live rule execution with artifact inspection.

## Migration Order

1. Add the new monitoring project and pack without changing tool names.
2. Move monitoring/history/report/snapshot readers into the new project.
3. Split tool contracts and pack-info surfaces.
4. Split parity inventory and `/toolhealth` reporting.
5. Add independent runtime enable flags.
6. Decide later whether stored runs remain in core or become a third artifact-focused pack.

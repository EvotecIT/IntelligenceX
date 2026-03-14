# 700 LOC Maintainability Plan

Date: 2026-03-14
Workspace: `C:\Support\GitHub\_worktrees\js-shell18-rendering-split`
Branch: `codex/js-shell18-rendering-split`

## Goal

Bring tracked repo-owned source and test files back under the internal 700 LOC maintainability threshold using small, controlled, behavior-preserving refactors.

## Scope

Included:
- tracked source and test files with extensions `.cs`, `.js`, `.mjs`, `.cjs`, `.ts`, `.tsx`, `.jsx`, `.ps1`, `.psm1`, `.psd1`, `.xaml`, `.razor`, `.cshtml`, `.sql`
- repo-owned code and tests only

Excluded:
- vendored or minified assets
- generated files when they appear in future scans
- website work unless explicitly requested

## Current Snapshot

- 76 repo-maintained files are above 700 LOC
- 47 are repo code files
- 29 are test files
- 1 additional tracked file is above 700 LOC but should be excluded from refactor planning because it is vendored/minified:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/vendor/mermaid/mermaid.min.js` at 2567 LOC

Snapshot note:
- this count was refreshed after the latest controlled splits in this branch and is the authoritative remaining total for this worktree
- some batch/file LOC figures below are still useful for ordering, but they should be treated as planning estimates until the next full inventory refresh

Completed in this branch:
- `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.18.core.tools.rendering.js`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.18.core.tools.rendering.js` at 515 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.18a.transcript.rendering.js` at 239 LOC
- `IntelligenceX.Cli/Setup/Web/Assets/wizard.js`
  - split into:
  - `IntelligenceX.Cli/Setup/Web/Assets/wizard.js` at 583 LOC
  - `IntelligenceX.Cli/Setup/Web/Assets/wizard.setup.js` at 558 LOC
  - `IntelligenceX.Cli/Setup/Web/Assets/wizard.formatting.js` at 274 LOC
  - `IntelligenceX.Cli/Setup/Web/Assets/wizard.flows.js` at 540 LOC
- `IntelligenceX.Cli/Setup/SetupRunner.Types.cs`
  - split into:
  - `IntelligenceX.Cli/Setup/SetupRunner.Types.cs` at 393 LOC
  - `IntelligenceX.Cli/Setup/SetupRunner.Types.Settings.cs` at 326 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/PluginFolderToolPackLoader.Loading.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/PluginFolderToolPackLoader.Loading.cs` at 327 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/PluginFolderToolPackLoader.Loading.Support.cs` at 382 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/LocalExportArtifactWriterTests.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/LocalExportArtifactWriterTests.cs` at 617 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/LocalExportArtifactWriterTests.DocxRendering.cs` at 219 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.PendingActionsCtaConfirmation.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.PendingActionsCtaConfirmation.cs` at 594 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.PendingActionsSelection.cs` at 252 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ServiceOptionsProfileBootstrapTests.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ServiceOptionsProfileBootstrapTests.cs` at 636 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ServiceOptionsProfileBootstrapTests.RuntimePolicy.cs` at 185 LOC
- `IntelligenceX.Cli/Todo/PrWatchConsolidationRunner.cs`
  - split into:
  - `IntelligenceX.Cli/Todo/PrWatchConsolidationRunner.cs` at 636 LOC
  - `IntelligenceX.Cli/Todo/PrWatchConsolidationRunner.TrackerIssues.cs` at 174 LOC
- `IntelligenceX.Tests/Program.Reviewer.CleanupAndThreads.cs`
  - split into:
  - `IntelligenceX.Tests/Program.Reviewer.CleanupAndThreads.cs` at 700 LOC
  - `IntelligenceX.Tests/Program.Reviewer.ThreadResolutionHelpers.cs` at 107 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.PendingActions.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.PendingActions.cs` at 625 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.PendingActions.IntentSelection.cs` at 217 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentSignals.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentSignals.cs` at 583 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentSignals.Lexicon.cs` at 287 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.ToolNudge.StructuredNextAction.cs`
  - reduced to 700 LOC via whitespace-only trim
- `IntelligenceX.Tests/Program.Setup.cs`
  - split into:
  - `IntelligenceX.Tests/Program.Setup.cs` at 510 LOC
  - `IntelligenceX.Tests/Program.Setup.OpenAiRoutingMerge.cs` at 201 LOC
- `IntelligenceX.Tests/Program.Reviewer.ResolveAndFilters.cs`
  - split into:
  - `IntelligenceX.Tests/Program.Reviewer.ResolveAndFilters.cs` at 560 LOC
  - `IntelligenceX.Tests/Program.Reviewer.ResolveAndFilters.AzureDevOps.cs` at 153 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.ProfileUpdates.CapabilitySelfKnowledge.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.ProfileUpdates.CapabilitySelfKnowledge.cs` at 591 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.ProfileUpdates.CapabilitySelfKnowledge.Support.cs` at 124 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.AccountUsage.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.AccountUsage.cs` at 626 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.AccountUsage.StatePublishing.cs` at 89 LOC
- `IntelligenceX/Providers/OpenAI/Usage/ChatGptUsageModels.cs`
  - split into:
  - `IntelligenceX/Providers/OpenAI/Usage/ChatGptUsageModels.cs` at 514 LOC
  - `IntelligenceX/Providers/OpenAI/Usage/ChatGptUsageModels.Breakdowns.cs` at 212 LOC
- `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolSchemaSnapshotTests.ActiveDirectory.cs`
  - split into:
  - `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolSchemaSnapshotTests.ActiveDirectory.cs` at 622 LOC
  - `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolSchemaSnapshotTests.ActiveDirectory.Gpo.cs` at 98 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.IntelligenceLoop.VisualCatalog.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.IntelligenceLoop.VisualCatalog.cs` at 534 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.IntelligenceLoop.VisualCatalog.Support.cs` at 183 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.UiState.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.UiState.cs` at 634 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.UiState.DiagnosticsSupport.cs` at 82 LOC
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.PersistenceHelpers.cs`
  - split into:
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.PersistenceHelpers.cs` at 456 LOC
  - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.PersistenceHelpers.StateSupport.cs` at 270 LOC
- `IntelligenceX.Reviewer/ReviewSettings.Environment.cs`
  - split into:
  - `IntelligenceX.Reviewer/ReviewSettings.Environment.cs` at 12 LOC
  - `IntelligenceX.Reviewer/ReviewSettings.Environment.Support.cs` at 469 LOC
  - `IntelligenceX.Reviewer/ReviewSettings.Environment.CommentAndCleanup.cs` at 439 LOC
- `IntelligenceX.Reviewer/GitHubClient.cs`
  - split into:
  - `IntelligenceX.Reviewer/GitHubClient.cs` at 574 LOC
  - `IntelligenceX.Reviewer/GitHubClient.HttpSupport.cs` at 167 LOC

## Refactor Rules

- Use a dedicated worktree and branch per batch.
- Keep each batch scoped to one subsystem or one clearly related cluster.
- Preserve public/runtime contracts first, then split internals behind those contracts.
- Prefer extracting adjacent helpers, partials, and feature slices over rewriting logic.
- Split tests by scenario family, contract surface, or workflow phase.
- Do not mix behavior changes with maintainability-only moves unless a failing test forces it.
- Re-run the narrowest relevant test/build gate after each batch.

## Recommended Batch Order

### Batch 1: Chat UI shell JavaScript

Why first:
- highest LOC concentration
- explicit shell manifest already exists
- runtime order is already test-covered

Target files:
- `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.21.core.visuals.js` - 3826
- `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.15.core.tools.js` - 3422
- `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.10.core.js` - 2257
- `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.20.bindings.js` - 1562

Suggested split style:
- split by modal/surface ownership
- keep manifest order explicit in `UiShellAssets.cs`
- update `UiShellAssetsTests.cs` for new chunk ownership

### Batch 2: Setup and CLI web assets

Why next:
- standalone surface
- fewer cross-project dependencies

Target files:
- `IntelligenceX.Cli/Setup/Web/Assets/wizard.js` - completed in this branch
- `IntelligenceX.Cli/Setup/SetupRunner.Types.cs` - completed in this branch

Suggested split style:
- split wizard by sections or step flows
- move shared request/validation/serialization helpers out of `SetupRunner.Types.cs`

### Batch 3: Telemetry and visualization

Why next:
- domain-oriented cluster
- likely clean helper extraction paths

Target files:
- `IntelligenceX.Cli/Telemetry/UsageTelemetryCliRunner.cs` - 1708
- `IntelligenceX/Visualization/Heatmaps/UsageTelemetryOverviewBuilder.cs` - 1575
- `IntelligenceX/Telemetry/Usage/SqliteUsageTelemetryStores.cs` - 1255
- `IntelligenceX/Tools/ToolSelectionMetadata.cs` - 1029
- `IntelligenceX/Tools/ToolRegistry.cs` - 745
- `IntelligenceX.Tests/Program.Telemetry.Usage.Overview.cs` - 812

Suggested split style:
- projector/builder/query/writer separation
- one store type per file when practical
- matching telemetry test splits in the same batch

### Batch 4: Chat app window lifecycle and messaging

Why next:
- visible UI ownership boundaries already exist in filenames
- good candidate for partial-class decomposition

Target files:
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.StartupFlow.cs` - 1159
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.TurnQueue.cs` - 898
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.Connection.cs` - 878
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.ServiceMessages.cs` - 770
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.cs` - 722
- `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/MainWindowStartupConnectTimeoutPolicyTests.cs` - 934
- `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/UiShellAssetsTests.cs` - 812
- `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/LocalExportArtifactWriterTests.cs` - completed in this branch

Suggested split style:
- partials by startup, connection, queue, host messaging, export, shell composition
- tests split by scenario family instead of one broad fixture

### Batch 5: Chat service routing core

Why next:
- highest-risk C# cluster
- needs smaller, contract-first batches

Target files:
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.IntelligenceLoop.PhaseProgressAndFallback.cs` - 1122
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.cs` - 1091
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.RoutingHelpers.cs` - 1061
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.RequestFlow.cs` - 1051
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolEvidenceCache.cs` - 1003
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.Secondary.cs` - 998
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolExecution.RuntimeAndRecovery.cs` - 891
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.cs` - 875
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentAffinity.cs` - 799
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.PendingActions.cs` - completed in this branch
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentSignals.cs` - completed in this branch
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ServiceOptions.cs` - 758

Suggested split style:
- extract evaluators, selectors, policy builders, scoring helpers, DTO normalizers
- preserve session-facing entrypoints and message contracts
- use narrow test-batch follow-ups after each source split

### Batch 6: Chat service profile, memory, tooling, and host

Why next:
- adjacent to Batch 5 but less central to live routing

Target files:
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ProfilesAndModels.cs` - 1516
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.WorkingMemoryCheckpoint.cs` - 1222
- `IntelligenceX.Chat/IntelligenceX.Chat.Profiles/SqliteServiceProfileStore.cs` - 871
- `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/ToolOrchestrationCatalog.cs` - 850
- `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/ToolPackBootstrap.RegistryAndReflection.cs` - 804
- `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/PluginFolderToolPackLoader.Loading.cs` - completed in this branch
- `IntelligenceX.Chat/IntelligenceX.Chat.Host/Program.Options.cs` - 805
- `IntelligenceX.Chat/IntelligenceX.Chat.Host/Program.Session.ScenarioContractParsing.cs` - 751

Suggested split style:
- split storage/repository access from normalization and projections
- split host CLI parsing from option validation and defaults
- split tooling discovery from registry materialization

### Batch 7: Tooling/common packages

Why next:
- smaller cluster
- straightforward helper extraction

Target files:
- `IntelligenceX.Tools/IntelligenceX.Tools.Common/ToolPackGuidance.cs` - 949
- `IntelligenceX.Tools/IntelligenceX.Tools.Common/ToolPackGuidance.NormalizationHelpers.cs` - 754
- `IntelligenceX.Tools/IntelligenceX.Tools.ADPlayground/AdMonitoringProbeRunTool.Helpers.cs` - 733
- `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolDefinitionContractTests.cs` - 2492
- `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolPackInfoContractTests.cs` - 838
- `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolPackGuidanceTests.cs` - 804

Suggested split style:
- split contract tests by metadata/schema/registry family
- extract guidance formatting/normalization helpers into dedicated files

### Batch 8: Chat service and tooling tests

Why last:
- many test files mirror production complexity
- best split once production seams are clearer

Target files:
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.IntelligenceLoop.EndToEnd.cs` - 4301
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServicePlannerPromptTests.cs` - 1990
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceDomainAffinityTests.cs` - 1258
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.ToolBatchRecovery.cs` - 1231
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.IntelligenceLoop.cs` - 1219
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceToolingBootstrapTests.cs` - 1147
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/PluginFolderLoaderTests.cs` - 1097
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRetryPolicyTests.cs` - 997
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ToolCapabilityParityInventoryBuilderTests.cs` - 956
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ToolPackBootstrapMetadataTests.cs` - 919
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/HostNoToolRetryHeuristicsTests.ToolFallbacks.cs` - 807
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/HostScenarioAssertionTests.cs` - 773
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ToolOrchestrationCatalogTests.cs` - 766
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.IntelligenceLoop.VisualContracts.cs` - 752
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.ToolNudge.ExecutionContract.Carryover.cs` - 749
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceToolEvidenceCacheTests.cs` - 728
- `IntelligenceX.Chat/IntelligenceX.Chat.Tests/HostNoToolRetryHeuristicsTests.cs` - 726
- `IntelligenceX.Tests/Program.Core.cs` - 726
- `IntelligenceX.Tests/Program.Reviewer.CleanupAndThreads.cs` - completed in this branch

Suggested split style:
- split by scenario family, protocol surface, or review loop phase
- prefer shared fixtures/helpers over giant assertion files
- keep test naming aligned to current scenario intent

## Project Inventory

### IntelligenceX repo code

- 1575 LOC - `IntelligenceX/Visualization/Heatmaps/UsageTelemetryOverviewBuilder.cs`
- 1255 LOC - `IntelligenceX/Telemetry/Usage/SqliteUsageTelemetryStores.cs`
- 1029 LOC - `IntelligenceX/Tools/ToolSelectionMetadata.cs`
- 745 LOC - `IntelligenceX/Tools/ToolRegistry.cs`

### IntelligenceX.Chat repo code

- 3826 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.21.core.visuals.js`
- 3422 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.15.core.tools.js`
- 2257 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.10.core.js`
- 1562 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/Ui/Shell.20.bindings.js`
- 1516 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ProfilesAndModels.cs`
- 1222 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.WorkingMemoryCheckpoint.cs`
- 1159 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.StartupFlow.cs`
- 1122 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.IntelligenceLoop.PhaseProgressAndFallback.cs`
- 1091 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.cs`
- 1061 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.RoutingHelpers.cs`
- 1051 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.RequestFlow.cs`
- 1003 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolEvidenceCache.cs`
- 998 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.Secondary.cs`
- 898 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.TurnQueue.cs`
- 891 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolExecution.RuntimeAndRecovery.cs`
- 878 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.Connection.cs`
- 875 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.cs`
- 871 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Profiles/SqliteServiceProfileStore.cs`
- 850 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/ToolOrchestrationCatalog.cs`
- 805 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Host/Program.Options.cs`
- 804 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tooling/ToolPackBootstrap.RegistryAndReflection.cs`
- 799 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ToolRouting.DomainIntentAffinity.cs`
- 770 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.Messaging.ServiceMessages.cs`
- 758 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Service/ServiceOptions.cs`
- 751 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Host/Program.Session.ScenarioContractParsing.cs`
- 722 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.cs`
### IntelligenceX.Chat tests

- 4301 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.IntelligenceLoop.EndToEnd.cs`
- 1990 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServicePlannerPromptTests.cs`
- 1258 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceDomainAffinityTests.cs`
- 1231 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.ToolBatchRecovery.cs`
- 1219 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.IntelligenceLoop.cs`
- 1147 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceToolingBootstrapTests.cs`
- 1097 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/PluginFolderLoaderTests.cs`
- 997 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRetryPolicyTests.cs`
- 956 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ToolCapabilityParityInventoryBuilderTests.cs`
- 934 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/MainWindowStartupConnectTimeoutPolicyTests.cs`
- 919 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ToolPackBootstrapMetadataTests.cs`
- 812 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.App.Tests/UiShellAssetsTests.cs`
- 807 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/HostNoToolRetryHeuristicsTests.ToolFallbacks.cs`
- 773 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/HostScenarioAssertionTests.cs`
- 766 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ToolOrchestrationCatalogTests.cs`
- 752 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.IntelligenceLoop.VisualContracts.cs`
- 749 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceRoutingTrimTests.ToolNudge.ExecutionContract.Carryover.cs`
- 728 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/ChatServiceToolEvidenceCacheTests.cs`
- 726 LOC - `IntelligenceX.Chat/IntelligenceX.Chat.Tests/HostNoToolRetryHeuristicsTests.cs`
### IntelligenceX.Cli repo code

- 1708 LOC - `IntelligenceX.Cli/Telemetry/UsageTelemetryCliRunner.cs`
- 927 LOC - `IntelligenceX.Cli/GitHub/GitHubOverviewSectionProjector.cs`

### IntelligenceX.Tools repo code and tests

- 949 LOC - `IntelligenceX.Tools/IntelligenceX.Tools.Common/ToolPackGuidance.cs`
- 754 LOC - `IntelligenceX.Tools/IntelligenceX.Tools.Common/ToolPackGuidance.NormalizationHelpers.cs`
- 733 LOC - `IntelligenceX.Tools/IntelligenceX.Tools.ADPlayground/AdMonitoringProbeRunTool.Helpers.cs`
- 2492 LOC - `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolDefinitionContractTests.cs`
- 838 LOC - `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolPackInfoContractTests.cs`
- 804 LOC - `IntelligenceX.Tools/IntelligenceX.Tools.Tests/ToolPackGuidanceTests.cs`

### IntelligenceX.Tests

- 812 LOC - `IntelligenceX.Tests/Program.Telemetry.Usage.Overview.cs`
- 726 LOC - `IntelligenceX.Tests/Program.Core.cs`
## Practical Next Step

Next safest follow-up candidates after the completed splits in this branch:
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.UiState.cs` - 710
- `IntelligenceX.Chat/IntelligenceX.Chat.Service/ChatServiceSession.ChatRouting.IntelligenceLoop.VisualCatalog.cs` - 711
- `IntelligenceX.Chat/IntelligenceX.Chat.App/MainWindow.PersistenceHelpers.cs` - 714
- `IntelligenceX.Reviewer/ReviewSettings.Environment.cs` - 717
- `IntelligenceX.Reviewer/GitHubClient.cs` - 718

If the goal is maximum risk reduction per branch, continue with the smallest over-threshold file that already has an obvious scenario or helper boundary before taking on the larger chat service and shell clusters.

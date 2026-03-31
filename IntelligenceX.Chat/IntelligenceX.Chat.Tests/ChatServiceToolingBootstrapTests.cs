using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceToolingBootstrapTests {
    private static string CreateToolingBootstrapCachePath(string namePrefix) {
        return TempPathTestHelper.CreateTempFilePath("ix-chat-" + namePrefix, ".json");
    }

    private static (string PreviewCacheKey, string CacheKey) BuildToolingBootstrapKeys(ServiceOptions options) {
        var runtimePolicyOptionsMethod = typeof(ChatServiceSession).GetMethod(
            "BuildRuntimePolicyOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var previewCacheKeyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapPreviewCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        var cacheKeyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(runtimePolicyOptionsMethod);
        Assert.NotNull(previewCacheKeyMethod);
        Assert.NotNull(cacheKeyMethod);

        var runtimePolicyOptions = Assert.IsType<ToolRuntimePolicyOptions>(runtimePolicyOptionsMethod!.Invoke(
            null,
            new object[] { options }));
        var resolvedRuntimePolicyOptions = ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions);
        var previewCacheKey = Assert.IsType<string>(previewCacheKeyMethod!.Invoke(
            null,
            new object?[] { options, runtimePolicyOptions, resolvedRuntimePolicyOptions }));
        var cacheKey = Assert.IsType<string>(cacheKeyMethod!.Invoke(
            null,
            new object?[] { options, runtimePolicyOptions, resolvedRuntimePolicyOptions }));
        return (previewCacheKey, cacheKey);
    }

    private static string BuildToolingBootstrapPreviewFingerprint(ServiceOptions options) {
        var runtimePolicyOptionsMethod = typeof(ChatServiceSession).GetMethod(
            "BuildRuntimePolicyOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var previewFingerprintMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapPreviewFingerprint",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(runtimePolicyOptionsMethod);
        Assert.NotNull(previewFingerprintMethod);

        var runtimePolicyOptions = Assert.IsType<ToolRuntimePolicyOptions>(runtimePolicyOptionsMethod!.Invoke(
            null,
            new object[] { options }));
        return Assert.IsType<string>(previewFingerprintMethod!.Invoke(
            null,
            new object?[] { options, runtimePolicyOptions }));
    }

    [Fact]
    public void RebuildToolingFromOptions_RefreshesPackAvailabilitySnapshot() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var packAvailabilityField = typeof(ChatServiceSession).GetField("_packAvailability", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(packAvailabilityField);

        var options = new ServiceOptions();
        var session = new ChatServiceSession(options, Stream.Null);

        var initialAvailability = Assert.IsType<ToolPackAvailabilityInfo[]>(packAvailabilityField!.GetValue(session));

        options.DisabledPackIds.Add("active_directory");
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        var rebuiltAvailability = Assert.IsType<ToolPackAvailabilityInfo[]>(packAvailabilityField.GetValue(session));
        Assert.NotSame(initialAvailability, rebuiltAvailability);

        var activeDirectory = Assert.Single(rebuiltAvailability, static item =>
            string.Equals(item.Id, "active_directory", StringComparison.OrdinalIgnoreCase));
        Assert.False(activeDirectory.Enabled);
    }

    [Fact]
    public void RebuildToolingFromOptions_AllowsToollessMode_WhenPluginOnlyModeLoadsNoPacks() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupWarningsField = typeof(ChatServiceSession).GetField("_startupWarnings", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField("_cachedToolDefinitions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(startupWarningsField);
        Assert.NotNull(cachedToolDefinitionsField);

        var options = new ServiceOptions {
            EnableBuiltInPackLoading = false,
            EnableDefaultPluginPaths = false
        };
        var session = new ChatServiceSession(options, Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        var warnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(session));
        Assert.Contains(
            warnings,
            static warning => warning.Contains("no_tool_packs_loaded", StringComparison.OrdinalIgnoreCase));

        var toolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField!.GetValue(session));
        Assert.Empty(toolDefinitions);
    }

    [Fact]
    public void RebuildToolingFromOptions_DoesNotEmitNoToolPacksWarning_WhenBuiltInPacksAreEnabled() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupWarningsField = typeof(ChatServiceSession).GetField("_startupWarnings", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(startupWarningsField);

        var options = new ServiceOptions {
            EnableBuiltInPackLoading = true,
            EnableDefaultPluginPaths = false
        };
        var session = new ChatServiceSession(options, Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        var warnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(session));
        Assert.DoesNotContain(
            warnings,
            static warning => warning.Contains("no_tool_packs_loaded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteToolAsyncForTesting_BootstrapsMissingToolOnDemand() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var call = new ToolCall(
            callId: "call-system-pack-info",
            name: "system_pack_info",
            input: "{}",
            arguments: new JsonObject(),
            raw: new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting(
            threadId: string.Empty,
            userRequest: string.Empty,
            call: call,
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);

        Assert.True(output.Ok is true, output.Output);
        Assert.NotEqual("tool_not_registered", output.ErrorCode);

        var capabilitySnapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
        Assert.Contains("system", capabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.True(capabilitySnapshot.RegisteredTools > 0);
    }

    [Fact]
    public async Task ExecuteToolAsyncForTesting_ActivatesKnownPackFromCachedToolDefinitionsWithoutStartingFullBootstrap() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "System pack info.",
                PackId = "system"
            }
        });

        var blockedStartupTask = Task.FromException(new InvalidOperationException("full bootstrap should not run"));
        session.SetStartupToolingBootstrapTaskForTesting(blockedStartupTask);

        var call = new ToolCall(
            callId: "call-system-pack-info-targeted",
            name: "system_pack_info",
            input: "{}",
            arguments: new JsonObject(),
            raw: new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting(
            threadId: string.Empty,
            userRequest: string.Empty,
            call: call,
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);

        Assert.True(output.Ok is true, output.Output);
        Assert.Same(blockedStartupTask, session.GetStartupToolingBootstrapTaskForTesting());

        var capabilitySnapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
        Assert.Contains("system", capabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system", capabilitySnapshot.ToolingSnapshot!.Packs.Select(static pack => pack.Id), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteToolAsyncForTesting_OnDemandActivationPreservesFreshPendingActionsContext() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "System pack info.",
                PackId = "system"
            }
        });

        var blockedStartupTask = Task.FromException(new InvalidOperationException("full bootstrap should not run"));
        session.SetStartupToolingBootstrapTaskForTesting(blockedStartupTask);

        const string threadId = "thread-preserve-pending-actions-activation";
        session.RememberPendingActionsForTesting(
            threadId,
            """
            [Action]
            ix:action:v1
            id: act_preserve_pending
            title: Run system pack info
            request: Run system pack info.
            mutating: false
            reply: /act act_preserve_pending
            """);
        Assert.True(session.HasFreshPendingActionsContextForTesting(threadId));

        var call = new ToolCall(
            callId: "call-system-pack-info-preserve-pending",
            name: "system_pack_info",
            input: "{}",
            arguments: new JsonObject(),
            raw: new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting(
            threadId: threadId,
            userRequest: "continue",
            call: call,
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);

        Assert.True(output.Ok is true, output.Output);
        Assert.True(session.HasFreshPendingActionsContextForTesting(threadId));
        Assert.Same(blockedStartupTask, session.GetStartupToolingBootstrapTaskForTesting());
    }

    [Fact]
    public async Task ExecuteToolAsyncForTesting_OnDemandActivationPreservesBackgroundWorkSnapshot() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "system_pack_info",
                Description = "System pack info.",
                PackId = "system"
            }
        });

        var blockedStartupTask = Task.FromException(new InvalidOperationException("full bootstrap should not run"));
        session.SetStartupToolingBootstrapTaskForTesting(blockedStartupTask);

        const string threadId = "thread-preserve-background-work-activation";
        var definitions = new[] {
            new ToolDefinition(
                name: "seed_system_probe_followup",
                description: "Seed a system probe follow-up.",
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetToolName = "system_pack_info",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                            FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "target",
                                    TargetArgument = "target"
                                }
                            }
                        }
                    }
                })
        };

        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
        session.RememberToolHandoffBackgroundWorkForTesting(
            threadId,
            definitions,
            new[] {
                new ToolCallDto {
                    CallId = "call-seed-system-probe-followup",
                    Name = "seed_system_probe_followup",
                    ArgumentsJson = """{"target":"srv-preserve.contoso.com"}"""
                }
            },
            new[] {
                new ToolOutputDto {
                    CallId = "call-seed-system-probe-followup",
                    Ok = true,
                    Output = """{"ok":true}"""
                }
            });

        var initialSnapshot = session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId);
        Assert.Contains(
            initialSnapshot.Items,
            static item => string.Equals(item.TargetToolName, "system_pack_info", StringComparison.OrdinalIgnoreCase));

        var call = new ToolCall(
            callId: "call-system-pack-info-preserve-background",
            name: "system_pack_info",
            input: "{}",
            arguments: new JsonObject(),
            raw: new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting(
            threadId: threadId,
            userRequest: "continue",
            call: call,
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);

        Assert.True(output.Ok is true, output.Output);
        Assert.Contains(
            session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
            static item => string.Equals(item.TargetToolName, "system_pack_info", StringComparison.OrdinalIgnoreCase));
        Assert.Same(blockedStartupTask, session.GetStartupToolingBootstrapTaskForTesting());
    }

    [Fact]
    public async Task ExecuteToolAsyncForTesting_ReturnsStructuredFailure_WhenOnDemandBootstrapFails() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        session.SetStartupToolingBootstrapTaskForTesting(Task.FromException(new InvalidOperationException("synthetic bootstrap failure")));
        var call = new ToolCall(
            callId: "call-system-pack-info-failure",
            name: "system_pack_info",
            input: "{}",
            arguments: new JsonObject(),
            raw: new JsonObject());

        var output = await session.ExecuteToolAsyncForTesting(
            threadId: string.Empty,
            userRequest: string.Empty,
            call: call,
            toolTimeoutSeconds: 10,
            cancellationToken: CancellationToken.None);

        Assert.False(output.Ok ?? false);
        Assert.Equal("tool_bootstrap_failed", output.ErrorCode);
        Assert.Contains("synthetic bootstrap failure", output.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RebuildToolingFromOptions_CapturesStartupBootstrapPhases() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupBootstrapField = typeof(ChatServiceSession).GetField("_startupBootstrap", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(startupBootstrapField);

        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());
        var startupBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField!.GetValue(session));

        Assert.Equal(5, startupBootstrap.Phases.Length);
        Assert.Equal(StartupBootstrapContracts.PhaseRuntimePolicyId, startupBootstrap.Phases[0].Id);
        Assert.Equal(StartupBootstrapContracts.PhaseDescriptorDiscoveryId, startupBootstrap.Phases[2].Id);
        Assert.Equal(StartupBootstrapContracts.PhasePackActivationId, startupBootstrap.Phases[3].Id);
        Assert.Equal(StartupBootstrapContracts.PhaseRegistryActivationFinalizeId, startupBootstrap.Phases[4].Id);
        Assert.True(startupBootstrap.Phases[2].DurationMs >= 1);
        Assert.False(string.IsNullOrWhiteSpace(startupBootstrap.SlowestPhaseId));
        Assert.True(startupBootstrap.SlowestPhaseMs >= 1);
    }

    [Fact]
    public void RebuildToolingFromOptions_ProjectsExecutionScopeAndContractMetadataIntoToolDefinitions() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField("_cachedToolDefinitions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(cachedToolDefinitionsField);

        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        var toolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField!.GetValue(session));
        Assert.Contains(
            toolDefinitions,
            static item =>
                item.SupportsRemoteHostTargeting
                && string.Equals(item.ExecutionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase)
                && item.RemoteHostArguments.Length > 0);

        var timeline = Assert.Single(toolDefinitions, static item =>
            string.Equals(item.Name, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase));
        Assert.True(timeline.IsExecutionAware);
        Assert.Equal(ToolExecutionContract.DefaultContractId, timeline.ExecutionContractId);
        Assert.True(timeline.SupportsLocalExecution);
        Assert.True(timeline.SupportsRemoteExecution);
        Assert.True(timeline.IsSetupAware);
        Assert.Equal("eventlog_connectivity_probe", timeline.SetupToolName);
        Assert.True(timeline.IsHandoffAware);
        Assert.Contains("system", timeline.HandoffTargetPackIds, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_info", timeline.HandoffTargetToolNames, StringComparer.OrdinalIgnoreCase);
        Assert.True(timeline.IsRecoveryAware);
        Assert.Contains("eventlog_channels_list", timeline.RecoveryToolNames, StringComparer.OrdinalIgnoreCase);

        var adPackInfo = Assert.Single(toolDefinitions, static item =>
            string.Equals(item.Name, "ad_pack_info", StringComparison.OrdinalIgnoreCase));
        Assert.True(adPackInfo.IsPackInfoTool);
        Assert.False(adPackInfo.IsEnvironmentDiscoverTool);

        var adEnvironmentDiscover = Assert.Single(toolDefinitions, static item =>
            string.Equals(item.Name, "ad_environment_discover", StringComparison.OrdinalIgnoreCase));
        Assert.False(adEnvironmentDiscover.IsPackInfoTool);
        Assert.True(adEnvironmentDiscover.IsEnvironmentDiscoverTool);
    }

    [Fact]
    public async Task HandleListToolsAsync_EmitsRoutingCatalogDiagnosticsAlongsideToolCatalog() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleListToolsMethod = typeof(ChatServiceSession).GetMethod("HandleListToolsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(handleListToolsMethod);

        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream) { AutoFlush = true };
        var task = Assert.IsAssignableFrom<Task>(handleListToolsMethod!.Invoke(session, new object?[] {
            writer,
            "req_list_tools",
            CancellationToken.None
        }));
        await task;

        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var json = await reader.ReadToEndAsync();
        var parsed = JsonSerializer.Deserialize<ChatServiceMessage>(json, ChatServiceJsonContext.Default.ChatServiceMessage);
        var message = Assert.IsType<ToolListMessage>(parsed);
        var routingCatalog = Assert.IsType<SessionRoutingCatalogDiagnosticsDto>(message.RoutingCatalog);
        var capabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(message.CapabilitySnapshot);
        Assert.NotEmpty(message.Packs);
        Assert.NotEmpty(message.Plugins);
        Assert.Contains(message.Packs, static pack => pack.AutonomySummary is not null);
        Assert.Contains(message.Plugins, static plugin => plugin.PackIds.Length > 0);

        Assert.Equal(message.Tools.Length, routingCatalog.TotalTools);
        Assert.True(capabilitySnapshot.ToolingAvailable);
        Assert.True(capabilitySnapshot.RegisteredTools >= message.Tools.Length);
        Assert.NotEmpty(capabilitySnapshot.EnabledPackIds);
        Assert.True(routingCatalog.RemoteCapableTools > 0);
        Assert.True(routingCatalog.CrossPackHandoffTools > 0);
        Assert.NotEmpty(routingCatalog.AutonomyReadinessHighlights);
    }

    [Fact]
    public async Task HandleListToolsAsync_UsesDeferredDescriptorPreviewToolDefinitionsForManifestOnlyPlugins() {
        var handleListToolsMethod = typeof(ChatServiceSession).GetMethod("HandleListToolsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(handleListToolsMethod);

        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-list-tools-deferred-preview");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "packIds": ["ops_inventory"],
              "sourceKind": "closed_source",
              "entryAssembly": "Ops.Bundle.dll",
              "entryType": "Ops.Bundle.PluginPack",
              "tools": [
                {
                  "name": "ops_inventory_query",
                  "description": "Query inventory from deferred manifest metadata.",
                  "packId": "ops_inventory",
                  "supportsRemoteExecution": true,
                  "supportsRemoteHostTargeting": true
                }
              ]
            }
            """);

            var options = new ServiceOptions {
                EnableDefaultPluginPaths = false
            };
            options.RuntimePluginPaths.Add(pluginRoot);
            var session = new ChatServiceSession(options, Stream.Null);

            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream) { AutoFlush = true };
            var task = Assert.IsAssignableFrom<Task>(handleListToolsMethod!.Invoke(session, new object?[] {
                writer,
                "req_list_tools_deferred_preview",
                CancellationToken.None
            }));
            await task;

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
            var json = await reader.ReadToEndAsync();
            var parsed = JsonSerializer.Deserialize<ChatServiceMessage>(json, ChatServiceJsonContext.Default.ChatServiceMessage);
            var message = Assert.IsType<ToolListMessage>(parsed);
            var tool = Assert.Single(message.Tools, static tool => string.Equals(tool.Name, "ops_inventory_query", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("ops_inventory", tool.PackId);
            Assert.True(tool.SupportsRemoteExecution);
            Assert.True(tool.SupportsRemoteHostTargeting);

            var capabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(message.CapabilitySnapshot);
            Assert.Equal("deferred_descriptor_preview", capabilitySnapshot.ToolingSnapshot?.Source);
            Assert.Contains(message.Plugins, static plugin => string.Equals(plugin.Id, "ops_bundle", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void DeferredPreviewToolDefinitions_MarkToolSeekingChatAsBootstrapSensitive() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-deferred-preview-routing");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "packIds": ["ops_inventory"],
              "sourceKind": "closed_source",
              "entryAssembly": "Ops.Bundle.dll",
              "entryType": "Ops.Bundle.PluginPack",
              "tools": [
                {
                  "name": "ops_inventory_query",
                  "description": "Query remote host inventory and diagnostics.",
                  "packId": "ops_inventory",
                  "tags": ["inventory", "diagnostics", "remote"],
                  "supportsRemoteExecution": true,
                  "supportsRemoteHostTargeting": true,
                  "representativeExamples": ["Check remote inventory for srv-01"]
                }
              ]
            }
            """);

            var options = new ServiceOptions {
                EnableDefaultPluginPaths = false
            };
            options.RuntimePluginPaths.Add(pluginRoot);
            var session = new ChatServiceSession(options, Stream.Null);

            Assert.True(session.HasDeferredToolCandidateMatchForTesting("Check remote inventory for srv-01"));
            Assert.False(session.HasDeferredToolCandidateMatchForTesting("Write me a haiku about spring"));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HandleToolHealthAsync_UsesSameContractBackedPackInfoSurfaceAsExportedToolCatalog() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleListToolsMethod = typeof(ChatServiceSession).GetMethod("HandleListToolsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleToolHealthMethod = typeof(ChatServiceSession).GetMethod("HandleToolHealthAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(handleListToolsMethod);
        Assert.NotNull(handleToolHealthMethod);

        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        ToolListMessage toolListMessage;
        using (var toolsStream = new MemoryStream())
        using (var toolsWriter = new StreamWriter(toolsStream) { AutoFlush = true }) {
            var listTask = Assert.IsAssignableFrom<Task>(handleListToolsMethod!.Invoke(session, new object?[] {
                toolsWriter,
                "req_list_tools_for_health_parity",
                CancellationToken.None
            }));
            await listTask;

            toolsStream.Position = 0;
            using var toolsReader = new StreamReader(toolsStream);
            var toolsJson = await toolsReader.ReadToEndAsync();
            var parsedTools = JsonSerializer.Deserialize<ChatServiceMessage>(toolsJson, ChatServiceJsonContext.Default.ChatServiceMessage);
            toolListMessage = Assert.IsType<ToolListMessage>(parsedTools);
        }

        ToolHealthMessage toolHealthMessage;
        using (var healthStream = new MemoryStream())
        using (var healthWriter = new StreamWriter(healthStream) { AutoFlush = true }) {
            var healthTask = Assert.IsAssignableFrom<Task>(handleToolHealthMethod!.Invoke(session, new object?[] {
                healthWriter,
                new CheckToolHealthRequest {
                    RequestId = "req_tool_health_parity"
                },
                CancellationToken.None
            }));
            await healthTask;

            healthStream.Position = 0;
            using var healthReader = new StreamReader(healthStream);
            var healthJson = await healthReader.ReadToEndAsync();
            var parsedHealth = JsonSerializer.Deserialize<ChatServiceMessage>(healthJson, ChatServiceJsonContext.Default.ChatServiceMessage);
            toolHealthMessage = Assert.IsType<ToolHealthMessage>(parsedHealth);
        }

        var exportedPackInfoTools = toolListMessage.Tools
            .Where(static tool => tool.IsPackInfoTool)
            .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exportedEnvironmentDiscoverTools = toolListMessage.Tools
            .Where(static tool => tool.IsEnvironmentDiscoverTool)
            .Select(static tool => tool.Name)
            .ToArray();
        var exportedToolsByName = toolListMessage.Tools.ToDictionary(static tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        var exportedPacksById = toolListMessage.Packs.ToDictionary(static pack => pack.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(exportedPackInfoTools.Length, toolHealthMessage.Probes.Length);
        Assert.Equal(toolHealthMessage.Probes.Length, toolHealthMessage.OkCount + toolHealthMessage.FailedCount);

        foreach (var probe in toolHealthMessage.Probes) {
            Assert.True(exportedToolsByName.TryGetValue(probe.ToolName, out var exportedTool));
            Assert.True(exportedTool.IsPackInfoTool);
            Assert.False(exportedTool.IsEnvironmentDiscoverTool);
            Assert.DoesNotContain(probe.ToolName, exportedEnvironmentDiscoverTools, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(exportedTool.PackId, probe.PackId);

            if (!string.IsNullOrWhiteSpace(probe.PackId) && exportedPacksById.TryGetValue(probe.PackId, out var exportedPack)) {
                Assert.Equal(exportedPack.Name, probe.PackName);
            }
        }
    }

    [Fact]
    public async Task GetToolHealthProbeCatalog_AppliesSourceAndPackFiltersWithoutLeavingPackInfoSurface() {
        var rebuildMethod = typeof(ChatServiceSession).GetMethod("RebuildToolingFromOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        var handleListToolsMethod = typeof(ChatServiceSession).GetMethod("HandleListToolsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildMethod);
        Assert.NotNull(handleListToolsMethod);

        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        rebuildMethod!.Invoke(session, Array.Empty<object>());

        ToolListMessage toolListMessage;
        using (var toolsStream = new MemoryStream())
        using (var toolsWriter = new StreamWriter(toolsStream) { AutoFlush = true }) {
            var listTask = Assert.IsAssignableFrom<Task>(handleListToolsMethod!.Invoke(session, new object?[] {
                toolsWriter,
                "req_list_tools_for_probe_catalog_filters",
                CancellationToken.None
            }));
            await listTask;

            toolsStream.Position = 0;
            using var toolsReader = new StreamReader(toolsStream);
            var toolsJson = await toolsReader.ReadToEndAsync();
            var parsedTools = JsonSerializer.Deserialize<ChatServiceMessage>(toolsJson, ChatServiceJsonContext.Default.ChatServiceMessage);
            toolListMessage = Assert.IsType<ToolListMessage>(parsedTools);
        }

        var filteredCatalog = session.GetToolHealthProbeCatalog(
            new[] { ToolPackSourceKind.ClosedSource },
            new[] { "active_directory", "eventlog" });
        Assert.NotEmpty(filteredCatalog);

        var exportedToolsByName = toolListMessage.Tools.ToDictionary(static tool => tool.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in filteredCatalog) {
            Assert.Equal(ToolPackSourceKind.ClosedSource, entry.SourceKind);
            Assert.Equal("active_directory", entry.PackId);
            Assert.True(exportedToolsByName.TryGetValue(entry.ToolName, out var exportedTool));
            Assert.True(exportedTool.IsPackInfoTool);
            Assert.False(exportedTool.IsEnvironmentDiscoverTool);
            Assert.Equal(exportedTool.PackId, entry.PackId);
            Assert.Equal(exportedTool.PackName, entry.PackName);
        }
    }

    [Fact]
    public void Constructor_UsesSharedToolingBootstrapCache_WhenProvided() {
        var rebuildCoreMethod = typeof(ChatServiceSession).GetMethod(
            "RebuildToolingCore",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var startupBootstrapField = typeof(ChatServiceSession).GetField("_startupBootstrap", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupWarningsField = typeof(ChatServiceSession).GetField("_startupWarnings", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildCoreMethod);
        Assert.NotNull(startupBootstrapField);
        Assert.NotNull(startupWarningsField);

        var cachePath = CreateToolingBootstrapCachePath("tooling-cache");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var cache = new ChatServiceToolingBootstrapCache(cachePath);

            var firstSession = new ChatServiceSession(new ServiceOptions(), Stream.Null, cache);
            rebuildCoreMethod!.Invoke(firstSession, new object?[] { false });
            var firstBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField!.GetValue(firstSession));
            Assert.NotEmpty(firstBootstrap.Phases);
            Assert.NotEqual(StartupBootstrapContracts.PhaseCacheHitId, firstBootstrap.Phases[0].Id);

            var secondSession = new ChatServiceSession(new ServiceOptions(), Stream.Null, cache);
            var secondPreviewWarnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(secondSession));
            Assert.Contains(
                secondPreviewWarnings,
                static warning => warning.Contains("persisted cache", StringComparison.OrdinalIgnoreCase));

            rebuildCoreMethod.Invoke(secondSession, new object?[] { false });
            var secondBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField.GetValue(secondSession));
            Assert.Single(secondBootstrap.Phases);
            Assert.Equal(StartupBootstrapContracts.PhaseCacheHitId, secondBootstrap.Phases[0].Id);

            var secondWarnings = Assert.IsType<string[]>(startupWarningsField.GetValue(secondSession));
            Assert.Contains(secondWarnings, static warning => warning.Contains("tooling bootstrap cache hit", StringComparison.OrdinalIgnoreCase));
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void RebuildToolingCore_UsesPreviewKeyCacheHit_WhenPersistedPreviewWasRestored() {
        var rebuildCoreMethod = typeof(ChatServiceSession).GetMethod(
            "RebuildToolingCore",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var startupBootstrapField = typeof(ChatServiceSession).GetField("_startupBootstrap", BindingFlags.NonPublic | BindingFlags.Instance);
        var startupWarningsField = typeof(ChatServiceSession).GetField("_startupWarnings", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField("_cachedToolDefinitions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(rebuildCoreMethod);
        Assert.NotNull(startupBootstrapField);
        Assert.NotNull(startupWarningsField);
        Assert.NotNull(cachedToolDefinitionsField);

        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-preview-cache-hit");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var options = new ServiceOptions();
            var (previewCacheKey, cacheKey) = BuildToolingBootstrapKeys(options);
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "preview_cache_tool",
                            Description = "Preview cache hit tool",
                            PackId = "preview_pack"
                        }
                    },
                    PackSummaries = new[] {
                        new ToolPackInfoDto {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.ClosedSource
                        }
                    },
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto {
                        Tools = 1,
                        PacksLoaded = 1
                    },
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "preview_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "preview_cache_tool" }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            Assert.NotEqual(previewCacheKey, cacheKey);
            Assert.False(cache.TryGetSnapshot(cacheKey, out _));
            Assert.True(cache.TryGetSnapshotByPreviewCacheKey(previewCacheKey, out _));

            var session = new ChatServiceSession(options, Stream.Null, cache);
            var previewToolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField!.GetValue(session));
            Assert.Equal("preview_cache_tool", Assert.Single(previewToolDefinitions).Name);

            rebuildCoreMethod!.Invoke(session, new object?[] { false });

            var startupBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField!.GetValue(session));
            Assert.Single(startupBootstrap.Phases);
            Assert.Equal(StartupBootstrapContracts.PhaseCacheHitId, startupBootstrap.Phases[0].Id);

            var startupWarnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(session));
            Assert.Contains(
                startupWarnings,
                static warning => warning.Contains("tooling bootstrap cache hit", StringComparison.OrdinalIgnoreCase));

            var cachedToolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField.GetValue(session));
            Assert.Equal("preview_cache_tool", Assert.Single(cachedToolDefinitions).Name);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ToolingBootstrapCache_PersistsSnapshotToDisk_AndRestoresToolDefinitions() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-roundtrip");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            const string cacheKey = "unit-test-cache-key";
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                cacheKey,
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "unit_test_tool",
                            Description = "unit test tool definition"
                        }
                    },
                    PackSummaries = Array.Empty<ToolPackInfoDto>(),
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = Array.Empty<ToolPackAvailabilityInfo>(),
                    PluginAvailability = new[] {
                        new ToolPluginAvailabilityInfo {
                            Id = "plugin_loader_test",
                            Name = "Plugin Loader Test",
                            Origin = "plugin_folder",
                            SourceKind = "open_source",
                            DefaultEnabled = true,
                            Enabled = true,
                            PackIds = new[] { "plugin_loader_test" },
                            SkillDirectories = new[] { "C:\\plugins\\plugin-loader-test\\skills" },
                            SkillIds = new[] { "inventory-test", "network-recon" }
                        }
                    },
                    PluginCatalog = new[] {
                        new ToolPluginCatalogInfo {
                            Id = "plugin_loader_test",
                            Name = "Plugin Loader Test",
                            Version = "1.0.0",
                            Origin = "plugin_folder",
                            SourceKind = "open_source",
                            DefaultEnabled = true,
                            PackIds = new[] { "plugin_loader_test" },
                            RootPath = "C:\\plugins\\plugin-loader-test",
                            SkillDirectories = new[] { "C:\\plugins\\plugin-loader-test\\skills" },
                            SkillIds = new[] { "inventory-test", "network-recon" }
                        }
                    },
                    StartupWarnings = new[] { "[startup] unit-test warning" },
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 0,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = Array.Empty<string>(),
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "unit_test_tool" }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            var reloaded = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.True(reloaded.TryGetPersistedSnapshot(cacheKey, out var persistedSnapshot));
            var toolDefinition = Assert.Single(persistedSnapshot.ToolDefinitions);
            Assert.Equal("unit_test_tool", toolDefinition.Name);
            Assert.Equal("unit test tool definition", toolDefinition.Description);
            Assert.True(reloaded.TryGetPersistedPreviewSnapshot(cacheKey, out var previewSnapshot));
            Assert.Equal("unit_test_tool", Assert.Single(previewSnapshot.ToolDefinitions).Name);
            var pluginAvailability = Assert.Single(persistedSnapshot.PluginAvailability);
            Assert.Equal("plugin_loader_test", pluginAvailability.Id);
            Assert.Equal(new[] { "inventory-test", "network-recon" }, pluginAvailability.SkillIds);
            var pluginCatalog = Assert.Single(persistedSnapshot.PluginCatalog);
            Assert.Equal("plugin_loader_test", pluginCatalog.Id);
            Assert.Equal("1.0.0", pluginCatalog.Version);
            Assert.Equal("C:\\plugins\\plugin-loader-test", pluginCatalog.RootPath);
            Assert.Equal(new[] { "inventory-test", "network-recon" }, pluginCatalog.SkillIds);
            Assert.Equal("unit_test_tool", Assert.Single(persistedSnapshot.CapabilitySnapshot.HealthyTools));
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ToolingBootstrapCache_PersistedSnapshotJson_IncludesCapabilitySnapshot() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-capability-json");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            const string cacheKey = "unit-test-capability-json";
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                cacheKey,
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = Array.Empty<ToolDefinitionDto>(),
                    PackSummaries = Array.Empty<ToolPackInfoDto>(),
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = Array.Empty<ToolPackAvailabilityInfo>(),
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 0,
                        EnabledPackCount = 0,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = false,
                        AllowedRootCount = 0,
                        EnabledPackIds = Array.Empty<string>(),
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = Array.Empty<string>()
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            using var document = JsonDocument.Parse(File.ReadAllText(cachePath));
            Assert.True(document.RootElement.TryGetProperty("CapabilitySnapshot", out var capabilitySnapshot));
            Assert.False(capabilitySnapshot.GetProperty("ToolingAvailable").GetBoolean());
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ToolingBootstrapCache_LoadsLegacyPersistedSnapshot_AndDerivesPreviewKey() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-legacy-preview-key");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            const string legacyCacheKey = "ad_dc=dc01.contoso.com;write_mode=Disabled;discovery_fingerprint=legacy-fingerprint;";
            const string expectedPreviewCacheKey = "ad_dc=dc01.contoso.com;write_mode=Disabled;";
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var capabilitySnapshot = new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 0,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                EnabledPackIds = Array.Empty<string>(),
                EnabledPluginIds = Array.Empty<string>(),
                RoutingFamilies = Array.Empty<string>(),
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                Skills = Array.Empty<string>(),
                HealthyTools = new[] { "legacy_tool" }
            };
            var legacyPersistedSnapshot = new {
                SchemaVersion = 4,
                CacheKey = legacyCacheKey,
                CachedAtUtc = DateTime.UtcNow,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "legacy_tool",
                        Description = "Legacy preview tool"
                    }
                },
                PackSummaries = Array.Empty<ToolPackInfoDto>(),
                PackAvailability = Array.Empty<ToolPackAvailabilityInfo>(),
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = routingDiagnostics,
                CapabilitySnapshot = capabilitySnapshot
            };

            File.WriteAllText(cachePath, JsonSerializer.Serialize(legacyPersistedSnapshot));

            var reloaded = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.True(reloaded.TryGetPersistedPreviewSnapshot(expectedPreviewCacheKey, out var persistedSnapshot));
            Assert.Equal("legacy_tool", Assert.Single(persistedSnapshot.ToolDefinitions).Name);
            Assert.False(string.IsNullOrWhiteSpace(persistedSnapshot.PreviewDiscoveryFingerprint));
            Assert.False(reloaded.TryGetPersistedSnapshot(expectedPreviewCacheKey, out _));
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ToolingBootstrapCache_StoresVersionedDescriptorSnapshotContract() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-descriptor-snapshot-contract");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                "write_mode=Disabled;discovery_fingerprint=current;",
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "descriptor_preview_tool",
                            Description = "Descriptor-preview tool",
                            PackId = "preview_pack"
                        }
                    },
                    PackSummaries = new[] {
                        new ToolPackInfoDto {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.ClosedSource
                        }
                    },
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "preview_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "descriptor_preview_tool" }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            using var document = JsonDocument.Parse(File.ReadAllText(cachePath));
            Assert.True(document.RootElement.TryGetProperty("DescriptorSnapshot", out var descriptorSnapshot));
            Assert.Equal(1, descriptorSnapshot.GetProperty("SchemaVersion").GetInt32());
            Assert.Equal(
                "descriptor_preview_tool",
                descriptorSnapshot.GetProperty("ToolDefinitions")[0].GetProperty("Name").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                descriptorSnapshot.GetProperty("PreviewDiscoveryFingerprint").GetString()));
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ToolingBootstrapCache_LoadPrefersVersionedDescriptorSnapshotContract() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-descriptor-snapshot-preferred");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            const string previewCacheKey = "write_mode=Disabled;";
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var persistedSnapshot = new ChatServiceToolingBootstrapPersistedSnapshot {
                SchemaVersion = 4,
                CacheKey = previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                PreviewCacheKey = previewCacheKey,
                CachedAtUtc = DateTime.UtcNow,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "legacy_stale_tool",
                        Description = "Legacy stale tool",
                        PackId = "legacy_pack"
                    }
                },
                PackSummaries = new[] {
                    new ToolPackInfoDto {
                        Id = "legacy_pack",
                        Name = "Legacy Pack",
                        Tier = CapabilityTier.ReadOnly,
                        Enabled = true,
                        IsDangerous = false,
                        SourceKind = ToolPackSourceKind.ClosedSource
                    }
                },
                PackAvailability = new[] {
                    new ToolPackAvailabilityInfo {
                        Id = "legacy_pack",
                        Name = "Legacy Pack",
                        SourceKind = "closed_source",
                        Enabled = true
                    }
                },
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = routingDiagnostics,
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    EnabledPackIds = new[] { "legacy_pack" },
                    EnabledPluginIds = Array.Empty<string>(),
                    RoutingFamilies = Array.Empty<string>(),
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    Skills = Array.Empty<string>(),
                    HealthyTools = new[] { "legacy_stale_tool" }
                },
                DescriptorSnapshot = new ChatServiceToolingBootstrapDescriptorSnapshot {
                    SchemaVersion = 1,
                    PreviewDiscoveryFingerprint = "descriptor-current",
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "descriptor_current_tool",
                            Description = "Descriptor current tool",
                            PackId = "descriptor_pack"
                        }
                    },
                    PackSummaries = new[] {
                        new ToolPackInfoDto {
                            Id = "descriptor_pack",
                            Name = "Descriptor Pack",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.ClosedSource
                        }
                    },
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "descriptor_pack",
                            Name = "Descriptor Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "descriptor_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "descriptor_current_tool" }
                    }
                }
            };

            File.WriteAllText(cachePath, JsonSerializer.Serialize(persistedSnapshot));

            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.True(cache.TryGetPersistedPreviewSnapshot(previewCacheKey, out var previewSnapshot));
            Assert.Equal("descriptor_current_tool", Assert.Single(previewSnapshot.ToolDefinitions).Name);
            Assert.Equal("descriptor_pack", Assert.Single(previewSnapshot.PackSummaries).Id);
            Assert.Equal("descriptor_pack", Assert.Single(previewSnapshot.PackAvailability).Id);
            Assert.Equal("descriptor-current", previewSnapshot.PreviewDiscoveryFingerprint);
            Assert.Equal(new[] { "descriptor_pack" }, previewSnapshot.CapabilitySnapshot.EnabledPackIds);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public async Task HandleListToolsAsync_PersistedPreviewPreservesAutonomyRichToolAndPackMetadata() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-persisted-preview-autonomy");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var keyMethod = typeof(ChatServiceSession).GetMethod(
                "BuildToolingBootstrapCacheKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            var runtimePolicyOptionsMethod = typeof(ChatServiceSession).GetMethod(
                "BuildRuntimePolicyOptions",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(keyMethod);
            Assert.NotNull(runtimePolicyOptionsMethod);
            var options = new ServiceOptions();
            var runtimePolicyOptions = Assert.IsType<ToolRuntimePolicyOptions>(runtimePolicyOptionsMethod!.Invoke(
                null,
                new object[] { options }));
            var resolvedRuntimePolicyOptions = ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions);
            var cacheKey = Assert.IsType<string>(keyMethod!.Invoke(
                null,
                new object?[] { options, runtimePolicyOptions, resolvedRuntimePolicyOptions }));
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                cacheKey,
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "ad_environment_discover",
                            Description = "Discover AD environment context.",
                            PackId = "active_directory",
                            PackName = "ADPlayground",
                            PackSourceKind = ToolPackSourceKind.ClosedSource,
                            IsEnvironmentDiscoverTool = true,
                            RequiresAuthentication = true,
                            AuthenticationContractId = "ix.auth.runtime.v1",
                            AuthenticationArguments = new[] { "domain_controller" },
                            SupportsConnectivityProbe = true,
                            ProbeToolName = "ad_environment_discover",
                            ExecutionScope = "local_or_remote",
                            SupportsTargetScoping = true,
                            TargetScopeArguments = new[] { "domain_controller", "search_base_dn" },
                            SupportsRemoteHostTargeting = true,
                            RemoteHostArguments = new[] { "domain_controller" },
                            IsSetupAware = true,
                            SetupToolName = "ad_environment_discover",
                            IsHandoffAware = true,
                            HandoffTargetPackIds = new[] { "eventlog", "system" },
                            HandoffTargetToolNames = new[] { "eventlog_channels_list", "system_info" },
                            IsRecoveryAware = true,
                            SupportsTransientRetry = true,
                            MaxRetryAttempts = 1,
                            RecoveryToolNames = new[] { "ad_environment_discover" }
                        }
                    },
                    PackSummaries = new[] {
                        new ToolPackInfoDto {
                            Id = "active_directory",
                            Name = "Active Directory",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.ClosedSource,
                            AutonomySummary = new ToolPackAutonomySummaryDto {
                                TotalTools = 1,
                                RemoteCapableTools = 1,
                                RemoteCapableToolNames = new[] { "ad_environment_discover" },
                                TargetScopedTools = 1,
                                TargetScopedToolNames = new[] { "ad_environment_discover" },
                                RemoteHostTargetingTools = 1,
                                RemoteHostTargetingToolNames = new[] { "ad_environment_discover" },
                                SetupAwareTools = 1,
                                SetupAwareToolNames = new[] { "ad_environment_discover" },
                                EnvironmentDiscoverTools = 1,
                                EnvironmentDiscoverToolNames = new[] { "ad_environment_discover" },
                                HandoffAwareTools = 1,
                                HandoffAwareToolNames = new[] { "ad_environment_discover" },
                                RecoveryAwareTools = 1,
                                RecoveryAwareToolNames = new[] { "ad_environment_discover" },
                                AuthenticationRequiredTools = 1,
                                AuthenticationRequiredToolNames = new[] { "ad_environment_discover" },
                                ProbeCapableTools = 1,
                                ProbeCapableToolNames = new[] { "ad_environment_discover" },
                                CrossPackHandoffTools = 1,
                                CrossPackHandoffToolNames = new[] { "ad_environment_discover" },
                                CrossPackTargetPacks = new[] { "eventlog", "system" }
                            }
                        }
                    },
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "active_directory",
                            Name = "ADPlayground",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "active_directory" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "ad_environment_discover" },
                        RemoteReachabilityMode = "remote_capable",
                        Autonomy = new SessionCapabilityAutonomySummaryDto {
                            RemoteCapableToolCount = 1,
                            TargetScopedToolCount = 1,
                            RemoteHostTargetingToolCount = 1,
                            SetupAwareToolCount = 1,
                            EnvironmentDiscoverToolCount = 1,
                            HandoffAwareToolCount = 1,
                            RecoveryAwareToolCount = 1,
                            AuthenticationRequiredToolCount = 1,
                            ProbeCapableToolCount = 1,
                            CrossPackHandoffToolCount = 1,
                            RemoteCapablePackIds = new[] { "active_directory" },
                            TargetScopedPackIds = new[] { "active_directory" },
                            RemoteHostTargetingPackIds = new[] { "active_directory" },
                            EnvironmentDiscoverPackIds = new[] { "active_directory" },
                            AuthenticationRequiredPackIds = new[] { "active_directory" },
                            ProbeCapablePackIds = new[] { "active_directory" },
                            CrossPackReadyPackIds = new[] { "active_directory" },
                            CrossPackTargetPackIds = new[] { "eventlog", "system" }
                        }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            var session = new ChatServiceSession(options, Stream.Null, cache);
            var orchestrationCatalogField = typeof(ChatServiceSession).GetField("_toolOrchestrationCatalog", BindingFlags.NonPublic | BindingFlags.Instance);
            var startupToolingBootstrapTaskField = typeof(ChatServiceSession).GetField("_startupToolingBootstrapTask", BindingFlags.NonPublic | BindingFlags.Instance);
            var handleListToolsMethod = typeof(ChatServiceSession).GetMethod("HandleListToolsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(orchestrationCatalogField);
            Assert.NotNull(startupToolingBootstrapTaskField);
            Assert.NotNull(handleListToolsMethod);

            var previewCatalog = Assert.IsType<ToolOrchestrationCatalog>(orchestrationCatalogField!.GetValue(session));
            Assert.Equal(0, previewCatalog.Count);

            var previewCapabilitySnapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
            Assert.NotNull(previewCapabilitySnapshot.Autonomy);
            Assert.Equal(1, previewCapabilitySnapshot.Autonomy!.RemoteCapableToolCount);
            Assert.Equal(new[] { "eventlog", "system" }, previewCapabilitySnapshot.Autonomy.CrossPackTargetPackIds);

            var startupToolingBootstrapTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            startupToolingBootstrapTaskField!.SetValue(session, startupToolingBootstrapTcs.Task);

            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream) { AutoFlush = true };
            var task = Assert.IsAssignableFrom<Task>(handleListToolsMethod!.Invoke(session, new object?[] {
                writer,
                "req_list_tools_persisted_preview",
                CancellationToken.None
            }));
            await task;

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
            var json = await reader.ReadToEndAsync();
            var parsed = JsonSerializer.Deserialize<ChatServiceMessage>(json, ChatServiceJsonContext.Default.ChatServiceMessage);
            var message = Assert.IsType<ToolListMessage>(parsed);

            var tool = Assert.Single(message.Tools);
            Assert.Equal("ad_environment_discover", tool.Name);
            Assert.True(tool.IsEnvironmentDiscoverTool);
            Assert.True(tool.RequiresAuthentication);
            Assert.Equal("ix.auth.runtime.v1", tool.AuthenticationContractId);
            Assert.Equal(new[] { "domain_controller" }, tool.AuthenticationArguments);
            Assert.True(tool.SupportsConnectivityProbe);
            Assert.Equal("ad_environment_discover", tool.ProbeToolName);
            Assert.Equal("local_or_remote", tool.ExecutionScope);
            Assert.Equal(new[] { "domain_controller", "search_base_dn" }, tool.TargetScopeArguments);
            Assert.Equal(new[] { "domain_controller" }, tool.RemoteHostArguments);
            Assert.Equal("ad_environment_discover", tool.SetupToolName);
            Assert.Equal(new[] { "eventlog", "system" }, tool.HandoffTargetPackIds);
            Assert.Equal(new[] { "eventlog_channels_list", "system_info" }, tool.HandoffTargetToolNames);
            Assert.Equal(new[] { "ad_environment_discover" }, tool.RecoveryToolNames);

            var pack = Assert.Single(message.Packs);
            Assert.Equal("active_directory", pack.Id);
            var autonomySummary = Assert.IsType<ToolPackAutonomySummaryDto>(pack.AutonomySummary);
            Assert.Equal(1, autonomySummary.RemoteCapableTools);
            Assert.Equal(new[] { "ad_environment_discover" }, autonomySummary.RemoteCapableToolNames);
            Assert.Equal(1, autonomySummary.TargetScopedTools);
            Assert.Equal(1, autonomySummary.RemoteHostTargetingTools);
            Assert.Equal(1, autonomySummary.EnvironmentDiscoverTools);
            Assert.Equal(1, autonomySummary.AuthenticationRequiredTools);
            Assert.Equal(1, autonomySummary.ProbeCapableTools);
            Assert.Equal(new[] { "eventlog", "system" }, autonomySummary.CrossPackTargetPacks);

            var capabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(message.CapabilitySnapshot);
            Assert.Equal("remote_capable", capabilitySnapshot.RemoteReachabilityMode);
            Assert.NotNull(capabilitySnapshot.Autonomy);
            Assert.Equal(1, capabilitySnapshot.Autonomy!.RemoteCapableToolCount);
            Assert.Equal(1, capabilitySnapshot.Autonomy.TargetScopedToolCount);
            Assert.Equal(1, capabilitySnapshot.Autonomy.RemoteHostTargetingToolCount);
            Assert.Equal(1, capabilitySnapshot.Autonomy.EnvironmentDiscoverToolCount);
            Assert.Equal(1, capabilitySnapshot.Autonomy.AuthenticationRequiredToolCount);
            Assert.Equal(1, capabilitySnapshot.Autonomy.ProbeCapableToolCount);
            Assert.Equal(new[] { "eventlog", "system" }, capabilitySnapshot.Autonomy.CrossPackTargetPackIds);

            startupToolingBootstrapTcs.SetResult(null);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void Constructor_RestoresPersistedPreview_WhenOnlyPreviewCacheKeyMatches() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-preview-key-startup-restore");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var options = new ServiceOptions();
            var (previewCacheKey, cacheKey) = BuildToolingBootstrapKeys(options);
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var capabilitySnapshot = new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                EnabledPackIds = new[] { "preview_pack" },
                EnabledPluginIds = Array.Empty<string>(),
                RoutingFamilies = Array.Empty<string>(),
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                Skills = Array.Empty<string>(),
                HealthyTools = new[] { "preview_tool" }
            };
            var persistedSnapshot = new ChatServiceToolingBootstrapPersistedSnapshot {
                SchemaVersion = 4,
                CacheKey = previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                PreviewCacheKey = previewCacheKey,
                CachedAtUtc = DateTime.UtcNow,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "preview_tool",
                        Description = "Preview-only tool",
                        PackId = "preview_pack"
                    }
                },
                PackSummaries = new[] {
                    new ToolPackInfoDto {
                        Id = "preview_pack",
                        Name = "Preview Pack",
                        Tier = CapabilityTier.ReadOnly,
                        Enabled = true,
                        IsDangerous = false,
                        SourceKind = ToolPackSourceKind.ClosedSource
                    }
                },
                PackAvailability = new[] {
                    new ToolPackAvailabilityInfo {
                        Id = "preview_pack",
                        Name = "Preview Pack",
                        SourceKind = "closed_source",
                        Enabled = true
                    }
                },
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = routingDiagnostics,
                CapabilitySnapshot = capabilitySnapshot
            };

            File.WriteAllText(cachePath, JsonSerializer.Serialize(persistedSnapshot));

            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.False(cache.TryGetPersistedSnapshot(cacheKey, out _));
            Assert.True(cache.TryGetPersistedPreviewSnapshot(previewCacheKey, out _));

            var persistedPreviewFlagField = typeof(ChatServiceSession).GetField(
                "_servingPersistedToolingBootstrapPreview",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField(
                "_cachedToolDefinitions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var startupBootstrapField = typeof(ChatServiceSession).GetField(
                "_startupBootstrap",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(persistedPreviewFlagField);
            Assert.NotNull(cachedToolDefinitionsField);
            Assert.NotNull(startupBootstrapField);

            var session = new ChatServiceSession(options, Stream.Null, cache);

            Assert.True(Assert.IsType<bool>(persistedPreviewFlagField!.GetValue(session)));
            var toolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField!.GetValue(session));
            Assert.Equal("preview_tool", Assert.Single(toolDefinitions).Name);
            var startupBootstrap = Assert.IsType<SessionStartupBootstrapTelemetryDto>(startupBootstrapField!.GetValue(session));
            var previewPhase = Assert.Single(startupBootstrap.Phases);
            Assert.Equal(StartupBootstrapContracts.PhaseDescriptorCacheHitId, previewPhase.Id);
            Assert.Equal(StartupBootstrapContracts.PhaseDescriptorCacheHitLabel, previewPhase.Label);
            Assert.Equal(1, startupBootstrap.Tools);
            Assert.Equal(1, startupBootstrap.PacksLoaded);
            Assert.Equal(new[] { "preview_pack" }, session.BuildRuntimeCapabilitySnapshotForTesting().EnabledPackIds);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void TryGetPersistedPreviewSnapshot_RejectsMismatchedPreviewFingerprint() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-preview-fingerprint-mismatch");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            const string previewCacheKey = "ad_dc=dc01.contoso.com;write_mode=Disabled;";
            const string stalePreviewFingerprint = "stale-preview-fingerprint";
            const string currentPreviewFingerprint = "current-preview-fingerprint";
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());

            File.WriteAllText(
                cachePath,
                JsonSerializer.Serialize(new ChatServiceToolingBootstrapPersistedSnapshot {
                    SchemaVersion = 4,
                    CacheKey = previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                    PreviewCacheKey = previewCacheKey,
                    PreviewDiscoveryFingerprint = stalePreviewFingerprint,
                    CachedAtUtc = DateTime.UtcNow,
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "preview_tool",
                            Description = "Preview-only tool",
                            PackId = "preview_pack"
                        }
                    },
                    PackSummaries = Array.Empty<ToolPackInfoDto>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "preview_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "preview_tool" }
                    }
                }));

            cache = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.False(cache.TryGetPersistedPreviewSnapshot(previewCacheKey, currentPreviewFingerprint, out _));
            Assert.True(cache.TryGetPersistedSnapshotLoadWarning(out var warning));
            Assert.Contains("preview_fingerprint_mismatch", warning, StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void StoreSnapshot_PersistsProvidedPreviewFingerprint_ForDeferredPreviewValidation() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-preview-fingerprint-alignment");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var options = new ServiceOptions();
            var (previewCacheKey, cacheKey) = BuildToolingBootstrapKeys(options);
            var previewFingerprint = BuildToolingBootstrapPreviewFingerprint(options);
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());

            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                cacheKey,
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "synthetic_live_tool",
                            Description = "Only present in the live snapshot shape",
                            PackId = "synthetic_pack"
                        }
                    },
                    PackSummaries = Array.Empty<ToolPackInfoDto>(),
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "synthetic_pack",
                            Name = "Synthetic Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "synthetic_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "synthetic_live_tool" }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                },
                previewFingerprint);

            var reloaded = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.True(reloaded.TryGetPersistedPreviewSnapshot(previewCacheKey, previewFingerprint, out var previewSnapshot));
            Assert.Equal("synthetic_live_tool", Assert.Single(previewSnapshot.ToolDefinitions).Name);
            Assert.Equal(previewFingerprint, previewSnapshot.PreviewDiscoveryFingerprint);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void Constructor_EmitsStartupWarning_WhenDescriptorSnapshotSchemaMismatchIsIgnored() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-descriptor-snapshot-schema-mismatch");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var options = new ServiceOptions();
            var (previewCacheKey, _) = BuildToolingBootstrapKeys(options);
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var persistedSnapshot = new ChatServiceToolingBootstrapPersistedSnapshot {
                SchemaVersion = 4,
                CacheKey = previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                PreviewCacheKey = previewCacheKey,
                CachedAtUtc = DateTime.UtcNow,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "legacy_preview_tool",
                        Description = "Legacy preview tool",
                        PackId = "preview_pack"
                    }
                },
                PackSummaries = Array.Empty<ToolPackInfoDto>(),
                PackAvailability = new[] {
                    new ToolPackAvailabilityInfo {
                        Id = "preview_pack",
                        Name = "Preview Pack",
                        SourceKind = "closed_source",
                        Enabled = true
                    }
                },
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = routingDiagnostics,
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    EnabledPackIds = new[] { "preview_pack" },
                    EnabledPluginIds = Array.Empty<string>(),
                    RoutingFamilies = Array.Empty<string>(),
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    Skills = Array.Empty<string>(),
                    HealthyTools = new[] { "legacy_preview_tool" }
                },
                DescriptorSnapshot = new ChatServiceToolingBootstrapDescriptorSnapshot {
                    SchemaVersion = 2,
                    PreviewDiscoveryFingerprint = "stale-descriptor-preview",
                    ToolDefinitions = Array.Empty<ToolDefinitionDto>(),
                    PackSummaries = Array.Empty<ToolPackInfoDto>(),
                    PackAvailability = Array.Empty<ToolPackAvailabilityInfo>(),
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics
                }
            };

            File.WriteAllText(cachePath, JsonSerializer.Serialize(persistedSnapshot));

            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.True(cache.TryGetPersistedSnapshotLoadWarning(out var cacheLoadWarning));
            Assert.Contains("descriptor_snapshot_schema_mismatch", cacheLoadWarning, StringComparison.OrdinalIgnoreCase);

            var startupWarningsField = typeof(ChatServiceSession).GetField(
                "_startupWarnings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField(
                "_cachedToolDefinitions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(startupWarningsField);
            Assert.NotNull(cachedToolDefinitionsField);

            var session = new ChatServiceSession(options, Stream.Null, cache);

            var startupWarnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(session));
            Assert.Contains(
                startupWarnings,
                static warning => warning.Contains("persisted preview ignored", StringComparison.OrdinalIgnoreCase)
                    && warning.Contains("descriptor_snapshot_schema_mismatch", StringComparison.OrdinalIgnoreCase));

            var toolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField!.GetValue(session));
            Assert.Empty(toolDefinitions);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void Constructor_EmitsStartupWarning_WhenPersistedPreviewSchemaMismatchIsIgnored() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-preview-schema-mismatch");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var options = new ServiceOptions();
            var (previewCacheKey, _) = BuildToolingBootstrapKeys(options);
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var persistedSnapshot = new ChatServiceToolingBootstrapPersistedSnapshot {
                SchemaVersion = 3,
                CacheKey = previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                PreviewCacheKey = previewCacheKey,
                CachedAtUtc = DateTime.UtcNow,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "preview_tool",
                        Description = "Preview-only tool",
                        PackId = "preview_pack"
                    }
                },
                PackSummaries = Array.Empty<ToolPackInfoDto>(),
                PackAvailability = new[] {
                    new ToolPackAvailabilityInfo {
                        Id = "preview_pack",
                        Name = "Preview Pack",
                        SourceKind = "closed_source",
                        Enabled = true
                    }
                },
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = routingDiagnostics,
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    EnabledPackIds = new[] { "preview_pack" },
                    EnabledPluginIds = Array.Empty<string>(),
                    RoutingFamilies = Array.Empty<string>(),
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    Skills = Array.Empty<string>(),
                    HealthyTools = new[] { "preview_tool" }
                }
            };

            File.WriteAllText(cachePath, JsonSerializer.Serialize(persistedSnapshot));

            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            Assert.True(cache.TryGetPersistedSnapshotLoadWarning(out var cacheLoadWarning));
            Assert.Contains("schema_mismatch", cacheLoadWarning, StringComparison.OrdinalIgnoreCase);

            var startupWarningsField = typeof(ChatServiceSession).GetField(
                "_startupWarnings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField(
                "_cachedToolDefinitions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(startupWarningsField);
            Assert.NotNull(cachedToolDefinitionsField);

            var session = new ChatServiceSession(options, Stream.Null, cache);

            var startupWarnings = Assert.IsType<string[]>(startupWarningsField!.GetValue(session));
            Assert.Contains(
                startupWarnings,
                static warning => warning.Contains("persisted preview ignored", StringComparison.OrdinalIgnoreCase)
                    && warning.Contains("schema_mismatch", StringComparison.OrdinalIgnoreCase));

            var toolDefinitions = Assert.IsType<ToolDefinitionDto[]>(cachedToolDefinitionsField!.GetValue(session));
            Assert.Empty(toolDefinitions);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void TryGetCachedToolCatalogForListTools_UsesPersistedPreviewSnapshotBeforeStrictCacheKey() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-list-tools-preview-fallback");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var tryGetCachedToolCatalogMethod = typeof(ChatServiceSession).GetMethod(
                "TryGetCachedToolCatalogForListTools",
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: new[] {
                    typeof(ToolDefinitionDto[]).MakeByRefType(),
                    typeof(ToolPackInfoDto[]).MakeByRefType(),
                    typeof(PluginInfoDto[]).MakeByRefType(),
                    typeof(SessionRoutingCatalogDiagnosticsDto).MakeByRefType(),
                    typeof(SessionCapabilitySnapshotDto).MakeByRefType()
                },
                modifiers: null);
            var cachedToolDefinitionsField = typeof(ChatServiceSession).GetField(
                "_cachedToolDefinitions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(tryGetCachedToolCatalogMethod);
            Assert.NotNull(cachedToolDefinitionsField);

            var options = new ServiceOptions();
            var (previewCacheKey, cacheKey) = BuildToolingBootstrapKeys(options);
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var capabilitySnapshot = new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
                EnabledPackCount = 1,
                PluginCount = 0,
                EnabledPluginCount = 0,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                EnabledPackIds = new[] { "preview_pack" },
                EnabledPluginIds = Array.Empty<string>(),
                RoutingFamilies = Array.Empty<string>(),
                FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                Skills = Array.Empty<string>(),
                HealthyTools = new[] { "preview_tool" }
            };
            var persistedSnapshot = new ChatServiceToolingBootstrapPersistedSnapshot {
                SchemaVersion = 4,
                CacheKey = previewCacheKey + "discovery_fingerprint=stale-fingerprint;",
                PreviewCacheKey = previewCacheKey,
                CachedAtUtc = DateTime.UtcNow,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "preview_tool",
                        Description = "Preview-only tool",
                        PackId = "preview_pack"
                    }
                },
                PackSummaries = new[] {
                    new ToolPackInfoDto {
                        Id = "preview_pack",
                        Name = "Preview Pack",
                        Tier = CapabilityTier.ReadOnly,
                        Enabled = true,
                        IsDangerous = false,
                        SourceKind = ToolPackSourceKind.ClosedSource
                    }
                },
                PackAvailability = new[] {
                    new ToolPackAvailabilityInfo {
                        Id = "preview_pack",
                        Name = "Preview Pack",
                        SourceKind = "closed_source",
                        Enabled = true
                    }
                },
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                PluginCatalog = Array.Empty<ToolPluginCatalogInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = routingDiagnostics,
                CapabilitySnapshot = capabilitySnapshot
            };

            File.WriteAllText(cachePath, JsonSerializer.Serialize(persistedSnapshot));

            var session = new ChatServiceSession(options, Stream.Null, new ChatServiceToolingBootstrapCache(cachePath));
            cachedToolDefinitionsField!.SetValue(session, Array.Empty<ToolDefinitionDto>());

            var invocationArguments = new object?[] { null, null, null, null, null };
            var result = Assert.IsType<bool>(tryGetCachedToolCatalogMethod!.Invoke(session, invocationArguments));
            Assert.True(result);

            var tools = Assert.IsType<ToolDefinitionDto[]>(invocationArguments[0]);
            var packs = Assert.IsType<ToolPackInfoDto[]>(invocationArguments[1]);
            var plugins = Assert.IsType<PluginInfoDto[]>(invocationArguments[2]);
            var routingCatalog = Assert.IsType<SessionRoutingCatalogDiagnosticsDto>(invocationArguments[3]);
            var returnedCapabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(invocationArguments[4]);

            Assert.Equal("preview_tool", Assert.Single(tools).Name);
            Assert.Equal("preview_pack", Assert.Single(packs).Id);
            Assert.Equal("preview_pack", Assert.Single(plugins).Id);
            Assert.Equal(0, routingCatalog.TotalTools);
            Assert.Contains("preview_pack", returnedCapabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
            Assert.NotEqual(cacheKey, persistedSnapshot.CacheKey);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public async Task RebuildToolingFromOptions_ClearsPersistedPreviewPackAndCapabilityState_WhenLiveBootstrapApplies() {
        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-clear-preview-state");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var keyMethod = typeof(ChatServiceSession).GetMethod(
                "BuildToolingBootstrapCacheKey",
                BindingFlags.NonPublic | BindingFlags.Static);
            var runtimePolicyOptionsMethod = typeof(ChatServiceSession).GetMethod(
                "BuildRuntimePolicyOptions",
                BindingFlags.NonPublic | BindingFlags.Static);
            var rebuildMethod = typeof(ChatServiceSession).GetMethod(
                "RebuildToolingFromOptions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handleListToolsMethod = typeof(ChatServiceSession).GetMethod(
                "HandleListToolsAsync",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var persistedPreviewFlagField = typeof(ChatServiceSession).GetField(
                "_servingPersistedToolingBootstrapPreview",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var persistedPreviewPackSummariesField = typeof(ChatServiceSession).GetField(
                "_persistedPreviewPackSummaries",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var persistedPreviewCapabilitySnapshotField = typeof(ChatServiceSession).GetField(
                "_persistedPreviewCapabilitySnapshot",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(keyMethod);
            Assert.NotNull(runtimePolicyOptionsMethod);
            Assert.NotNull(rebuildMethod);
            Assert.NotNull(handleListToolsMethod);
            Assert.NotNull(persistedPreviewFlagField);
            Assert.NotNull(persistedPreviewPackSummariesField);
            Assert.NotNull(persistedPreviewCapabilitySnapshotField);

            var options = new ServiceOptions();
            var runtimePolicyOptions = Assert.IsType<ToolRuntimePolicyOptions>(runtimePolicyOptionsMethod!.Invoke(
                null,
                new object[] { options }));
            var resolvedRuntimePolicyOptions = ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions);
            var cacheKey = Assert.IsType<string>(keyMethod!.Invoke(
                null,
                new object?[] { options, runtimePolicyOptions, resolvedRuntimePolicyOptions }));
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                cacheKey,
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "preview_tool",
                            Description = "Persisted preview tool",
                            PackId = "preview_pack"
                        }
                    },
                    PackSummaries = new[] {
                        new ToolPackInfoDto {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.ClosedSource
                        }
                    },
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "preview_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "preview_tool" }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            var session = new ChatServiceSession(options, Stream.Null, cache);
            Assert.True(Assert.IsType<bool>(persistedPreviewFlagField!.GetValue(session)));

            var previewCapabilitySnapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
            Assert.Equal(new[] { "preview_pack" }, previewCapabilitySnapshot.EnabledPackIds);

            rebuildMethod!.Invoke(session, Array.Empty<object>());

            Assert.False(Assert.IsType<bool>(persistedPreviewFlagField.GetValue(session)));
            Assert.Empty(Assert.IsType<ToolPackInfoDto[]>(persistedPreviewPackSummariesField!.GetValue(session)));
            Assert.Null(persistedPreviewCapabilitySnapshotField!.GetValue(session));

            var liveCapabilitySnapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
            Assert.DoesNotContain("preview_pack", liveCapabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
            Assert.NotEmpty(liveCapabilitySnapshot.EnabledPackIds);

            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream) { AutoFlush = true };
            var listTask = Assert.IsAssignableFrom<Task>(handleListToolsMethod!.Invoke(session, new object?[] {
                writer,
                "req_list_tools_after_live_bootstrap",
                CancellationToken.None
            }));
            await listTask;

            memoryStream.Position = 0;
            using var reader = new StreamReader(memoryStream);
            var json = await reader.ReadToEndAsync();
            var parsed = JsonSerializer.Deserialize<ChatServiceMessage>(json, ChatServiceJsonContext.Default.ChatServiceMessage);
            var message = Assert.IsType<ToolListMessage>(parsed);
            var listCapabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(message.CapabilitySnapshot);
            Assert.DoesNotContain(message.Packs, static pack => string.Equals(pack.Id, "preview_pack", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("preview_pack", listCapabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void ApplyToolingBootstrapCacheSnapshot_ClearsPersistedPreviewPackAndCapabilityState_WhenRealSnapshotApplies() {
        var keyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        var runtimePolicyOptionsMethod = typeof(ChatServiceSession).GetMethod(
            "BuildRuntimePolicyOptions",
            BindingFlags.NonPublic | BindingFlags.Static);
        var applyCacheSnapshotMethod = typeof(ChatServiceSession).GetMethod(
            "ApplyToolingBootstrapCacheSnapshot",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var persistedPreviewFlagField = typeof(ChatServiceSession).GetField(
            "_servingPersistedToolingBootstrapPreview",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var persistedPreviewPackSummariesField = typeof(ChatServiceSession).GetField(
            "_persistedPreviewPackSummaries",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var persistedPreviewCapabilitySnapshotField = typeof(ChatServiceSession).GetField(
            "_persistedPreviewCapabilitySnapshot",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(keyMethod);
        Assert.NotNull(runtimePolicyOptionsMethod);
        Assert.NotNull(applyCacheSnapshotMethod);
        Assert.NotNull(persistedPreviewFlagField);
        Assert.NotNull(persistedPreviewPackSummariesField);
        Assert.NotNull(persistedPreviewCapabilitySnapshotField);

        var cachePath = CreateToolingBootstrapCachePath("tooling-cache-apply-cache-snapshot");
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory)) {
            Directory.CreateDirectory(cacheDirectory);
        }

        try {
            var options = new ServiceOptions();
            var runtimePolicyOptions = Assert.IsType<ToolRuntimePolicyOptions>(runtimePolicyOptionsMethod!.Invoke(
                null,
                new object[] { options }));
            var resolvedRuntimePolicyOptions = ToolRuntimePolicyBootstrap.ResolveOptions(runtimePolicyOptions);
            var cacheKey = Assert.IsType<string>(keyMethod!.Invoke(
                null,
                new object?[] { options, runtimePolicyOptions, resolvedRuntimePolicyOptions }));
            var diagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(
                ToolRuntimePolicyBootstrap.CreateContext(new ToolRuntimePolicyOptions()));
            var routingDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>());
            var cache = new ChatServiceToolingBootstrapCache(cachePath);
            cache.StoreSnapshot(
                cacheKey,
                new ChatServiceToolingBootstrapSnapshot {
                    Registry = new ToolRegistry(),
                    ToolDefinitions = new[] {
                        new ToolDefinitionDto {
                            Name = "preview_tool",
                            Description = "Persisted preview tool",
                            PackId = "preview_pack"
                        }
                    },
                    PackSummaries = new[] {
                        new ToolPackInfoDto {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false,
                            SourceKind = ToolPackSourceKind.ClosedSource
                        }
                    },
                    Packs = Array.Empty<IToolPack>(),
                    PackAvailability = new[] {
                        new ToolPackAvailabilityInfo {
                            Id = "preview_pack",
                            Name = "Preview Pack",
                            SourceKind = "closed_source",
                            Enabled = true
                        }
                    },
                    PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                    StartupWarnings = Array.Empty<string>(),
                    StartupBootstrap = new SessionStartupBootstrapTelemetryDto(),
                    PluginSearchPaths = Array.Empty<string>(),
                    RuntimePolicyDiagnostics = diagnostics,
                    RoutingCatalogDiagnostics = routingDiagnostics,
                    CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                        RegisteredTools = 1,
                        EnabledPackCount = 1,
                        PluginCount = 0,
                        EnabledPluginCount = 0,
                        ToolingAvailable = true,
                        AllowedRootCount = 0,
                        EnabledPackIds = new[] { "preview_pack" },
                        EnabledPluginIds = Array.Empty<string>(),
                        RoutingFamilies = Array.Empty<string>(),
                        FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                        Skills = Array.Empty<string>(),
                        HealthyTools = new[] { "preview_tool" }
                    },
                    ToolOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>())
                });

            var session = new ChatServiceSession(options, Stream.Null, cache);
            Assert.True(Assert.IsType<bool>(persistedPreviewFlagField!.GetValue(session)));

            var liveRegistry = new ToolRegistry();
            var liveOrchestrationCatalog = ToolOrchestrationCatalog.Build(Array.Empty<ToolDefinition>());
            var livePackAvailability = new[] {
                new ToolPackAvailabilityInfo {
                    Id = "system",
                    Name = "System",
                    SourceKind = "closed_source",
                    Enabled = true
                }
            };

            var snapshot = new ChatServiceToolingBootstrapSnapshot {
                Registry = liveRegistry,
                ToolDefinitions = new[] {
                    new ToolDefinitionDto {
                        Name = "system_info",
                        Description = "Get system info.",
                        PackId = "system",
                        PackName = "System"
                    }
                },
                PackSummaries = new[] {
                    new ToolPackInfoDto {
                        Id = "system",
                        Name = "System",
                        Tier = CapabilityTier.ReadOnly,
                        Enabled = true,
                        IsDangerous = false,
                        SourceKind = ToolPackSourceKind.ClosedSource
                    }
                },
                Packs = Array.Empty<IToolPack>(),
                PackAvailability = livePackAvailability,
                PluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>(),
                StartupWarnings = Array.Empty<string>(),
                StartupBootstrap = new SessionStartupBootstrapTelemetryDto {
                    Tools = 0,
                    PacksLoaded = 1
                },
                PluginSearchPaths = Array.Empty<string>(),
                RuntimePolicyDiagnostics = diagnostics,
                RoutingCatalogDiagnostics = ToolRoutingCatalogDiagnosticsBuilder.Build(Array.Empty<ToolDefinition>()),
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 0,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    EnabledPackIds = new[] { "system" },
                    EnabledPluginIds = Array.Empty<string>(),
                    RoutingFamilies = Array.Empty<string>(),
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                    Skills = Array.Empty<string>(),
                    HealthyTools = Array.Empty<string>()
                },
                ToolOrchestrationCatalog = liveOrchestrationCatalog
            };

            applyCacheSnapshotMethod!.Invoke(session, new object?[] {
                snapshot,
                false,
                TimeSpan.FromMilliseconds(1)
            });

            Assert.False(Assert.IsType<bool>(persistedPreviewFlagField.GetValue(session)));
            Assert.Empty(Assert.IsType<ToolPackInfoDto[]>(persistedPreviewPackSummariesField!.GetValue(session)));
            Assert.Null(persistedPreviewCapabilitySnapshotField!.GetValue(session));

            var runtimeCapabilitySnapshot = session.BuildRuntimeCapabilitySnapshotForTesting();
            Assert.DoesNotContain("preview_pack", runtimeCapabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("system", runtimeCapabilitySnapshot.EnabledPackIds, StringComparer.OrdinalIgnoreCase);
        } finally {
            try {
                if (File.Exists(cachePath)) {
                    File.Delete(cachePath);
                }
            } catch {
                // Best-effort test cleanup.
            }
        }
    }

    [Fact]
    public void BuildToolingBootstrapCacheKey_IncludesResolvedSmtpProbePolicyDimensions() {
        var keyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(keyMethod);

        var options = new ServiceOptions();
        var runtimePolicyOptions = new ToolRuntimePolicyOptions {
            AuthenticationPreset = ToolAuthenticationRuntimePreset.Strict,
            RequireAuthenticationRuntime = true
        };

        var strictResolved = new ToolRuntimePolicyResolvedOptions {
            Options = runtimePolicyOptions,
            RequireSuccessfulSmtpProbeForSend = true,
            SmtpProbeMaxAgeSeconds = 600
        };
        var relaxedResolved = strictResolved with {
            RequireSuccessfulSmtpProbeForSend = false,
            SmtpProbeMaxAgeSeconds = 60
        };

        var strictKey = Assert.IsType<string>(keyMethod!.Invoke(null, new object?[] { options, runtimePolicyOptions, strictResolved }));
        var relaxedKey = Assert.IsType<string>(keyMethod.Invoke(null, new object?[] { options, runtimePolicyOptions, relaxedResolved }));

        Assert.Contains("require_smtp_probe=1;", strictKey, StringComparison.Ordinal);
        Assert.Contains("smtp_probe_max_age_seconds=600;", strictKey, StringComparison.Ordinal);
        Assert.Contains("require_smtp_probe=0;", relaxedKey, StringComparison.Ordinal);
        Assert.Contains("smtp_probe_max_age_seconds=60;", relaxedKey, StringComparison.Ordinal);
        Assert.NotEqual(strictKey, relaxedKey);
    }

    [Fact]
    public void BuildToolingBootstrapCacheKey_IncludesBuiltInPackLoadingDimension() {
        var keyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(keyMethod);

        var runtimePolicyOptions = new ToolRuntimePolicyOptions();
        var resolved = new ToolRuntimePolicyResolvedOptions {
            Options = runtimePolicyOptions,
            RequireSuccessfulSmtpProbeForSend = false,
            SmtpProbeMaxAgeSeconds = 900
        };

        var builtInEnabledOptions = new ServiceOptions {
            EnableBuiltInPackLoading = true
        };
        var builtInDisabledOptions = new ServiceOptions {
            EnableBuiltInPackLoading = false
        };

        var enabledKey = Assert.IsType<string>(keyMethod!.Invoke(null, new object?[] { builtInEnabledOptions, runtimePolicyOptions, resolved }));
        var disabledKey = Assert.IsType<string>(keyMethod.Invoke(null, new object?[] { builtInDisabledOptions, runtimePolicyOptions, resolved }));

        Assert.Contains("built_in_packs=1;", enabledKey, StringComparison.Ordinal);
        Assert.Contains("built_in_packs=0;", disabledKey, StringComparison.Ordinal);
        Assert.NotEqual(enabledKey, disabledKey);
    }

    [Fact]
    public void BuildToolingBootstrapCacheKey_IncludesRuntimePluginPaths() {
        var keyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(keyMethod);

        var runtimePolicyOptions = new ToolRuntimePolicyOptions();
        var resolved = new ToolRuntimePolicyResolvedOptions {
            Options = runtimePolicyOptions,
            RequireSuccessfulSmtpProbeForSend = false,
            SmtpProbeMaxAgeSeconds = 900
        };

        var first = new ServiceOptions();
        first.RuntimePluginPaths.Add(@"C:\plugins\runtime-a");
        var second = new ServiceOptions();
        second.RuntimePluginPaths.Add(@"C:\plugins\runtime-b");

        var firstKey = Assert.IsType<string>(keyMethod!.Invoke(null, new object?[] { first, runtimePolicyOptions, resolved }));
        var secondKey = Assert.IsType<string>(keyMethod.Invoke(null, new object?[] { second, runtimePolicyOptions, resolved }));

        Assert.Contains(@"plugin_paths=C:\plugins\runtime-a;", firstKey, StringComparison.Ordinal);
        Assert.Contains(@"plugin_paths=C:\plugins\runtime-b;", secondKey, StringComparison.Ordinal);
        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void BuildToolingBootstrapCacheKey_ChangesWhenPluginDiscoverySurfaceChanges() {
        var keyMethod = typeof(ChatServiceSession).GetMethod(
            "BuildToolingBootstrapCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(keyMethod);

        var runtimePolicyOptions = new ToolRuntimePolicyOptions();
        var resolved = new ToolRuntimePolicyResolvedOptions {
            Options = runtimePolicyOptions,
            RequireSuccessfulSmtpProbeForSend = false,
            SmtpProbeMaxAgeSeconds = 900
        };

        var root = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-plugin-fingerprint");
        Directory.CreateDirectory(root);
        var pluginRoot = Path.Combine(root, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);
        File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), """
            {
              "schemaVersion": 1,
              "pluginId": "ops_bundle",
              "displayName": "Ops bundle"
            }
            """);

        try {
            var options = new ServiceOptions {
                EnableDefaultPluginPaths = false
            };
            options.RuntimePluginPaths.Add(pluginRoot);

            var keyWithManifest = Assert.IsType<string>(keyMethod!.Invoke(null, new object?[] { options, runtimePolicyOptions, resolved }));
            File.Delete(Path.Combine(pluginFolder, "ix-plugin.json"));
            var keyWithoutManifest = Assert.IsType<string>(keyMethod.Invoke(null, new object?[] { options, runtimePolicyOptions, resolved }));

            Assert.NotEqual(keyWithManifest, keyWithoutManifest);
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesAndSortsTopEntries() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] path_not_found path='C:\\plugins\\missing'",
            "[plugin] load_timing plugin='delta' elapsed_ms='650' entry_assemblies='1' candidate_types='1' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_timing plugin='alpha' elapsed_ms='1400' entry_assemblies='1' candidate_types='1' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_timing plugin='beta' elapsed_ms='900' entry_assemblies='1' candidate_types='1' loaded='0' disabled='1' duplicate='0' failed='0'",
            "[plugin] load_timing plugin='gamma' elapsed_ms='1200' entry_assemblies='1' candidate_types='1' loaded='0' disabled='0' duplicate='0' failed='1'",
            "[plugin] load_timing plugin='alpha' elapsed_ms='1100' entry_assemblies='1' candidate_types='1' loaded='1' disabled='0' duplicate='0' failed='0'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(warnings, static w => w.StartsWith("[plugin] load_timing", StringComparison.OrdinalIgnoreCase));

        var summary = Assert.Single(warnings, static w => w.StartsWith("[startup] slow plugin loads top", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("alpha=1400ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gamma=1200ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("beta=900ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delta=650ms", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(warnings, static w => w.StartsWith("[startup] additional slow plugins omitted: 1.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_NoTimingWarnings_LeavesCollectionUntouched() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] path_not_found path='C:\\plugins\\missing'",
            "[plugin] init_failed plugin='alpha' error='missing dep'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Equal(2, warnings.Count);
        Assert.Contains("[plugin] path_not_found path='C:\\plugins\\missing'", warnings);
        Assert.Contains("[plugin] init_failed plugin='alpha' error='missing dep'", warnings);
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesPluginProgressWarnings() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] load_progress plugin='alpha' phase='begin' index='1' total='3'",
            "[plugin] load_progress plugin='alpha' phase='end' index='1' total='3' elapsed_ms='800' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='beta' phase='begin' index='2' total='3'",
            "[plugin] load_progress plugin='beta' phase='end' index='2' total='3' elapsed_ms='300' loaded='0' disabled='1' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='gamma' phase='begin' index='3' total='3'",
            "[plugin] load_progress plugin='gamma' phase='end' index='3' total='3' elapsed_ms='400' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] path_not_found path='C:\\plugins\\missing'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.DoesNotContain(warnings, static w => w.StartsWith("[plugin] load_progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings,
            static w => w.StartsWith("[startup] plugin load progress: processed 3/3 plugin folders (begin=3, end=3).", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesPackProgressWarnings() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[startup] pack_load_progress pack='eventlog' phase='begin' index='1' total='3'",
            "[startup] pack_load_progress pack='eventlog' phase='end' index='1' total='3' elapsed_ms='120' failed='0'",
            "[startup] pack_load_progress pack='active_directory' phase='begin' index='2' total='3'",
            "[startup] pack_load_progress pack='active_directory' phase='end' index='2' total='3' elapsed_ms='1400' failed='0'",
            "[startup] pack_load_progress pack='plugins' phase='begin' index='3' total='3'",
            "[startup] pack_load_progress pack='plugins' phase='end' index='3' total='3' elapsed_ms='900' failed='1'",
            "[plugin] path_not_found path='C:\\plugins\\missing'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.DoesNotContain(warnings, static w => w.StartsWith("[startup] pack_load_progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[startup] pack load progress: processed 3/3 bootstrap steps (begin=3, end=3).", StringComparison.OrdinalIgnoreCase));

        var slowPacks = Assert.Single(warnings, static w => w.StartsWith("[startup] slow pack loads top", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("active_directory=1400ms", slowPacks, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("plugins=900ms", slowPacks, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_PluginProcessedProgress_UsesCompletedEndEvents() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[plugin] load_progress plugin='alpha' phase='begin' index='1' total='3'",
            "[plugin] load_progress plugin='alpha' phase='end' index='1' total='3' elapsed_ms='100' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='beta' phase='begin' index='2' total='3'",
            "[plugin] load_progress plugin='beta' phase='end' index='2' total='3' elapsed_ms='120' loaded='1' disabled='0' duplicate='0' failed='0'",
            "[plugin] load_progress plugin='gamma' phase='begin' index='3' total='3'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Contains(
            warnings,
            static w => w.StartsWith("[startup] plugin load progress: processed 2/3 plugin folders (begin=3, end=2).", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_PackProcessedProgress_UsesCompletedEndEvents() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[startup] pack_load_progress pack='eventlog' phase='begin' index='1' total='3'",
            "[startup] pack_load_progress pack='eventlog' phase='end' index='1' total='3' elapsed_ms='120' failed='0'",
            "[startup] pack_load_progress pack='active_directory' phase='begin' index='2' total='3'",
            "[startup] pack_load_progress pack='plugins' phase='begin' index='3' total='3'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.Contains(
            warnings,
            static w => w.StartsWith("[startup] pack load progress: processed 1/3 bootstrap steps (begin=3, end=1).", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SummarizeSlowPluginLoadWarnings_CompressesPackRegistrationProgressWarnings() {
        var method = typeof(ChatServiceSession).GetMethod(
            "SummarizeSlowPluginLoadWarnings",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var warnings = new List<string> {
            "[startup] pack_register_progress pack='eventlog' phase='begin' index='1' total='3'",
            "[startup] pack_register_progress pack='eventlog' phase='end' index='1' total='3' elapsed_ms='1200' tools_registered='10' total_tools='10' failed='0'",
            "[startup] pack_register_progress pack='active_directory' phase='begin' index='2' total='3'",
            "[startup] pack_register_progress pack='active_directory' phase='end' index='2' total='3' elapsed_ms='220' tools_registered='14' total_tools='24' failed='0'",
            "[startup] pack_register_progress pack='plugins' phase='begin' index='3' total='3'",
            "[plugin] path_not_found path='C:\\plugins\\missing'"
        };

        method!.Invoke(null, new object?[] { warnings });

        Assert.DoesNotContain(warnings, static w => w.StartsWith("[startup] pack_register_progress", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[plugin] path_not_found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(warnings, static w => w.StartsWith("[startup] pack registration progress: processed 2/3 packs (begin=3, end=2).", StringComparison.OrdinalIgnoreCase));

        var slowRegistrations = Assert.Single(warnings, static w => w.StartsWith("[startup] slow pack registrations top", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("eventlog=1200ms", slowRegistrations, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tools=10", slowRegistrations, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveDeferredActivationPackIdsForChatRequest_PrefersStrongUnambiguousDescriptorPackMatch() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.SetCachedToolDefinitionsForTesting(new[] {
            new ToolDefinitionDto {
                Name = "ops_inventory_collect",
                Description = "Collect remote host inventory.",
                PackId = "ops_inventory",
                Category = "system",
                ExecutionScope = "remote_only",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "collect inventory from remote host" }
            },
            new ToolDefinitionDto {
                Name = "eventlog_live_query",
                Description = "Query remote event logs.",
                PackId = "eventlog",
                Category = "event-log",
                ExecutionScope = "local_or_remote",
                SupportsRemoteExecution = true,
                SupportsRemoteHostTargeting = true,
                RepresentativeExamples = new[] { "query event logs from remote host" }
            }
        });

        var packIds = session.ResolveDeferredActivationPackIdsForChatRequestForTesting("run ops_inventory_collect against srv1");

        Assert.Equal(new[] { "ops_inventory" }, packIds);
    }

    [Fact]
    public async Task TryPrepareDeferredChatToolingForRequestAsync_ActivatesMatchingPackWithoutStartupBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-deferred-chat-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false,
                  "representativeExamples": ["show synthetic plugin probe status"]
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var prepared = await session.TryPrepareDeferredChatToolingForRequestAsyncForTesting(new ChatRequest {
                RequestId = "req_chat",
                Text = "please run plugin_loader_synthetic_probe"
            });

            Assert.True(prepared);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_probe", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryPrepareDeferredChatToolingForRequestAsync_ActivatesMatchingPackForStructuredContinuationWithoutStartupBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-deferred-chat-continuation-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false,
                  "representativeExamples": ["show synthetic plugin probe status"]
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var prepared = await session.TryPrepareDeferredChatToolingForRequestAsyncForTesting(new ChatRequest {
                RequestId = "req_chat_continuation",
                ThreadId = "thread_synthetic",
                Text = """
                       ix:continuation:v1
                       intent_anchor: investigate synthetic plugin runtime
                       follow_up: please run plugin_loader_synthetic_probe
                       """
            });

            Assert.True(prepared);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_probe", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CaptureDeferredChatToolingStatusesForTesting_EmitsPendingAndActivatedRoutingStatuses() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-deferred-chat-statuses");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false,
                  "representativeExamples": ["show synthetic plugin probe status"]
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var statuses = await session.CaptureDeferredChatToolingStatusesForTesting(new ChatRequest {
                RequestId = "req_chat_status",
                Text = "please run plugin_loader_synthetic_probe"
            });

            Assert.Collection(
                statuses,
                first => {
                    Assert.Equal(ChatStatusCodes.Routing, first.Status);
                    Assert.Equal(
                        "Activating descriptor-matched pack 'plugin_loader_synthetic_catalog' before chat routing...",
                        first.Message);
                },
                second => {
                    Assert.Equal(ChatStatusCodes.Routing, second.Status);
                    Assert.Equal(
                        "Activated descriptor-matched pack 'plugin_loader_synthetic_catalog' before chat routing.",
                        second.Message);
                });
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryPrepareDeferredChatToolingForRequestAsync_PrewarmsDeferredHandoffTargetPackFromDescriptorMatchedSourceWithoutStartupBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-deferred-handoff-prewarm");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var sourcePluginFolder = Path.Combine(pluginRoot, "source-bundle");
        var targetPluginFolder = Path.Combine(pluginRoot, "target-bundle");
        Directory.CreateDirectory(sourcePluginFolder);
        Directory.CreateDirectory(targetPluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(sourcePluginFolder, entryAssemblyName), overwrite: true);
            File.Copy(sourceAssemblyPath, Path.Combine(targetPluginFolder, entryAssemblyName), overwrite: true);

            var sourceEntryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticHandoffSourcePack).FullName;
            var targetEntryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(sourceEntryType));
            Assert.False(string.IsNullOrWhiteSpace(targetEntryType));

            File.WriteAllText(Path.Combine(sourcePluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "source-bundle",
              "displayName": "Source Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_handoff_source"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{sourceEntryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_handoff_entry",
                  "description": "Synthetic source tool that hands off to a deferred plugin target.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false,
                  "representativeExamples": ["inspect synthetic handoff source runtime"],
                  "handoffTargetPackIds": ["plugin_loader_synthetic_catalog"],
                  "handoffTargetToolNames": ["plugin_loader_synthetic_probe"]
                }
              ]
            }
            """);

            File.WriteAllText(Path.Combine(targetPluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "target-bundle",
              "displayName": "Target Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{targetEntryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);

            var activationPackIds = session.ResolveDeferredActivationPackIdsForChatRequestForTesting("please run plugin_loader_synthetic_handoff_entry");
            Assert.Equal(new[] { "plugin_loader_synthetic_handoff_source" }, activationPackIds);

            var prepared = await session.TryPrepareDeferredChatToolingForRequestAsyncForTesting(new ChatRequest {
                RequestId = "req_chat_handoff_prewarm",
                Text = "please run plugin_loader_synthetic_handoff_entry"
            });

            Assert.True(prepared);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_handoff_entry", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_probe", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryBuildRecoveryHelperInvocationForTesting_ActivatesDeferredHelperPackWithoutStartupBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-recovery-helper-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "system_info",
                    Description = "Inspect system details on a target host.",
                    PackId = "system",
                    RepresentativeExamples = new[] { "run system_info" }
                },
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_probe",
                    Description = "Probe the synthetic plugin runtime.",
                    PackId = "plugin_loader_synthetic_catalog"
                }
            });

            var failedCall = new ToolCall(
                "failed_call",
                "synthetic_failure",
                """{"target":"srv1"}""",
                new JsonObject(StringComparer.Ordinal).Add("target", "srv1"),
                new JsonObject(StringComparer.Ordinal));

            var built = session.TryBuildRecoveryHelperInvocationForTesting(
                failedCall,
                "plugin_loader_synthetic_probe",
                out var helperCall);

            Assert.True(built);
            Assert.Equal("plugin_loader_synthetic_probe", helperCall.Name);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_probe", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildHostPackPreflightCalls_ActivatesDeferredHelperPackForSameRoundPreflight() {
        var buildHostPackPreflightCallsMethod = typeof(ChatServiceSession).GetMethod(
            "BuildHostPackPreflightCalls",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(buildHostPackPreflightCallsMethod);

        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-preflight-helper-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticPreflightPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_preflight"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_helper",
                  "description": "Synthetic helper tool.",
                  "category": "diagnostic",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_helper",
                    Description = "Synthetic helper tool.",
                    PackId = "plugin_loader_synthetic_preflight"
                }
            });

            var operationalDefinition = new ToolDefinition(
                "plugin_loader_synthetic_operational",
                "Synthetic operational tool.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "plugin_loader_synthetic_preflight",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    RecoveryToolNames = new[] { "plugin_loader_synthetic_helper" }
                });

            var extractedCalls = new List<ToolCall> {
                new(
                    "operational_call",
                    "plugin_loader_synthetic_operational",
                    "{}",
                    new JsonObject(StringComparer.Ordinal),
                    new JsonObject(StringComparer.Ordinal))
            };

            var result = buildHostPackPreflightCallsMethod!.Invoke(
                session,
                new object?[] { "thread_preflight", new[] { operationalDefinition }, extractedCalls });
            var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

            Assert.Equal(2, preflightCalls.Count);
            Assert.Equal("plugin_loader_synthetic_pack_probe", preflightCalls[0].Name);
            Assert.Equal("plugin_loader_synthetic_helper", preflightCalls[1].Name);
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_helper", StringComparison.OrdinalIgnoreCase));
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunBackgroundSchedulerDaemonAsync_CanceledBeforePolling_DoesNotStartBootstrap() {
        var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
        options.EnableBackgroundSchedulerDaemon = true;
        options.EnableBuiltInPackLoading = false;
        options.EnableDefaultPluginPaths = false;
        var session = new ChatServiceSession(options, Stream.Null);

        using var cancellationTokenSource = new System.Threading.CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await session.RunBackgroundSchedulerDaemonAsync(cancellationTokenSource.Token);

        Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
    }

    [Fact]
    public async Task RunBackgroundSchedulerDaemonAsync_ExecutesDeferredPluginTargetWithoutStartingFullBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-background-daemon-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBackgroundSchedulerDaemon = true;
            options.BackgroundSchedulerPollSeconds = 1;
            options.BackgroundSchedulerBurstLimit = 1;
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_probe",
                    Description = "Probe the synthetic plugin runtime.",
                    PackId = "plugin_loader_synthetic_catalog"
                }
            });

            const string threadId = "thread-background-daemon-deferred-plugin";
            var definitions = new[] {
                new ToolDefinition(
                    name: "seed_plugin_probe_followup",
                    description: "Seed a deferred plugin follow-up.",
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "plugin_loader_synthetic_catalog",
                                TargetToolName = "plugin_loader_synthetic_probe",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                                FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "target",
                                        TargetArgument = "target"
                                    }
                                }
                            }
                        }
                    })
            };

            session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-background-daemon-deferred-plugin",
                        Name = "seed_plugin_probe_followup",
                        ArgumentsJson = """{"target":"srv-daemon.contoso.com"}"""
                    }
                },
                new[] {
                    new ToolOutputDto {
                        CallId = "call-background-daemon-deferred-plugin",
                        Ok = true,
                        Output = """{"ok":true}"""
                    }
                });

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var daemonTask = session.RunBackgroundSchedulerDaemonAsync(cancellationTokenSource.Token);

            SessionCapabilityBackgroundSchedulerDto? summary = null;
            for (var attempt = 0; attempt < 40; attempt++) {
                summary = session.BuildBackgroundSchedulerSummaryForTesting();
                if (summary.CompletedItemCount > 0) {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken.None);
            }

            Assert.NotNull(summary);
            Assert.Equal(1, summary!.CompletedItemCount);
            Assert.Contains(threadId, summary.ThreadSummaries.Select(static item => item.ThreadId), StringComparer.Ordinal);
            Assert.Contains("plugin_loader_synthetic_probe", session.GetRegisteredToolNamesForTesting(), StringComparer.OrdinalIgnoreCase);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());

            await cancellationTokenSource.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await daemonTask);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryBuildScheduledBackgroundWorkToolCallForTesting_ClaimsDeferredPluginTargetWithoutStartupBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-background-claim-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_probe",
                    Description = "Probe the synthetic plugin runtime.",
                    PackId = "plugin_loader_synthetic_catalog"
                }
            });

            const string threadId = "thread-background-claim-deferred-plugin";
            var definitions = new[] {
                new ToolDefinition(
                    name: "seed_plugin_probe_followup",
                    description: "Seed a deferred plugin follow-up.",
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "plugin_loader_synthetic_catalog",
                                TargetToolName = "plugin_loader_synthetic_probe",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                                FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "target",
                                        TargetArgument = "target"
                                    }
                                }
                            }
                        }
                    })
            };

            session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-background-claim-deferred-plugin",
                        Name = "seed_plugin_probe_followup",
                        ArgumentsJson = """{"target":"srv-claim.contoso.com"}"""
                    }
                },
                new[] {
                    new ToolOutputDto {
                        CallId = "call-background-claim-deferred-plugin",
                        Ok = true,
                        Output = """{"ok":true}"""
                    }
                });

            var claimed = session.TryBuildScheduledBackgroundWorkToolCallForTesting(
                Array.Empty<ToolDefinition>(),
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                out var scheduledThreadId,
                out _,
                out var toolName,
                out var argumentsJson,
                out var reason);

            Assert.True(claimed, reason);
            Assert.Equal("background_scheduler_claimed_ready_work", reason);
            Assert.Equal(threadId, scheduledThreadId);
            Assert.Equal("plugin_loader_synthetic_probe", toolName);
            Assert.Contains("\"target\":\"srv-claim.contoso.com\"", argumentsJson, StringComparison.Ordinal);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
            Assert.DoesNotContain("plugin_loader_synthetic_probe", session.GetRegisteredToolNamesForTesting(), StringComparer.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunBackgroundSchedulerIterationAsyncForTesting_ExecutesClaimedDeferredPackWithoutStartingFullBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-background-execute-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_probe",
                    Description = "Probe the synthetic plugin runtime.",
                    PackId = "plugin_loader_synthetic_catalog"
                }
            });

            const string threadId = "thread-background-execute-deferred-plugin";
            var definitions = new[] {
                new ToolDefinition(
                    name: "seed_plugin_probe_followup",
                    description: "Seed a deferred plugin follow-up.",
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "plugin_loader_synthetic_catalog",
                                TargetToolName = "plugin_loader_synthetic_probe",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                FollowUpKind = ToolHandoffFollowUpKinds.Enrichment,
                                FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "target",
                                        TargetArgument = "target"
                                    }
                                }
                            }
                        }
                    })
            };

            session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-background-execute-deferred-plugin",
                        Name = "seed_plugin_probe_followup",
                        ArgumentsJson = """{"target":"srv-execute.contoso.com"}"""
                    }
                },
                new[] {
                    new ToolOutputDto {
                        CallId = "call-background-execute-deferred-plugin",
                        Ok = true,
                        Output = """{"ok":true}"""
                    }
                });

            string? observedThreadId = null;
            string? observedToolName = null;
            string? observedArgumentsJson = null;
            string[] observedRegisteredToolNames = Array.Empty<string>();
            var result = await session.RunBackgroundSchedulerIterationAsyncForTesting(
                Array.Empty<ToolDefinition>(),
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                async (scheduledThreadId, toolCall, cancellationToken) => {
                    observedThreadId = scheduledThreadId;
                    observedToolName = toolCall.Name;
                    observedArgumentsJson = JsonLite.Serialize(toolCall.Arguments);
                    var output = await session.ExecuteToolAsyncForTesting(
                        scheduledThreadId,
                        "ix:background-scheduler-daemon",
                        toolCall,
                        5,
                        cancellationToken);
                    observedRegisteredToolNames = session.GetRegisteredToolNamesForTesting();

                    return new[] { output };
                });

            Assert.Equal(ChatServiceSession.BackgroundSchedulerIterationOutcomeKind.Completed, result.Outcome);
            Assert.Equal(threadId, result.ThreadId);
            Assert.Equal("plugin_loader_synthetic_probe", result.ToolName);
            Assert.Equal(threadId, observedThreadId);
            Assert.Equal("plugin_loader_synthetic_probe", observedToolName);
            Assert.Contains("\"target\":\"srv-execute.contoso.com\"", observedArgumentsJson, StringComparison.Ordinal);
            Assert.Contains("plugin_loader_synthetic_probe", observedRegisteredToolNames, StringComparer.OrdinalIgnoreCase);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TryActivateDeferredHandoffTargetPacksAfterRoundAsyncForTesting_ActivatesDeferredPluginTargetForSameTurnFollowUp() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-round-handoff-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticCatalogPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_catalog"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_probe",
                  "description": "Probe the synthetic plugin runtime.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_probe",
                    Description = "Probe the synthetic plugin runtime.",
                    PackId = "plugin_loader_synthetic_catalog"
                }
            });

            var sourceDefinition = new ToolDefinition(
                name: "system_info",
                description: "Inspect system details and hand off to plugin follow-up.",
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "system",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "plugin_loader_synthetic_catalog",
                            TargetToolName = "plugin_loader_synthetic_probe",
                            TargetRole = ToolRoutingTaxonomy.RoleOperational,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "target",
                                    TargetArgument = "target"
                                }
                            }
                        }
                    }
                });

            session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(new[] { sourceDefinition }));

            var recentCalls = new[] {
                new ToolCall(
                    callId: "call-round-handoff-activation",
                    name: "system_info",
                    input: "{}",
                    arguments: new JsonObject(),
                    raw: new JsonObject())
            };
            var activatedPackIds = await session.TryActivateDeferredHandoffTargetPacksAfterRoundAsyncForTesting(
                new[] { sourceDefinition },
                recentCalls,
                hasExplicitToolEnableSelectors: false,
                continuationContractDetected: false,
                executionContractApplies: false,
                hasPendingActionContext: false);

            Assert.Single(activatedPackIds);
            Assert.Equal("plugin_loader_synthetic_catalog", activatedPackIds[0], ignoreCase: true);
            Assert.Contains(
                session.GetRegisteredToolNamesForTesting(),
                static toolName => string.Equals(toolName, "plugin_loader_synthetic_probe", StringComparison.OrdinalIgnoreCase));
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryBuildBackgroundWorkDependencyRecoveryPromptForTesting_ActivatesDeferredDependencyPackWithoutStartupBootstrap() {
        var tempRoot = TempPathTestHelper.CreateTempDirectoryPath("ix-chat-background-recovery-activation");
        var pluginRoot = Path.Combine(tempRoot, "plugins");
        var pluginFolder = Path.Combine(pluginRoot, "ops-bundle");
        Directory.CreateDirectory(pluginFolder);

        try {
            var options = ChatServiceTestSessionFactory.CreateIsolatedOptions();
            options.EnableBuiltInPackLoading = false;
            options.EnableDefaultPluginPaths = false;
            options.RuntimePluginPaths.Add(pluginRoot);

            var testAssembly = Assembly.GetExecutingAssembly();
            var sourceAssemblyPath = testAssembly.Location;
            var entryAssemblyName = Path.GetFileName(sourceAssemblyPath);
            File.Copy(sourceAssemblyPath, Path.Combine(pluginFolder, entryAssemblyName), overwrite: true);
            var entryType = typeof(PluginFolderLoaderTests.PluginFolderLoaderSyntheticBackgroundDependencyPack).FullName;
            Assert.False(string.IsNullOrWhiteSpace(entryType));

            File.WriteAllText(Path.Combine(pluginFolder, "ix-plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "pluginId": "ops-bundle",
              "displayName": "Ops Bundle",
              "version": "1.2.3",
              "packIds": ["plugin_loader_synthetic_background_dependency"],
              "defaultEnabled": true,
              "sourceKind": "closed_source",
              "entryAssembly": "{{entryAssemblyName}}",
              "entryType": "{{entryType}}",
              "tools": [
                {
                  "name": "plugin_loader_synthetic_background_operational",
                  "description": "Synthetic deferred operational tool.",
                  "category": "inventory",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                },
                {
                  "name": "plugin_loader_synthetic_background_helper",
                  "description": "Synthetic deferred helper tool.",
                  "category": "diagnostic",
                  "supportsLocalExecution": true,
                  "supportsRemoteExecution": false
                }
              ]
            }
            """);

            var session = new ChatServiceSession(options, Stream.Null);
            session.SetCachedToolDefinitionsForTesting(new[] {
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_background_operational",
                    Description = "Synthetic deferred operational tool.",
                    PackId = "plugin_loader_synthetic_background_dependency"
                },
                new ToolDefinitionDto {
                    Name = "plugin_loader_synthetic_background_helper",
                    Description = "Synthetic deferred helper tool.",
                    PackId = "plugin_loader_synthetic_background_dependency"
                }
            });
            const string threadId = "thread-background-dependency-deferred-plugin";
            var definitions = new[] {
                new ToolDefinition(
                    name: "seed_background_dependency_followup",
                    description: "Seed a deferred background dependency follow-up.",
                    handoff: new ToolHandoffContract {
                        IsHandoffAware = true,
                        OutboundRoutes = new[] {
                            new ToolHandoffRoute {
                                TargetPackId = "plugin_loader_synthetic_background_dependency",
                                TargetToolName = "plugin_loader_synthetic_background_operational",
                                TargetRole = ToolRoutingTaxonomy.RoleOperational,
                                FollowUpKind = ToolHandoffFollowUpKinds.Verification,
                                FollowUpPriority = ToolHandoffFollowUpPriorities.High,
                                Bindings = new[] {
                                    new ToolHandoffBinding {
                                        SourceField = "target",
                                        TargetArgument = "target"
                                    }
                                }
                            }
                        }
                    }),
                new ToolDefinition(
                    "plugin_loader_synthetic_background_operational",
                    "Synthetic deferred operational tool.",
                    ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties(),
                    authentication: new ToolAuthenticationContract {
                        IsAuthenticationAware = true,
                        RequiresAuthentication = true,
                        AuthenticationContractId = "ix.auth.runtime.v1",
                        Mode = ToolAuthenticationMode.ProfileReference,
                        ProfileIdArgumentName = "profile_id",
                        SupportsConnectivityProbe = true,
                        ProbeToolName = "plugin_loader_synthetic_background_helper"
                    }),
                new ToolDefinition(
                    "plugin_loader_synthetic_background_helper",
                    "Synthetic deferred helper tool.",
                    ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties())
            };

            session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(definitions));
            session.RememberToolHandoffBackgroundWorkForTesting(
                threadId,
                definitions,
                new[] {
                    new ToolCallDto {
                        CallId = "call-background-recovery-deferred-plugin",
                        Name = "seed_background_dependency_followup",
                        ArgumentsJson = """{"target":"srv-auth.contoso.com"}"""
                    }
                },
                new[] {
                    new ToolOutputDto {
                        CallId = "call-background-recovery-deferred-plugin",
                        Ok = true,
                        Output = """{"ok":true}"""
                    }
                });

            var helperItem = Assert.Single(
                session.ResolveThreadBackgroundWorkSnapshotForTesting(threadId).Items,
                static item => string.Equals(item.TargetToolName, "plugin_loader_synthetic_background_helper", StringComparison.OrdinalIgnoreCase));
            Assert.True(session.TrySetThreadBackgroundWorkItemStateForTesting(threadId, helperItem.Id, "running"));
            session.RememberBackgroundWorkExecutionOutcomeForTesting(
                threadId,
                helperItem.Id,
                "call-background-recovery-helper",
                new[] {
                    new ToolOutputDto {
                        CallId = "call-background-recovery-helper",
                        Ok = false,
                        ErrorCode = "authentication_failed",
                        Error = "Missing runtime auth profile.",
                        Output = """{"ok":false}"""
                    }
                });

            var built = session.TryBuildBackgroundWorkDependencyRecoveryPromptForTesting(
                threadId,
                "continue",
                "I can keep going with the prepared follow-up.",
                Array.Empty<ToolDefinition>(),
                out var prompt,
                out var reason);

            Assert.True(built);
            Assert.Equal("background_prerequisite_auth_context_required", reason);
            Assert.Contains("profile_id", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("plugin_loader_synthetic_background_helper", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.Null(session.GetStartupToolingBootstrapTaskForTesting());
            Assert.DoesNotContain("plugin_loader_synthetic_background_operational", session.GetRegisteredToolNamesForTesting(), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("plugin_loader_synthetic_background_helper", session.GetRegisteredToolNamesForTesting(), StringComparer.OrdinalIgnoreCase);
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

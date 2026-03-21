using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceToolingBootstrapTests {
    private static string CreateToolingBootstrapCachePath(string namePrefix) {
        return TempPathTestHelper.CreateTempFilePath("ix-chat-" + namePrefix, ".json");
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
        Assert.Equal(StartupBootstrapContracts.PhasePackLoadId, startupBootstrap.Phases[2].Id);
        Assert.Equal(StartupBootstrapContracts.PhasePackRegisterId, startupBootstrap.Phases[3].Id);
        Assert.Equal(StartupBootstrapContracts.PhaseRegistryFinalizeId, startupBootstrap.Phases[4].Id);
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
}

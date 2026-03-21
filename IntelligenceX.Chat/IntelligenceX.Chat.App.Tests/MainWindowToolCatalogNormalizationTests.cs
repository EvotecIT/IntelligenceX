using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards canonical pack-id normalization when the desktop app ingests tool catalog payloads.
/// </summary>
public sealed class MainWindowToolCatalogNormalizationTests {
    private static readonly MethodInfo UpdateToolCatalogMethod = typeof(MainWindow).GetMethod(
        "UpdateToolCatalog",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("UpdateToolCatalog not found.");

    private static readonly MethodInfo BuildToolCatalogExecutionSummaryMethod = typeof(MainWindow).GetMethod(
        "BuildToolCatalogExecutionSummary",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildToolCatalogExecutionSummary not found.");

    private static readonly MethodInfo BuildToolStateMethod = typeof(MainWindow).GetMethod(
        "BuildToolState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildToolState not found.");

    private static readonly MethodInfo ClearToolCatalogCacheMethod = typeof(MainWindow).GetMethod(
        "ClearToolCatalogCache",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ClearToolCatalogCache not found.");

    private static readonly MethodInfo BuildOptionsStateJsonMethod = typeof(MainWindow).GetMethod(
        "BuildOptionsStateJson",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildOptionsStateJson not found.");

    private static readonly FieldInfo SessionPolicyField = typeof(MainWindow).GetField(
        "_sessionPolicy",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_sessionPolicy field not found.");

    private static readonly FieldInfo ToolCatalogPacksField = typeof(MainWindow).GetField(
        "_toolCatalogPacks",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCatalogPacks field not found.");

    private static readonly FieldInfo ToolCatalogPluginsField = typeof(MainWindow).GetField(
        "_toolCatalogPlugins",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCatalogPlugins field not found.");

    private static readonly FieldInfo ToolCatalogRoutingCatalogField = typeof(MainWindow).GetField(
        "_toolCatalogRoutingCatalog",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCatalogRoutingCatalog field not found.");

    private static readonly FieldInfo ToolCatalogCapabilitySnapshotField = typeof(MainWindow).GetField(
        "_toolCatalogCapabilitySnapshot",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCatalogCapabilitySnapshot field not found.");

    private static readonly FieldInfo ToolDescriptionsField = typeof(MainWindow).GetField(
        "_toolDescriptions",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolDescriptions field not found.");

    private static readonly FieldInfo ToolDisplayNamesField = typeof(MainWindow).GetField(
        "_toolDisplayNames",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolDisplayNames field not found.");

    private static readonly FieldInfo ToolCategoriesField = typeof(MainWindow).GetField(
        "_toolCategories",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCategories field not found.");

    private static readonly FieldInfo ToolTagsField = typeof(MainWindow).GetField(
        "_toolTags",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolTags field not found.");

    private static readonly FieldInfo ToolPackIdsField = typeof(MainWindow).GetField(
        "_toolPackIds",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolPackIds field not found.");

    private static readonly FieldInfo ToolPackNamesField = typeof(MainWindow).GetField(
        "_toolPackNames",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolPackNames field not found.");

    private static readonly FieldInfo ToolParametersField = typeof(MainWindow).GetField(
        "_toolParameters",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolParameters field not found.");

    private static readonly FieldInfo ToolCatalogDefinitionsField = typeof(MainWindow).GetField(
        "_toolCatalogDefinitions",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCatalogDefinitions field not found.");

    private static readonly FieldInfo ToolWriteCapabilitiesField = typeof(MainWindow).GetField(
        "_toolWriteCapabilities",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolWriteCapabilities field not found.");

    private static readonly FieldInfo ToolExecutionAwarenessField = typeof(MainWindow).GetField(
        "_toolExecutionAwareness",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolExecutionAwareness field not found.");

    private static readonly FieldInfo ToolExecutionContractIdsField = typeof(MainWindow).GetField(
        "_toolExecutionContractIds",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolExecutionContractIds field not found.");

    private static readonly FieldInfo ToolExecutionScopesField = typeof(MainWindow).GetField(
        "_toolExecutionScopes",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolExecutionScopes field not found.");

    private static readonly FieldInfo ToolSupportsLocalExecutionField = typeof(MainWindow).GetField(
        "_toolSupportsLocalExecution",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolSupportsLocalExecution field not found.");

    private static readonly FieldInfo ToolSupportsRemoteExecutionField = typeof(MainWindow).GetField(
        "_toolSupportsRemoteExecution",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolSupportsRemoteExecution field not found.");

    private static readonly FieldInfo ToolStatesField = typeof(MainWindow).GetField(
        "_toolStates",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolStates field not found.");

    private static readonly FieldInfo ToolRoutingConfidenceField = typeof(MainWindow).GetField(
        "_toolRoutingConfidence",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingConfidence field not found.");

    private static readonly FieldInfo ToolRoutingReasonField = typeof(MainWindow).GetField(
        "_toolRoutingReason",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingReason field not found.");

    private static readonly FieldInfo ToolRoutingScoreField = typeof(MainWindow).GetField(
        "_toolRoutingScore",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingScore field not found.");

    private static readonly FieldInfo NativeAccountSlotsField = typeof(MainWindow).GetField(
        "_nativeAccountSlots",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_nativeAccountSlots field not found.");

    private static readonly FieldInfo ServiceProfileNamesField = typeof(MainWindow).GetField(
        "_serviceProfileNames",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_serviceProfileNames field not found.");

    private static readonly FieldInfo AppProfileNameField = typeof(MainWindow).GetField(
        "_appProfileName",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_appProfileName field not found.");

    private static readonly FieldInfo AvailableModelsField = typeof(MainWindow).GetField(
        "_availableModels",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_availableModels field not found.");

    private static readonly FieldInfo FavoriteModelsField = typeof(MainWindow).GetField(
        "_favoriteModels",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_favoriteModels field not found.");

    private static readonly FieldInfo RecentModelsField = typeof(MainWindow).GetField(
        "_recentModels",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_recentModels field not found.");

    private static readonly FieldInfo TurnDiagnosticsSyncField = typeof(MainWindow).GetField(
        "_turnDiagnosticsSync",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_turnDiagnosticsSync field not found.");

    private static readonly FieldInfo AccountUsageByKeyField = typeof(MainWindow).GetField(
        "_accountUsageByKey",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_accountUsageByKey field not found.");

    private static readonly FieldInfo MemoryDiagnosticsSyncField = typeof(MainWindow).GetField(
        "_memoryDiagnosticsSync",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_memoryDiagnosticsSync field not found.");

    private static readonly FieldInfo StartupMetadataSyncLockField = typeof(MainWindow).GetField(
        "_startupMetadataSyncLock",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_startupMetadataSyncLock field not found.");

    private static readonly FieldInfo MemoryDebugHistoryField = typeof(MainWindow).GetField(
        "_memoryDebugHistory",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_memoryDebugHistory field not found.");

    private static readonly FieldInfo AppStateField = typeof(MainWindow).GetField(
        "_appState",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_appState field not found.");

    private static readonly FieldInfo KnownProfilesField = typeof(MainWindow).GetField(
        "_knownProfiles",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_knownProfiles field not found.");

    private static readonly FieldInfo ConversationsField = typeof(MainWindow).GetField(
        "_conversations",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_conversations field not found.");

    private static readonly FieldInfo MessagesField = typeof(MainWindow).GetField(
        "_messages",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_messages field not found.");

    /// <summary>
    /// Ensures tool catalog ingest stores canonical shared Chat pack ids instead of raw alias labels.
    /// </summary>
    [Fact]
    public void UpdateToolCatalog_NormalizesPackIds_ToCanonicalContractIds() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "ad_search_users",
                Description = "Searches users.",
                PackId = "ADPlayground",
                PackName = "Active Directory",
                Category = "directory"
            },
            new ToolDefinitionDto {
                Name = "computer_inventory",
                Description = "Reads computer inventory.",
                PackId = "ComputerX",
                PackName = "System",
                Category = "system"
            },
            new ToolDefinitionDto {
                Name = "fs_scan",
                Description = "Scans filesystems.",
                PackId = "fs",
                PackName = "Filesystem",
                Category = "files"
            }
        };

        InvokeUpdateToolCatalog(window, tools);

        var packIds = Assert.IsType<Dictionary<string, string>>(ToolPackIdsField.GetValue(window));
        Assert.Equal("active_directory", packIds["ad_search_users"]);
        Assert.Equal("system", packIds["computer_inventory"]);
        Assert.Equal("filesystem", packIds["fs_scan"]);
    }

    /// <summary>
    /// Ensures tool catalog ingest preserves execution-locality metadata for downstream UI/self-knowledge surfaces.
    /// </summary>
    [Fact]
    public void UpdateToolCatalog_PreservesExecutionLocalityMetadata() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "eventlog_timeline_query",
                Description = "Query timeline from a host.",
                PackId = "eventlog",
                PackName = "Event Viewer",
                IsExecutionAware = true,
                ExecutionContractId = "ix.tool-execution.v1",
                ExecutionScope = "local_or_remote",
                SupportsLocalExecution = true,
                SupportsRemoteExecution = true
            },
            new ToolDefinitionDto {
                Name = "system_local_trace_query",
                Description = "Inspect local trace data.",
                PackId = "system",
                PackName = "System",
                IsExecutionAware = true,
                ExecutionContractId = "ix.tool-execution.v1",
                ExecutionScope = "local_only",
                SupportsLocalExecution = true,
                SupportsRemoteExecution = false
            }
        };

        InvokeUpdateToolCatalog(window, tools);

        var executionAwareness = Assert.IsType<Dictionary<string, bool>>(ToolExecutionAwarenessField.GetValue(window));
        var executionContractIds = Assert.IsType<Dictionary<string, string>>(ToolExecutionContractIdsField.GetValue(window));
        var executionScopes = Assert.IsType<Dictionary<string, string>>(ToolExecutionScopesField.GetValue(window));
        var supportsLocalExecution = Assert.IsType<Dictionary<string, bool>>(ToolSupportsLocalExecutionField.GetValue(window));
        var supportsRemoteExecution = Assert.IsType<Dictionary<string, bool>>(ToolSupportsRemoteExecutionField.GetValue(window));

        Assert.True(executionAwareness["eventlog_timeline_query"]);
        Assert.Equal("ix.tool-execution.v1", executionContractIds["eventlog_timeline_query"]);
        Assert.Equal("local_or_remote", executionScopes["eventlog_timeline_query"]);
        Assert.True(supportsLocalExecution["eventlog_timeline_query"]);
        Assert.True(supportsRemoteExecution["eventlog_timeline_query"]);

        Assert.True(executionAwareness["system_local_trace_query"]);
        Assert.Equal("local_only", executionScopes["system_local_trace_query"]);
        Assert.True(supportsLocalExecution["system_local_trace_query"]);
        Assert.False(supportsRemoteExecution["system_local_trace_query"]);
    }

    /// <summary>
    /// Ensures remote-only tools normalize correctly even when callers omit the explicit execution scope string.
    /// </summary>
    [Fact]
    public void UpdateToolCatalog_InferRemoteOnlyScope_WhenOnlyRemoteExecutionIsSupported() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "ad_remote_domain_query",
                Description = "Query remote directory state.",
                PackId = "adplayground",
                PackName = "Active Directory",
                IsExecutionAware = true,
                ExecutionContractId = "ix.tool-execution.v1",
                ExecutionScope = "",
                SupportsLocalExecution = false,
                SupportsRemoteExecution = true
            }
        };

        InvokeUpdateToolCatalog(window, tools);

        var executionScopes = Assert.IsType<Dictionary<string, string>>(ToolExecutionScopesField.GetValue(window));
        var supportsLocalExecution = Assert.IsType<Dictionary<string, bool>>(ToolSupportsLocalExecutionField.GetValue(window));
        var supportsRemoteExecution = Assert.IsType<Dictionary<string, bool>>(ToolSupportsRemoteExecutionField.GetValue(window));

        Assert.Equal("remote_only", executionScopes["ad_remote_domain_query"]);
        Assert.False(supportsLocalExecution["ad_remote_domain_query"]);
        Assert.True(supportsRemoteExecution["ad_remote_domain_query"]);
    }

    /// <summary>
    /// Ensures execution locality summaries only count tools that explicitly declare execution contracts.
    /// </summary>
    [Fact]
    public void BuildToolCatalogExecutionSummary_IgnoresToolsWithoutExecutionContracts() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "eventlog_timeline_query",
                Description = "Query timeline from a host.",
                PackId = "eventlog",
                PackName = "Event Viewer",
                IsExecutionAware = true,
                ExecutionContractId = "ix.tool-execution.v1",
                ExecutionScope = "local_only",
                SupportsLocalExecution = true,
                SupportsRemoteExecution = false
            },
            new ToolDefinitionDto {
                Name = "legacy_remote_helper",
                Description = "Legacy helper with no execution contract.",
                PackId = "system",
                PackName = "System",
                IsExecutionAware = false,
                ExecutionScope = "",
                SupportsLocalExecution = false,
                SupportsRemoteExecution = true
            }
        };

        InvokeUpdateToolCatalog(window, tools);

        var summary = InvokeBuildToolCatalogExecutionSummary(window);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.ExecutionAwareToolCount);
        Assert.Equal(1, summary.LocalOnlyToolCount);
        Assert.Equal(0, summary.RemoteOnlyToolCount);
        Assert.Equal(0, summary.LocalOrRemoteToolCount);
        Assert.Equal(new[] { "eventlog" }, summary.LocalOnlyPackIds);
        Assert.Empty(summary.RemoteCapablePackIds);
    }

    /// <summary>
    /// Ensures the desktop app preserves autonomy metadata when projecting tool catalogs into UI state.
    /// </summary>
    [Fact]
    public void BuildToolState_PreservesAutonomyContractMetadata_FromToolCatalogDtos() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "ad_environment_discover",
                Description = "Discovers the current AD environment.",
                DisplayName = "AD Environment Discover",
                Category = "directory",
                Tags = new[] { "bootstrap", "remote", "bootstrap" },
                PackId = "ADPlayground",
                PackName = "Active Directory",
                PackDescription = "AD analysis and discovery",
                PackSourceKind = ToolPackSourceKind.ClosedSource,
                IsEnvironmentDiscoverTool = true,
                IsPackInfoTool = false,
                ExecutionScope = "local_or_remote",
                SupportsTargetScoping = true,
                TargetScopeArguments = new[] { "domain_controller", "search_base_dn", "domain_controller" },
                SupportsRemoteHostTargeting = true,
                RemoteHostArguments = new[] { "domain_controller", "server" },
                IsSetupAware = true,
                SetupToolName = " ad_environment_discover ",
                IsHandoffAware = true,
                HandoffTargetPackIds = new[] { "System", "eventlog", "system" },
                HandoffTargetToolNames = new[] { "system_info", "eventlog_channels_list" },
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 2,
                RecoveryToolNames = new[] { "ad_environment_discover", "system_info" },
                RequiredArguments = new[] { "domain_controller", "search_base_dn" },
                Parameters = new[] {
                    new ToolParameterDto { Name = "domain_controller", Type = "string", Required = true },
                    new ToolParameterDto { Name = "search_base_dn", Type = "string", Required = false }
                }
            }
        };

        InvokeUpdateToolCatalog(window, tools);

        var toolState = InvokeBuildToolState(window);
        var tool = Assert.Single(toolState);

        Assert.Equal("active_directory", GetProperty<string>(tool, "packId"));
        Assert.Equal("Active Directory", GetProperty<string>(tool, "packName"));
        Assert.Equal("AD analysis and discovery", GetProperty<string>(tool, "packDescription"));
        Assert.Equal("closed_source", GetProperty<string>(tool, "packSourceKind"));
        Assert.False(GetProperty<bool>(tool, "isPackInfoTool"));
        Assert.True(GetProperty<bool>(tool, "isEnvironmentDiscoverTool"));
        Assert.Equal("local_or_remote", GetProperty<string>(tool, "executionScope"));
        Assert.True(GetProperty<bool>(tool, "supportsTargetScoping"));
        Assert.Equal(new[] { "domain_controller", "search_base_dn" }, GetProperty<string[]>(tool, "targetScopeArguments"));
        Assert.True(GetProperty<bool>(tool, "supportsRemoteHostTargeting"));
        Assert.Equal(new[] { "domain_controller", "server" }, GetProperty<string[]>(tool, "remoteHostArguments"));
        Assert.True(GetProperty<bool>(tool, "isSetupAware"));
        Assert.Equal("ad_environment_discover", GetProperty<string>(tool, "setupToolName"));
        Assert.True(GetProperty<bool>(tool, "isHandoffAware"));
        Assert.Equal(new[] { "eventlog", "system" }, GetProperty<string[]>(tool, "handoffTargetPackIds"));
        Assert.Equal(new[] { "eventlog_channels_list", "system_info" }, GetProperty<string[]>(tool, "handoffTargetToolNames"));
        Assert.True(GetProperty<bool>(tool, "isRecoveryAware"));
        Assert.True(GetProperty<bool>(tool, "supportsTransientRetry"));
        Assert.Equal(2, GetProperty<int>(tool, "maxRetryAttempts"));
        Assert.Equal(new[] { "ad_environment_discover", "system_info" }, GetProperty<string[]>(tool, "recoveryToolNames"));
        Assert.Equal(new[] { "domain_controller", "search_base_dn" }, GetProperty<string[]>(tool, "requiredArguments"));
    }

    /// <summary>
    /// Ensures tool-cache invalidation clears stale per-tool contracts while preserving still-valid hello metadata.
    /// </summary>
    [Fact]
    public void ClearToolCatalogCache_ClearsStaleToolDefinitions_WithoutDroppingLiveMetadata() {
        var window = CreateWindow();
        var tools = new[] {
            new ToolDefinitionDto {
                Name = "system_metrics_summary",
                Description = "Collect system metrics.",
                PackId = "system",
                PackName = "System",
                IsExecutionAware = true,
                ExecutionScope = "local_or_remote",
                SupportsLocalExecution = true,
                SupportsRemoteExecution = true
            }
        };

        InvokeUpdateToolCatalog(
            window,
            tools,
            routingCatalog: new SessionRoutingCatalogDiagnosticsDto {
                TotalTools = 1
            },
            packs: new[] {
                new ToolPackInfoDto {
                    Id = "system",
                    Name = "System",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            capabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
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
                HealthyTools = new[] { "system_metrics_summary" }
            });

        try {
            ClearToolCatalogCacheMethod.Invoke(window, new object?[] { false });
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }

        Assert.Empty(Assert.IsType<Dictionary<string, ToolDefinitionDto>>(ToolCatalogDefinitionsField.GetValue(window)));
        Assert.Empty(Assert.IsType<Dictionary<string, string>>(ToolDescriptionsField.GetValue(window)));
        Assert.Empty(Assert.IsType<Dictionary<string, bool>>(ToolStatesField.GetValue(window)));

        var packs = Assert.IsType<ToolPackInfoDto[]>(ToolCatalogPacksField.GetValue(window));
        Assert.Single(packs);
        Assert.NotNull(ToolCatalogRoutingCatalogField.GetValue(window));
        Assert.NotNull(ToolCatalogCapabilitySnapshotField.GetValue(window));
    }

    /// <summary>
    /// Ensures full catalog invalidation also clears pack-level routing and capability snapshot metadata.
    /// </summary>
    [Fact]
    public void ClearToolCatalogCache_WithMetadataFlag_ClearsPackAndSnapshotMetadata() {
        var window = CreateWindow();
        SetField(ToolCatalogPacksField, window, new[] {
            new ToolPackInfoDto {
                Id = "system",
                Name = "System",
                Tier = CapabilityTier.ReadOnly,
                Enabled = true,
                IsDangerous = false
            }
        });
        SetField(
            ToolCatalogRoutingCatalogField,
            window,
            new SessionRoutingCatalogDiagnosticsDto {
                TotalTools = 1
            });
        SetField(
            ToolCatalogCapabilitySnapshotField,
            window,
            new SessionCapabilitySnapshotDto {
                RegisteredTools = 1,
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
            });

        try {
            ClearToolCatalogCacheMethod.Invoke(window, new object?[] { true });
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }

        Assert.Empty(Assert.IsType<ToolPackInfoDto[]>(ToolCatalogPacksField.GetValue(window)));
        Assert.Null(ToolCatalogRoutingCatalogField.GetValue(window));
        Assert.Null(ToolCatalogCapabilitySnapshotField.GetValue(window));
    }

    /// <summary>
    /// Ensures options-state fallback plugins use the same resolved metadata source as session policy plugins.
    /// </summary>
    [Fact]
    public void BuildOptionsStateJson_UsesResolvedPluginMetadata_ForToolCatalogFallback() {
        var window = CreateWindow();
        SetField(
            SessionPolicyField,
            window,
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 4,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 0,
                    PluginCount = 1,
                    EnabledPluginCount = 1,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                        Source = "service_runtime",
                        Packs = Array.Empty<ToolPackInfoDto>(),
                        Plugins = new[] {
                            new PluginInfoDto {
                                Id = "ops_bundle",
                                Name = "Ops Bundle",
                                Enabled = true,
                                DefaultEnabled = true,
                                Origin = "plugin_folder",
                                SourceKind = ToolPackSourceKind.ClosedSource,
                                IsDangerous = false
                            }
                        }
                    }
                }
            });
        SetField(
            ToolCatalogPluginsField,
            window,
            new[] {
                new PluginInfoDto {
                    Id = "preview_plugin",
                    Name = "Preview Plugin",
                    Enabled = true,
                    DefaultEnabled = false,
                    Origin = "preview",
                    IsDangerous = false
                }
            });

        string json;
        try {
            json = Assert.IsType<string>(BuildOptionsStateJsonMethod.Invoke(window, Array.Empty<object>()));
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("ops_bundle", root.GetProperty("toolCatalogPlugins")[0].GetProperty("id").GetString());
        Assert.Equal("ops_bundle", root.GetProperty("policy").GetProperty("plugins")[0].GetProperty("id").GetString());
        Assert.DoesNotContain("preview_plugin", json, StringComparison.Ordinal);
    }

    private static MainWindow CreateWindow() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        SetField(NativeAccountSlotsField, window, new[] { string.Empty });
        SetField(AppProfileNameField, window, "default");
        SetField(ServiceProfileNamesField, window, Array.Empty<string>());
        SetField(AvailableModelsField, window, Array.Empty<ModelInfoDto>());
        SetField(FavoriteModelsField, window, Array.Empty<string>());
        SetField(RecentModelsField, window, Array.Empty<string>());
        SetField(ToolCatalogPacksField, window, Array.Empty<ToolPackInfoDto>());
        SetField(ToolCatalogPluginsField, window, Array.Empty<PluginInfoDto>());
        SetField(ToolCatalogRoutingCatalogField, window, null!);
        SetField(ToolCatalogCapabilitySnapshotField, window, null!);
        SetField(TurnDiagnosticsSyncField, window, new object());
        SetField(AccountUsageByKeyField, window, Activator.CreateInstance(AccountUsageByKeyField.FieldType)!);
        SetField(MemoryDiagnosticsSyncField, window, new object());
        SetField(StartupMetadataSyncLockField, window, new object());
        SetField(MemoryDebugHistoryField, window, Activator.CreateInstance(MemoryDebugHistoryField.FieldType)!);
        SetField(AppStateField, window, new ChatAppState());
        SetField(KnownProfilesField, window, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetField(ConversationsField, window, Activator.CreateInstance(ConversationsField.FieldType)!);
        SetField(MessagesField, window, Activator.CreateInstance(MessagesField.FieldType)!);
        SetField(ToolDescriptionsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolDisplayNamesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolCategoriesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolTagsField, window, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolPackIdsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolPackNamesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolParametersField, window, new Dictionary<string, ToolParameterDto[]>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolCatalogDefinitionsField, window, new Dictionary<string, ToolDefinitionDto>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolWriteCapabilitiesField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolExecutionAwarenessField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolExecutionContractIdsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolExecutionScopesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolSupportsLocalExecutionField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolSupportsRemoteExecutionField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolStatesField, window, new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolRoutingConfidenceField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolRoutingReasonField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolRoutingScoreField, window, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
        return window;
    }

    private static void InvokeUpdateToolCatalog(
        MainWindow window,
        ToolDefinitionDto[] tools,
        SessionRoutingCatalogDiagnosticsDto? routingCatalog = null,
        ToolPackInfoDto[]? packs = null,
        PluginInfoDto[]? plugins = null,
        SessionCapabilitySnapshotDto? capabilitySnapshot = null) {
        try {
            UpdateToolCatalogMethod.Invoke(window, new object?[] { tools, routingCatalog, packs, plugins, capabilitySnapshot });
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static ToolCatalogExecutionSummary? InvokeBuildToolCatalogExecutionSummary(MainWindow window) {
        try {
            return (ToolCatalogExecutionSummary?)BuildToolCatalogExecutionSummaryMethod.Invoke(window, Array.Empty<object>());
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static object[] InvokeBuildToolState(MainWindow window) {
        try {
            return Assert.IsType<object[]>(BuildToolStateMethod.Invoke(window, Array.Empty<object>()));
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static T GetProperty<T>(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        return Assert.IsType<T>(property.GetValue(instance));
    }

    private static void SetField(FieldInfo field, MainWindow window, object value) {
        field.SetValue(window, value);
    }
}

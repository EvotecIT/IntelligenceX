using IntelligenceX.Chat.App;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests deferred startup metadata rerun scheduling decisions.
/// </summary>
public sealed class MainWindowStartupMetadataSyncRerunTests {
    private static readonly MethodInfo ApplyHelloPolicyToolCatalogPreviewMethod = typeof(MainWindow).GetMethod(
        "ApplyHelloPolicyToolCatalogPreview",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ApplyHelloPolicyToolCatalogPreview method not found.");
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
    private static readonly FieldInfo ToolCatalogDefinitionsField = typeof(MainWindow).GetField(
        "_toolCatalogDefinitions",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolCatalogDefinitions field not found.");
    private static readonly FieldInfo ToolParametersField = typeof(MainWindow).GetField(
        "_toolParameters",
        BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolParameters field not found.");
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

    private static SessionPolicyDto CreatePolicy(
        SessionStartupBootstrapTelemetryDto? startupBootstrap = null,
        ToolPackInfoDto[]? packs = null,
        PluginInfoDto[]? plugins = null,
        SessionCapabilitySnapshotDto? capabilitySnapshot = null,
        string[]? startupWarnings = null) {
        return new SessionPolicyDto {
            ReadOnly = true,
            DangerousToolsEnabled = false,
            MaxToolRounds = 8,
            ParallelTools = true,
            AllowMutatingParallelToolCalls = false,
            Packs = packs ?? Array.Empty<ToolPackInfoDto>(),
            Plugins = plugins ?? Array.Empty<PluginInfoDto>(),
            CapabilitySnapshot = capabilitySnapshot,
            StartupWarnings = startupWarnings ?? Array.Empty<string>(),
            StartupBootstrap = startupBootstrap
        };
    }

    /// <summary>
    /// Ensures busy metadata sync requests ask for rerun only when explicitly requested.
    /// </summary>
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldRequestDeferredStartupMetadataSyncRerun_ReturnsExpectedValue(
        bool metadataSyncAlreadyQueued,
        bool requestRerunIfBusy,
        bool expected) {
        var shouldRerun = MainWindow.ShouldRequestDeferredStartupMetadataSyncRerun(
            metadataSyncAlreadyQueued,
            requestRerunIfBusy);
        Assert.Equal(expected, shouldRerun);
    }

    /// <summary>
    /// Ensures deferred metadata sync rerun dispatch runs only when requested and still safe.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, false, true, false)]
    public void ShouldDispatchDeferredStartupMetadataSyncRerun_ReturnsExpectedValue(
        bool rerunRequested,
        bool shutdownRequested,
        bool isConnected,
        bool expected) {
        var shouldDispatch = MainWindow.ShouldDispatchDeferredStartupMetadataSyncRerun(
            rerunRequested,
            shutdownRequested,
            isConnected);
        Assert.Equal(expected, shouldDispatch);
    }

    /// <summary>
    /// Ensures phase-failure recovery rerun is only requested when startup metadata sync is
    /// connected/safe and a critical phase (`hello` or `list_tools`) did not complete.
    /// </summary>
    [Theory]
    [InlineData(true, false, true, true, 0, 1, false)]
    [InlineData(true, false, false, true, 0, 1, true)]
    [InlineData(true, false, true, false, 0, 1, true)]
    [InlineData(true, false, false, false, 0, 1, true)]
    [InlineData(true, false, false, true, 1, 1, false)]
    [InlineData(true, true, false, true, 0, 1, false)]
    [InlineData(false, false, false, true, 0, 1, false)]
    [InlineData(true, false, false, true, 0, 0, false)]
    public void ShouldRequestDeferredStartupMetadataFailureRecoveryRerun_ReturnsExpectedValue(
        bool isConnected,
        bool shutdownRequested,
        bool helloPhaseSucceeded,
        bool toolCatalogPhaseSucceeded,
        int retriesConsumed,
        int retryLimit,
        bool expected) {
        var shouldRequest = MainWindow.ShouldRequestDeferredStartupMetadataFailureRecoveryRerun(
            isConnected: isConnected,
            shutdownRequested: shutdownRequested,
            helloPhaseSucceeded: helloPhaseSucceeded,
            toolCatalogPhaseSucceeded: toolCatalogPhaseSucceeded,
            retriesConsumed: retriesConsumed,
            retryLimit: retryLimit);

        Assert.Equal(expected, shouldRequest);
    }

    /// <summary>
    /// Ensures persisted-preview replacement rerun is requested only when metadata sync succeeded,
    /// startup cache mode indicates persisted preview, and retry budget remains.
    /// </summary>
    [Theory]
    [InlineData(true, 3, true, false, 0, 8, true)]
    [InlineData(true, 2, true, false, 0, 8, false)]
    [InlineData(false, 3, true, false, 0, 8, false)]
    [InlineData(true, 3, false, false, 0, 8, false)]
    [InlineData(true, 3, true, true, 0, 8, false)]
    [InlineData(true, 3, true, false, 8, 8, false)]
    [InlineData(true, 3, true, false, 0, 0, false)]
    public void ShouldRequestDeferredStartupMetadataPersistedPreviewRefreshRerun_ReturnsExpectedValue(
        bool metadataSyncSucceeded,
        int startupBootstrapCacheMode,
        bool isConnected,
        bool shutdownRequested,
        int retriesConsumed,
        int retryLimit,
        bool expected) {
        var shouldRequest = MainWindow.ShouldRequestDeferredStartupMetadataPersistedPreviewRefreshRerun(
            metadataSyncSucceeded: metadataSyncSucceeded,
            startupBootstrapCacheMode: startupBootstrapCacheMode,
            isConnected: isConnected,
            shutdownRequested: shutdownRequested,
            retriesConsumed: retriesConsumed,
            retryLimit: retryLimit);

        Assert.Equal(expected, shouldRequest);
    }

    /// <summary>
    /// Ensures persisted-preview refresh retry-limit detection is only active when startup
    /// metadata sync succeeded and persisted-preview mode is still active.
    /// </summary>
    [Theory]
    [InlineData(true, 3, 8, 8, true)]
    [InlineData(true, 3, 9, 8, true)]
    [InlineData(true, 3, 7, 8, false)]
    [InlineData(true, 2, 8, 8, false)]
    [InlineData(false, 3, 8, 8, false)]
    [InlineData(true, 3, 8, 0, false)]
    public void HasReachedDeferredStartupMetadataPersistedPreviewRefreshRetryLimit_ReturnsExpectedValue(
        bool metadataSyncSucceeded,
        int startupBootstrapCacheMode,
        int retriesConsumed,
        int retryLimit,
        bool expected) {
        var reached = MainWindow.HasReachedDeferredStartupMetadataPersistedPreviewRefreshRetryLimit(
            metadataSyncSucceeded: metadataSyncSucceeded,
            startupBootstrapCacheMode: startupBootstrapCacheMode,
            retriesConsumed: retriesConsumed,
            retryLimit: retryLimit);

        Assert.Equal(expected, reached);
    }

    /// <summary>
    /// Ensures startup metadata failure kind labeling remains deterministic for diagnostics.
    /// </summary>
    [Theory]
    [InlineData(true, true, "none")]
    [InlineData(false, true, "hello")]
    [InlineData(true, false, "list_tools")]
    [InlineData(false, false, "hello_and_list_tools")]
    public void ResolveDeferredStartupMetadataFailureKind_ReturnsExpectedToken(
        bool helloPhaseSucceeded,
        bool toolCatalogPhaseSucceeded,
        string expectedToken) {
        var token = MainWindow.ResolveDeferredStartupMetadataFailureKind(
            helloPhaseSucceeded,
            toolCatalogPhaseSucceeded);

        Assert.Equal(expectedToken, token);
    }

    /// <summary>
    /// Ensures persisted-preview hello policy can satisfy first-paint tooling metadata when descriptor snapshot data is already present.
    /// </summary>
    [Fact]
    public void ShouldSatisfyStartupToolCatalogFromHelloPolicy_ReturnsTrueForPersistedPreviewSnapshot() {
        var shouldSatisfy = MainWindow.ShouldSatisfyStartupToolCatalogFromHelloPolicy(
            CreatePolicy(
                startupBootstrap: new SessionStartupBootstrapTelemetryDto {
                    TotalMs = 1,
                    Phases = new[] {
                        StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorCacheHitId, 1, 1)
                    }
                },
                capabilitySnapshot: new SessionCapabilitySnapshotDto {
                    RegisteredTools = 0,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 0,
                    ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                        Source = "persisted_preview",
                        Packs = new[] {
                            new ToolPackInfoDto {
                                Id = "eventlog",
                                Name = "EventLog",
                                Tier = CapabilityTier.ReadOnly,
                                Enabled = true,
                                IsDangerous = false
                            }
                        },
                        Plugins = Array.Empty<PluginInfoDto>()
                    }
                }));

        Assert.True(shouldSatisfy);
    }

    /// <summary>
    /// Ensures startup does not skip inline tool-catalog sync when persisted-preview markers exist without usable tooling metadata.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldSatisfyStartupToolCatalogFromHelloPolicy_ReturnsFalseWithoutUsableMetadata(
        bool persistedPreview) {
        var startupBootstrap = persistedPreview
            ? new SessionStartupBootstrapTelemetryDto {
                TotalMs = 1,
                Phases = new[] {
                    StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorCacheHitId, 1, 1)
                }
            }
            : new SessionStartupBootstrapTelemetryDto {
                TotalMs = 250,
                Phases = new[] {
                    StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorDiscoveryId, 250, 1)
                }
            };

        var shouldSatisfy = MainWindow.ShouldSatisfyStartupToolCatalogFromHelloPolicy(
            CreatePolicy(startupBootstrap: startupBootstrap));

        Assert.False(shouldSatisfy);
    }

    /// <summary>
    /// Ensures persisted-preview hello policy clears stale tool definitions before treating inline list_tools as satisfied.
    /// </summary>
    [Fact]
    public void ApplyHelloPolicyToolCatalogPreview_ClearsStaleDefinitions_WhenSatisfyingPersistedPreview() {
        var window = CreateWindowForHelloPolicyPreview();
        var staleDefinitions = GetToolCatalogDefinitions(window);
        staleDefinitions["stale_tool"] = new ToolDefinitionDto {
            Name = "stale_tool",
            Description = "Stale",
            PackId = "stale_pack"
        };

        var policy = CreatePolicy(
            startupBootstrap: new SessionStartupBootstrapTelemetryDto {
                TotalMs = 1,
                Phases = new[] {
                    StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorCacheHitId, 1, 1)
                }
            },
            packs: new[] {
                new ToolPackInfoDto {
                    Id = "eventlog",
                    Name = "EventLog",
                    Tier = CapabilityTier.ReadOnly,
                    Enabled = true,
                    IsDangerous = false
                }
            },
            plugins: new[] {
                new PluginInfoDto {
                    Id = "ops_bundle",
                    Name = "Ops Bundle",
                    Enabled = true,
                    Origin = "plugin_folder",
                    IsDangerous = false
                }
            },
            capabilitySnapshot: new SessionCapabilitySnapshotDto {
                RegisteredTools = 0,
                EnabledPackCount = 1,
                PluginCount = 1,
                EnabledPluginCount = 1,
                ToolingAvailable = true,
                AllowedRootCount = 0,
                ToolingSnapshot = new SessionCapabilityToolingSnapshotDto {
                    Source = "persisted_preview",
                    Packs = new[] {
                        new ToolPackInfoDto {
                            Id = "eventlog",
                            Name = "EventLog",
                            Tier = CapabilityTier.ReadOnly,
                            Enabled = true,
                            IsDangerous = false
                        }
                    },
                    Plugins = new[] {
                        new PluginInfoDto {
                            Id = "ops_bundle",
                            Name = "Ops Bundle",
                            Enabled = true,
                            Origin = "plugin_folder",
                            IsDangerous = false
                        }
                    }
                }
            });

        InvokeApplyHelloPolicyToolCatalogPreview(window, policy, clearExistingToolDefinitions: true);

        Assert.Empty(GetToolCatalogDefinitions(window));
        var packs = Assert.IsType<ToolPackInfoDto[]>(ToolCatalogPacksField.GetValue(window));
        var plugins = Assert.IsType<PluginInfoDto[]>(ToolCatalogPluginsField.GetValue(window));
        var capabilitySnapshot = Assert.IsType<SessionCapabilitySnapshotDto>(ToolCatalogCapabilitySnapshotField.GetValue(window));
        Assert.Equal("eventlog", Assert.Single(packs).Id);
        Assert.Equal("ops_bundle", Assert.Single(plugins).Id);
        Assert.Equal("persisted_preview", capabilitySnapshot.ToolingSnapshot?.Source);
    }

    /// <summary>
    /// Ensures failure-recovery retry budget is consumed atomically and capped by configured limit.
    /// </summary>
    [Fact]
    public void TryConsumeDeferredStartupMetadataFailureRecoveryRetry_RespectsRetryLimit() {
        var retriesConsumed = 0;

        var first = MainWindow.TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
            ref retriesConsumed,
            retryLimit: 1);
        var second = MainWindow.TryConsumeDeferredStartupMetadataFailureRecoveryRetry(
            ref retriesConsumed,
            retryLimit: 1);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(1, retriesConsumed);
    }

    /// <summary>
    /// Ensures deferred startup metadata phases retry on timeout/cancel/disconnect-class transient failures.
    /// </summary>
    [Theory]
    [InlineData("timeout", true)]
    [InlineData("cancel", true)]
    [InlineData("disconnected", true)]
    [InlineData("generic", false)]
    public void ShouldRetryDeferredStartupMetadataPhaseAttempt_ReturnsExpectedValue(
        string exceptionKind,
        bool expected) {
        Exception ex = exceptionKind switch {
            "timeout" => new TimeoutException("phase timeout"),
            "cancel" => new OperationCanceledException("phase canceled"),
            "disconnected" => new InvalidOperationException("Not connected to runtime."),
            _ => new InvalidOperationException("invalid request")
        };

        var shouldRetry = MainWindow.ShouldRetryDeferredStartupMetadataPhaseAttempt(ex);

        Assert.Equal(expected, shouldRetry);
    }

    private static void InvokeApplyHelloPolicyToolCatalogPreview(
        MainWindow window,
        SessionPolicyDto? policy,
        bool clearExistingToolDefinitions) {
        try {
            ApplyHelloPolicyToolCatalogPreviewMethod.Invoke(window, new object?[] { policy, clearExistingToolDefinitions });
        } catch (TargetInvocationException ex) {
            throw ex.InnerException ?? ex;
        }
    }

    private static MainWindow CreateWindowForHelloPolicyPreview() {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        SetField(ToolCatalogPacksField, window, Array.Empty<ToolPackInfoDto>());
        SetField(ToolCatalogPluginsField, window, Array.Empty<PluginInfoDto>());
        SetField(ToolCatalogRoutingCatalogField, window, null!);
        SetField(ToolCatalogCapabilitySnapshotField, window, null!);
        SetField(ToolDescriptionsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolDisplayNamesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolCategoriesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolTagsField, window, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolPackIdsField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolPackNamesField, window, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolCatalogDefinitionsField, window, new Dictionary<string, ToolDefinitionDto>(StringComparer.OrdinalIgnoreCase));
        SetField(ToolParametersField, window, new Dictionary<string, ToolParameterDto[]>(StringComparer.OrdinalIgnoreCase));
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

    private static Dictionary<string, ToolDefinitionDto> GetToolCatalogDefinitions(MainWindow window) {
        return Assert.IsType<Dictionary<string, ToolDefinitionDto>>(ToolCatalogDefinitionsField.GetValue(window));
    }

    private static void SetField(FieldInfo field, object target, object? value) {
        field.SetValue(target, value);
    }
}

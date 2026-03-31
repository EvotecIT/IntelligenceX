using IntelligenceX.Chat.App;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using System;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests deferred startup metadata rerun scheduling decisions.
/// </summary>
public sealed class MainWindowStartupMetadataSyncRerunTests {
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
    /// Ensures persisted-preview hello policy produces a narrow preview plan that preserves metadata and requests stale-definition clearing.
    /// </summary>
    [Fact]
    public void BuildHelloPolicyToolCatalogPreviewPlan_PreservesMetadata_AndRequestsDefinitionClear() {
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

        var previewPlan = MainWindow.BuildHelloPolicyToolCatalogPreviewPlan(
            policy,
            clearExistingToolDefinitions: true);

        Assert.True(previewPlan.ClearExistingToolDefinitions);
        Assert.Equal("eventlog", Assert.Single(previewPlan.Packs).Id);
        Assert.Equal("ops_bundle", Assert.Single(previewPlan.Plugins).Id);
        Assert.Equal("persisted_preview", previewPlan.CapabilitySnapshot?.ToolingSnapshot?.Source);
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
}

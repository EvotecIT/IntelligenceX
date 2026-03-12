using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies startup bootstrap summary rendering helpers.
/// </summary>
public sealed class MainWindowStartupBootstrapSummaryTests {
    private static SessionPolicyDto CreatePolicy(
        SessionStartupBootstrapTelemetryDto? startupBootstrap = null,
        string[]? startupWarnings = null) {
        return new SessionPolicyDto {
            ReadOnly = true,
            DangerousToolsEnabled = false,
            MaxToolRounds = 8,
            ParallelTools = true,
            AllowMutatingParallelToolCalls = false,
            Packs = Array.Empty<ToolPackInfoDto>(),
            StartupWarnings = startupWarnings ?? Array.Empty<string>(),
            StartupBootstrap = startupBootstrap
        };
    }

    /// <summary>
    /// Includes phase timeline and slowest-phase details in startup summary output.
    /// </summary>
    [Fact]
    public void BuildStartupBootstrapSummaryLines_IncludesPhaseTimelineAndSlowestPhase() {
        var telemetry = new SessionStartupBootstrapTelemetryDto {
            TotalMs = 1000,
            RuntimePolicyMs = 50,
            BootstrapOptionsMs = 30,
            PackLoadMs = 800,
            PackRegisterMs = 90,
            RegistryFinalizeMs = 30,
            RegistryMs = 120,
            Tools = 142,
            PacksLoaded = 10,
            PacksDisabled = 1,
            Phases = new[] {
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseRuntimePolicyId, 50, 1),
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseBootstrapOptionsId, 30, 2),
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhasePackLoadId, 800, 3),
                new SessionStartupBootstrapPhaseTelemetryDto { Id = "registry_build", Label = "registry build", DurationMs = 120, Order = 4 }
            },
            SlowestPhaseId = StartupBootstrapContracts.PhasePackLoadId,
            SlowestPhaseLabel = StartupBootstrapContracts.PhasePackLoadLabel,
            SlowestPhaseMs = 800
        };

        var lines = MainWindow.BuildStartupBootstrapSummaryLines(telemetry);

        Assert.Contains("- Startup phases: runtime policy 50ms, bootstrap options 30ms, pack load 800ms, registry build 120ms", lines);
        Assert.Contains("- Slowest phase: pack load (800ms, 80%)", lines);
        Assert.Contains("- Total: 1.0s (pack load 800ms, pack register 90ms, registry finalize 30ms, registry total 120ms)", lines);
    }

    /// <summary>
    /// Suppresses startup summary notices for fast bootstrap runs without slow-load signals.
    /// </summary>
    [Fact]
    public void IsStartupBootstrapSignalWorthy_ReturnsFalseForFastBootstrapWithoutSlowSignals() {
        var telemetry = new SessionStartupBootstrapTelemetryDto {
            TotalMs = 420,
            PackLoadMs = 300,
            RegistryMs = 80
        };

        Assert.False(MainWindow.IsStartupBootstrapSignalWorthy(telemetry));
    }

    /// <summary>
    /// Produces concise header-status detail with total bootstrap and slowest phase timing.
    /// </summary>
    [Fact]
    public void BuildStartupBootstrapStatusDetail_IncludesTotalAndSlowestPhase() {
        var telemetry = new SessionStartupBootstrapTelemetryDto {
            TotalMs = 1800,
            SlowestPhaseLabel = StartupBootstrapContracts.PhasePackRegisterLabel,
            SlowestPhaseMs = 1200
        };

        var detail = MainWindow.BuildStartupBootstrapStatusDetail(telemetry);

        Assert.Equal("tool bootstrap 1.8s (slowest: pack register 1.2s)", detail);
    }

    /// <summary>
    /// Returns empty detail when startup bootstrap telemetry is not available.
    /// </summary>
    [Fact]
    public void BuildStartupBootstrapStatusDetail_ReturnsEmptyWhenTelemetryMissing() {
        var detail = MainWindow.BuildStartupBootstrapStatusDetail(null);

        Assert.Equal(string.Empty, detail);
    }

    /// <summary>
    /// Classifies startup bootstrap cache mode as hit when telemetry phase includes cache-hit.
    /// </summary>
    [Fact]
    public void ResolveStartupBootstrapCacheModeTokenFromPolicy_ReturnsHitForCacheHitPhase() {
        var policy = CreatePolicy(new SessionStartupBootstrapTelemetryDto {
            TotalMs = 42,
            Phases = new[] {
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseCacheHitId, 42, 1)
            }
        });

        var mode = MainWindow.ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);

        Assert.Equal(StartupBootstrapContracts.CacheModeHit, mode);
    }

    /// <summary>
    /// Classifies startup bootstrap cache mode as persisted-preview when warnings report preview restore.
    /// </summary>
    [Fact]
    public void ResolveStartupBootstrapCacheModeTokenFromPolicy_ReturnsPersistedPreviewForPreviewWarning() {
        var policy = CreatePolicy(
            startupBootstrap: null,
            startupWarnings: new[] { StartupBootstrapWarningBuilder.BuildPersistedPreviewRestoredSummary() });

        var mode = MainWindow.ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);

        Assert.Equal(StartupBootstrapContracts.CacheModePersistedPreview, mode);
    }

    /// <summary>
    /// Classifies startup bootstrap cache mode as miss when bootstrap telemetry exists without cache-hit markers.
    /// </summary>
    [Fact]
    public void ResolveStartupBootstrapCacheModeTokenFromPolicy_ReturnsMissWhenBootstrapWithoutCacheHit() {
        var policy = CreatePolicy(new SessionStartupBootstrapTelemetryDto {
            TotalMs = 880,
            Phases = new[] {
                StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhasePackLoadId, 700, 1)
            }
        });

        var mode = MainWindow.ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);

        Assert.Equal(StartupBootstrapContracts.CacheModeMiss, mode);
    }

    /// <summary>
    /// Suppresses full transcript bootstrap summaries while the app is still showing a persisted preview catalog.
    /// </summary>
    [Fact]
    public void ShouldAppendStartupBootstrapSummary_ReturnsFalseForPersistedPreviewPolicy() {
        var policy = CreatePolicy(
            startupBootstrap: new SessionStartupBootstrapTelemetryDto {
                TotalMs = 13100,
                PackLoadMs = 13000,
                Tools = 187,
                PacksLoaded = 10
            },
            startupWarnings: new[] {
                StartupBootstrapWarningBuilder.BuildPersistedPreviewRestoredSummary()
            });

        var shouldAppend = MainWindow.ShouldAppendStartupBootstrapSummary(policy);

        Assert.False(shouldAppend);
    }

    /// <summary>
    /// Keeps real bootstrap summaries for non-preview startup states so final rebuilt catalogs still surface timing details.
    /// </summary>
    [Fact]
    public void ShouldAppendStartupBootstrapSummary_ReturnsTrueForSignalWorthyFinalPolicy() {
        var policy = CreatePolicy(new SessionStartupBootstrapTelemetryDto {
            TotalMs = 18100,
            PackLoadMs = 17700,
            Tools = 235,
            PacksLoaded = 11
        });

        var shouldAppend = MainWindow.ShouldAppendStartupBootstrapSummary(policy);

        Assert.True(shouldAppend);
    }
}

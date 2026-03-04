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
                new SessionStartupBootstrapPhaseTelemetryDto { Id = "runtime_policy", Label = "runtime policy", DurationMs = 50, Order = 1 },
                new SessionStartupBootstrapPhaseTelemetryDto { Id = "bootstrap_options", Label = "bootstrap options", DurationMs = 30, Order = 2 },
                new SessionStartupBootstrapPhaseTelemetryDto { Id = "pack_load", Label = "pack load", DurationMs = 800, Order = 3 },
                new SessionStartupBootstrapPhaseTelemetryDto { Id = "registry_build", Label = "registry build", DurationMs = 120, Order = 4 }
            },
            SlowestPhaseId = "pack_load",
            SlowestPhaseLabel = "pack load",
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
            SlowestPhaseLabel = "pack register",
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
                new SessionStartupBootstrapPhaseTelemetryDto {
                    Id = "cache_hit",
                    Label = "cache hit",
                    DurationMs = 42,
                    Order = 1
                }
            }
        });

        var mode = MainWindow.ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);

        Assert.Equal("hit", mode);
    }

    /// <summary>
    /// Classifies startup bootstrap cache mode as persisted-preview when warnings report preview restore.
    /// </summary>
    [Fact]
    public void ResolveStartupBootstrapCacheModeTokenFromPolicy_ReturnsPersistedPreviewForPreviewWarning() {
        var policy = CreatePolicy(
            startupBootstrap: null,
            startupWarnings: new[] { "[startup] tooling bootstrap preview restored from persisted cache while runtime rebuild continues." });

        var mode = MainWindow.ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);

        Assert.Equal("persisted_preview", mode);
    }

    /// <summary>
    /// Classifies startup bootstrap cache mode as miss when bootstrap telemetry exists without cache-hit markers.
    /// </summary>
    [Fact]
    public void ResolveStartupBootstrapCacheModeTokenFromPolicy_ReturnsMissWhenBootstrapWithoutCacheHit() {
        var policy = CreatePolicy(new SessionStartupBootstrapTelemetryDto {
            TotalMs = 880,
            Phases = new[] {
                new SessionStartupBootstrapPhaseTelemetryDto {
                    Id = "pack_load",
                    Label = "pack load",
                    DurationMs = 700,
                    Order = 1
                }
            }
        });

        var mode = MainWindow.ResolveStartupBootstrapCacheModeTokenFromPolicy(policy);

        Assert.Equal("miss", mode);
    }
}

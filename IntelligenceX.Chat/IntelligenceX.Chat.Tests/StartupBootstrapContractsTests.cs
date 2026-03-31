using System;
using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupBootstrapContractsTests {
    [Theory]
    [InlineData(StartupBootstrapContracts.PhaseRuntimePolicyId, StartupBootstrapContracts.PhaseRuntimePolicyLabel)]
    [InlineData(StartupBootstrapContracts.PhaseBootstrapOptionsId, StartupBootstrapContracts.PhaseBootstrapOptionsLabel)]
    [InlineData(StartupBootstrapContracts.PhaseDescriptorDiscoveryId, StartupBootstrapContracts.PhaseDescriptorDiscoveryLabel)]
    [InlineData(StartupBootstrapContracts.PhasePackActivationId, StartupBootstrapContracts.PhasePackActivationLabel)]
    [InlineData(StartupBootstrapContracts.PhaseRegistryActivationFinalizeId, StartupBootstrapContracts.PhaseRegistryActivationFinalizeLabel)]
    [InlineData(StartupBootstrapContracts.PhasePackLoadId, StartupBootstrapContracts.PhasePackLoadLabel)]
    [InlineData(StartupBootstrapContracts.PhasePackRegisterId, StartupBootstrapContracts.PhasePackRegisterLabel)]
    [InlineData(StartupBootstrapContracts.PhaseRegistryFinalizeId, StartupBootstrapContracts.PhaseRegistryFinalizeLabel)]
    [InlineData(StartupBootstrapContracts.PhaseDescriptorCacheHitId, StartupBootstrapContracts.PhaseDescriptorCacheHitLabel)]
    [InlineData(StartupBootstrapContracts.PhaseCacheHitId, StartupBootstrapContracts.PhaseCacheHitLabel)]
    public void ResolvePhaseLabel_ReturnsCanonicalLabelForKnownPhaseIds(string phaseId, string expectedLabel) {
        var label = StartupBootstrapContracts.ResolvePhaseLabel(phaseId);

        Assert.Equal(expectedLabel, label);
    }

    [Fact]
    public void ResolveCacheModeToken_ReturnsPersistedPreviewForCanonicalPreviewWarning() {
        var mode = StartupBootstrapContracts.ResolveCacheModeToken(
            bootstrap: null,
            startupWarnings: new[] { StartupBootstrapWarningBuilder.BuildPersistedPreviewRestoredSummary() });

        Assert.Equal(StartupBootstrapContracts.CacheModePersistedPreview, mode);
    }

    [Fact]
    public void ResolveCacheModeToken_ReturnsPersistedPreviewForDescriptorPreviewPhase() {
        var mode = StartupBootstrapContracts.ResolveCacheModeToken(
            bootstrap: new SessionStartupBootstrapTelemetryDto {
                TotalMs = 1,
                Phases = new[] {
                    StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorCacheHitId, 1, 1)
                }
            },
            startupWarnings: Array.Empty<string>());

        Assert.Equal(StartupBootstrapContracts.CacheModePersistedPreview, mode);
    }

    [Fact]
    public void IsCacheHitPhaseId_MatchesCanonicalPhaseId() {
        Assert.True(StartupBootstrapContracts.IsCacheHitPhaseId(StartupBootstrapContracts.PhaseCacheHitId));
        Assert.False(StartupBootstrapContracts.IsCacheHitPhaseId(StartupBootstrapContracts.PhasePackLoadId));
    }

    [Fact]
    public void IsPersistedPreviewPhaseId_MatchesCanonicalPhaseId() {
        Assert.True(StartupBootstrapContracts.IsPersistedPreviewPhaseId(StartupBootstrapContracts.PhaseDescriptorCacheHitId));
        Assert.False(StartupBootstrapContracts.IsPersistedPreviewPhaseId(StartupBootstrapContracts.PhaseCacheHitId));
    }

    [Fact]
    public void ResolvePhaseDuration_PrefersExplicitPhaseTelemetryOverLegacyFields() {
        var duration = StartupBootstrapContracts.ResolvePhaseDuration(
            new SessionStartupBootstrapTelemetryDto {
                PackLoadMs = 900,
                Phases = new[] {
                    StartupBootstrapContracts.CreatePhase(StartupBootstrapContracts.PhaseDescriptorDiscoveryId, 420, 1)
                }
            },
            StartupBootstrapContracts.PhaseDescriptorDiscoveryId);

        Assert.Equal(420, duration);
    }

    [Fact]
    public void ResolvePhaseDuration_FallsBackToLegacyFieldsWhenPhaseTelemetryMissing() {
        var duration = StartupBootstrapContracts.ResolvePhaseDuration(
            new SessionStartupBootstrapTelemetryDto {
                PackRegisterMs = 87
            },
            StartupBootstrapContracts.PhasePackActivationId);

        Assert.Equal(87, duration);
    }

    [Fact]
    public void WithCanonicalPhaseDurations_PopulatesCanonicalFieldsFromLegacyFallbacks() {
        var normalized = StartupBootstrapContracts.WithCanonicalPhaseDurations(
            new SessionStartupBootstrapTelemetryDto {
                PackLoadMs = 41,
                PackRegisterMs = 52,
                RegistryFinalizeMs = 63
            });

        Assert.Equal(41, normalized.DescriptorDiscoveryMs);
        Assert.Equal(52, normalized.PackActivationMs);
        Assert.Equal(63, normalized.RegistryActivationFinalizeMs);
    }
}

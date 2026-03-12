using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupBootstrapContractsTests {
    [Theory]
    [InlineData(StartupBootstrapContracts.PhaseRuntimePolicyId, StartupBootstrapContracts.PhaseRuntimePolicyLabel)]
    [InlineData(StartupBootstrapContracts.PhaseBootstrapOptionsId, StartupBootstrapContracts.PhaseBootstrapOptionsLabel)]
    [InlineData(StartupBootstrapContracts.PhasePackLoadId, StartupBootstrapContracts.PhasePackLoadLabel)]
    [InlineData(StartupBootstrapContracts.PhasePackRegisterId, StartupBootstrapContracts.PhasePackRegisterLabel)]
    [InlineData(StartupBootstrapContracts.PhaseRegistryFinalizeId, StartupBootstrapContracts.PhaseRegistryFinalizeLabel)]
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
    public void IsCacheHitPhaseId_MatchesCanonicalPhaseId() {
        Assert.True(StartupBootstrapContracts.IsCacheHitPhaseId(StartupBootstrapContracts.PhaseCacheHitId));
        Assert.False(StartupBootstrapContracts.IsCacheHitPhaseId(StartupBootstrapContracts.PhasePackLoadId));
    }
}

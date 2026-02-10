using System;
using IntelligenceX.Cli.Setup.Web;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class WebSetupAnalysisValidatorTests {
    [Fact]
    public void NotSetup_WithAnalysisFields_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: false,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisPacks: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedPacks: out _,
            error: out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void WithoutConfig_WithAnalysisFields_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: false,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisPacks: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedPacks: out _,
            error: out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void WithConfigOverride_WithAnalysisFields_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: true,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisPacks: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedPacks: out _,
            error: out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void AnalysisEnabledNull_GateEnabled_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: null,
            analysisGateEnabled: true,
            analysisPacks: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedPacks: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("require", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisEnabledFalse_PacksProvided_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: false,
            analysisGateEnabled: null,
            analysisPacks: "all-50",
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedPacks: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("require", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisEnabledTrue_InvalidPacks_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisPacks: "--force",
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedPacks: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("Invalid pack id", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisEnabledTrue_NormalizesPacks() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: true,
            analysisPacks: " all-50,all-50 , powershell-50 ",
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGate,
            normalizedPacks: out var normalizedPacks,
            error: out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.True(normalizedEnabled);
        Assert.True(normalizedGate);
        Assert.Equal("all-50,powershell-50", normalizedPacks);
    }

    [Fact]
    public void AnalysisApplicable_NoFields_IsOk() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: null,
            analysisGateEnabled: null,
            analysisPacks: null,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGate,
            normalizedPacks: out var normalizedPacks,
            error: out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(normalizedEnabled);
        Assert.Null(normalizedGate);
        Assert.Null(normalizedPacks);
    }
}


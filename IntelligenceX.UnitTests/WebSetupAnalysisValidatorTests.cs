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
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
            error: out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void UpdateSecret_WithAnalysisFields_Fails() {
        // update-secret requests are non-setup mode in WebApi and must reject analysis options.
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: false,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
            error: out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Cleanup_WithAnalysisFields_Fails() {
        // cleanup requests are non-setup mode in WebApi and must reject analysis options.
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: false,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: null,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: "all-50",
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
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
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
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
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
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
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
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
            analysisRunStrict: null,
            analysisPacks: "all-50",
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("require", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisEnabledFalse_GateEnabled_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: false,
            analysisGateEnabled: true,
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("require", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisEnabledNull_PacksProvided_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: null,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: "all-50",
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
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
            analysisRunStrict: null,
            analysisPacks: "--force",
            analysisExportPath: null,
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
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
            analysisRunStrict: null,
            analysisPacks: " all-50,all-50 , powershell-50 ",
            analysisExportPath: null,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGate,
            normalizedRunStrict: out _,
            normalizedPacks: out var normalizedPacks,
            normalizedExportPath: out _,
            error: out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.True(normalizedEnabled);
        Assert.True(normalizedGate);
        Assert.Equal("all-50,powershell-50", normalizedPacks);
    }

    [Fact]
    public void AnalysisEnabledTrue_NormalizesExportPath() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: " .intelligencex\\analyzers ",
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out var normalizedExportPath,
            error: out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(".intelligencex/analyzers", normalizedExportPath);
    }

    [Fact]
    public void AnalysisEnabledFalse_ExportPath_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: false,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: ".intelligencex/analyzers",
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("require", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisEnabledTrue_InvalidExportPath_Fails() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: true,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: "../outside",
            normalizedEnabled: out _,
            normalizedGateEnabled: out _,
            normalizedRunStrict: out _,
            normalizedPacks: out _,
            normalizedExportPath: out _,
            error: out var error);

        Assert.False(ok);
        Assert.Contains("analysisExportPath", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalysisApplicable_NoFields_IsOk() {
        var ok = WebSetupAnalysisValidator.TryValidateAndNormalize(
            isSetup: true,
            withConfig: true,
            hasConfigOverride: false,
            analysisEnabled: null,
            analysisGateEnabled: null,
            analysisRunStrict: null,
            analysisPacks: null,
            analysisExportPath: null,
            normalizedEnabled: out var normalizedEnabled,
            normalizedGateEnabled: out var normalizedGate,
            normalizedRunStrict: out _,
            normalizedPacks: out var normalizedPacks,
            normalizedExportPath: out _,
            error: out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(normalizedEnabled);
        Assert.Null(normalizedGate);
        Assert.Null(normalizedPacks);
    }
}

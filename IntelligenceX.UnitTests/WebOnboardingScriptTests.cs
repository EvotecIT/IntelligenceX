using System;
using System.IO;
using System.Reflection;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class WebOnboardingScriptTests {
    [Fact]
    public void OnboardingPathSwitchesResetWithConfigDeterministically() {
        var script = LoadWizardScript();

        Assert.Contains("case 'new-setup':", script, StringComparison.Ordinal);
        Assert.Contains("if (withConfig) withConfig.checked = true;", script, StringComparison.Ordinal);

        var refreshBlock = ExtractCaseBlock(script, "refresh-auth");
        Assert.Contains("if (withConfig) withConfig.checked = false;", refreshBlock, StringComparison.Ordinal);
        Assert.Contains("selectProvider('openai');", refreshBlock, StringComparison.Ordinal);

        var cleanupBlock = ExtractCaseBlock(script, "cleanup");
        Assert.Contains("if (withConfig) withConfig.checked = false;", cleanupBlock, StringComparison.Ordinal);
        Assert.Contains("selectProvider('openai');", cleanupBlock, StringComparison.Ordinal);
    }

    private static string LoadWizardScript() {
        var assembly = Assembly.Load("IntelligenceX.Cli");
        using var stream = assembly.GetManifestResourceStream("Setup.Web.wizard.js");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private static string ExtractCaseBlock(string script, string caseName) {
        var start = script.IndexOf($"case '{caseName}':", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing case block: {caseName}");

        var end = script.IndexOf("break;", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing break for case block: {caseName}");

        return script.Substring(start, end - start);
    }
}

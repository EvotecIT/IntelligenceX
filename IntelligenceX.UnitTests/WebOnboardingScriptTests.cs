using System;
using System.IO;
using System.Reflection;
using System.Linq;
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
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => string.Equals(name, "Setup.Web.wizard.js", StringComparison.Ordinal))
            ?? assembly
                .GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("wizard.js", StringComparison.OrdinalIgnoreCase));

        Assert.False(string.IsNullOrWhiteSpace(resourceName), "wizard.js embedded resource not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName!);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    private static string ExtractCaseBlock(string script, string caseName) {
        var start = script.IndexOf($"case '{caseName}':", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing case block: {caseName}");

        var nextCase = script.IndexOf("case '", start + 1, StringComparison.Ordinal);
        var nextDefault = script.IndexOf("default:", start + 1, StringComparison.Ordinal);
        var switchEnd = script.IndexOf('}', start + 1);

        var end = int.MaxValue;
        if (nextCase > start) {
            end = Math.Min(end, nextCase);
        }
        if (nextDefault > start) {
            end = Math.Min(end, nextDefault);
        }
        if (switchEnd > start) {
            end = Math.Min(end, switchEnd);
        }

        Assert.True(end > start && end != int.MaxValue, $"Could not determine case block end: {caseName}");

        return script.Substring(start, end - start);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class WebOnboardingScriptTests {
    [Fact]
    public void OnboardingPathSwitchesResetWithConfigDeterministically() {
        var script = LoadWizardScript();
        var normalized = NormalizeWhitespace(script);

        Assert.Contains("case 'refresh-auth':", normalized, StringComparison.Ordinal);
        Assert.Contains("case 'cleanup':", normalized, StringComparison.Ordinal);
        Assert.Contains("case 'new-setup':", normalized, StringComparison.Ordinal);
        Assert.Contains("default:", normalized, StringComparison.Ordinal);

        Assert.Contains("selectOperation('update-secret');", normalized, StringComparison.Ordinal);
        Assert.Contains("selectOperation('cleanup');", normalized, StringComparison.Ordinal);
        Assert.Contains("selectOperation('setup');", normalized, StringComparison.Ordinal);

        Assert.Contains("selectSecretOption('skip');", normalized, StringComparison.Ordinal);
        Assert.True(CountOccurrences(normalized, "selectSecretOption('login');") >= 2,
            "Expected login secret option to be set for both setup and refresh-auth paths.");

        Assert.True(CountOccurrences(normalized, "if (withConfig) withConfig.checked = false;") >= 2,
            "Expected withConfig reset for non-setup paths.");
        Assert.Contains("if (withConfig) withConfig.checked = true;", normalized, StringComparison.Ordinal);
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

    private static string NormalizeWhitespace(string input) {
        var chars = input.Select(ch => char.IsWhiteSpace(ch) ? ' ' : ch).ToArray();
        return new string(chars);
    }

    private static int CountOccurrences(string input, string value) {
        if (string.IsNullOrEmpty(value)) {
            return 0;
        }

        var count = 0;
        var index = 0;
        while (true) {
            index = input.IndexOf(value, index, StringComparison.Ordinal);
            if (index < 0) {
                return count;
            }
            count++;
            index += value.Length;
        }
    }
}

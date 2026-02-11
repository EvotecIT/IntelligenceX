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
        Assert.Contains(
            "case 'new-setup':\n" +
            "    default:\n" +
            "      selectOperation('setup');\n" +
            "      selectProvider('openai');\n" +
            "      selectSecretOption('login');\n" +
            "      if (withConfig) withConfig.checked = true;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "case 'refresh-auth':\n" +
            "      selectOperation('update-secret');\n" +
            "      selectProvider('openai');\n" +
            "      selectSecretOption('login');\n" +
            "      if (withConfig) withConfig.checked = false;",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "case 'cleanup':\n" +
            "      selectOperation('cleanup');\n" +
            "      selectProvider('openai');\n" +
            "      selectSecretOption('skip');\n" +
            "      if (withConfig) withConfig.checked = false;",
            script,
            StringComparison.Ordinal);
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
}

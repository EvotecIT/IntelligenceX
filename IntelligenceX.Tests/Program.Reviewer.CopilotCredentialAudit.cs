#if INTELLIGENCEX_REVIEWER
namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestCopilotConfigReplacesThePreviousCredentialSource() {
        string? previous = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        string path = Path.Combine(Path.GetTempPath(), "ix-review-config-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", path);
            foreach (bool environment in new[] { true, false }) {
                File.WriteAllText(path, environment ? "{\"copilot\":{\"tokenEnv\":\"NEW_TOKEN\"}}" : "{\"copilot\":{\"token\":\"new-token\"}}");
                var settings = new ReviewSettings { CopilotToken = environment ? "old-token" : null,
                    CopilotTokenEnvironmentVariable = environment ? null : "OLD_TOKEN" };
                ReviewConfigLoader.Apply(settings);
                AssertEqual(environment ? null : "new-token", settings.CopilotToken, "config token override");
                AssertEqual(environment ? "NEW_TOKEN" : null, settings.CopilotTokenEnvironmentVariable, "config variable override");
            }
            File.WriteAllText(path, "{\"copilot\":{\"token\":\"one\",\"tokenEnv\":\"TWO\"}}");
            AssertThrows<InvalidOperationException>(() => ReviewConfigLoader.Apply(new()), "ambiguous config must fail");
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previous);
            File.Delete(path);
        }
    }

    private static void TestCopilotCredentialAuditTracksSelectedSourcesWithoutValues() {
        string[] variables = { "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN", "IX_CONFIG_TOKEN" };
        var prior = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try {
            foreach (string source in new[] { "literal", "IX_CONFIG_TOKEN", "COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN" }) {
                foreach (string variable in variables) Environment.SetEnvironmentVariable(variable, "secret-" + variable);
                if (source == "GH_TOKEN" || source == "GITHUB_TOKEN") Environment.SetEnvironmentVariable("COPILOT_GITHUB_TOKEN", null);
                if (source == "GITHUB_TOKEN") Environment.SetEnvironmentVariable("GH_TOKEN", null);
                var settings = new ReviewSettings { Provider = ReviewProvider.Copilot, CopilotModel = "model",
                    CopilotToken = source == "literal" ? "secret-literal" : null,
                    CopilotTokenEnvironmentVariable = source == "IX_CONFIG_TOKEN" ? source : null };
                using var audit = SecretsAudit.TryStart(settings)!;
                var native = new ReviewRunner(settings).BuildCopilotClientOptionsForTests().CopilotOptions;
                using var authentication = new IntelligenceX.Copilot.Native.CopilotNativeAuthentication(native);
                AssertEqual("secret-" + source, authentication.GetAccessTokenAsync().GetAwaiter().GetResult(), "selected credential");
                string expected = source == "literal" ? "Copilot credential from config (copilot.token)" : "Copilot credential from " + source;
                AssertEqual(true, audit.Entries.Contains(expected), "selected credential source recorded");
                AssertEqual(false, audit.Entries.Any(entry => entry.Contains("secret-", StringComparison.Ordinal)), "credential values never recorded");
            }
        } finally {
            foreach (string variable in variables) Environment.SetEnvironmentVariable(variable, prior[variable]);
        }
    }
}
#endif

using IntelligenceX.Cli.Setup;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class CopilotSetupModelTests {
    [Fact]
    public async Task NoninteractiveCopilotSetupRejectsOmittedModelBeforeAuthentication() {
        Assert.Equal(1, await SetupRunner.RunAsync(new[] { "--provider", "copilot" }));
        Assert.Throws<ArgumentException>(() => SetupRunner.BuildReviewerConfigJson(new[] { "--provider", "copilot" }));
        Assert.Throws<ArgumentException>(() => SetupRunner.BuildWorkflowYamlFromSeedForTests(new[] { "--provider", "copilot" }, ""));
        Assert.Empty(SetupProviderCatalog.GetDefaultModel("copilot"));
    }

    [Fact]
    public void CopilotSetupRetainsExplicitModelInBothGeneratedArtifacts() {
        var args = new[] { "--provider", "copilot", "--model", "account-catalog-model" };
        Assert.Contains("account-catalog-model", SetupRunner.BuildReviewerConfigJson(args));
        Assert.Contains("account-catalog-model", SetupRunner.BuildWorkflowYamlFromSeedForTests(args, ""));
        Assert.False(string.IsNullOrWhiteSpace(SetupProviderCatalog.GetDefaultModel("openai")));
    }
}

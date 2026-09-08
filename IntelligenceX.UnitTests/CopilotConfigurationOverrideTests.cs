using IntelligenceX.Configuration;
using IntelligenceX.Copilot.Native;
using IntelligenceX.OpenAI;
using Xunit;

namespace IntelligenceX.UnitTests;

[Collection("Copilot environment credentials")]
public sealed class CopilotConfigurationOverrideTests {
    [Theory]
    [InlineData("direct")]
    [InlineData("client")]
    [InlineData("session")]
    public async Task ConfiguredEnvironmentReplacesExistingTokenAndRemainsRotatable(string entryPoint) {
        const string variable = "IX_TEST_CONFIGURED_COPILOT_TOKEN";
        string? previous = Environment.GetEnvironmentVariable(variable);
        string path = Path.Combine(Path.GetTempPath(), "ix-copilot-config-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            Environment.SetEnvironmentVariable(variable, "replacement");
            await File.WriteAllTextAsync(path, "{\"copilot\":{\"tokenEnvironmentVariable\":\"" + variable + "\"}}");
            var native = new CopilotNativeOptions { GitHubToken = "original" };
            if (entryPoint == "direct") new CopilotConfig { TokenEnvironmentVariable = variable }.ApplyTo(native);
            else if (entryPoint == "client") Assert.True(new IntelligenceXClientOptions { CopilotOptions = native }.TryApplyConfig(path));
            else Assert.True(new EasySessionOptions { CopilotOptions = native }.TryApplyConfig(path));
            native.Validate();
            Assert.Null(native.GitHubToken);
            Assert.False(native.UseEnvironmentCredentials);
            Assert.Equal("replacement", await native.TokenProvider!(default));
            Environment.SetEnvironmentVariable(variable, "rotated");
            Assert.Equal("rotated", await native.TokenProvider!(default));
        } finally {
            Environment.SetEnvironmentVariable(variable, previous);
            File.Delete(path);
        }
    }
}

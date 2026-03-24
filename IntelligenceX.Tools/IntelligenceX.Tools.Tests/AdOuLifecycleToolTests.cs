using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdOuLifecycleToolTests {
    [Fact]
    public async Task InvokeAsync_MoveDryRun_ShouldUseDnAwareLeafParsingForEscapedCommaIdentity() {
        var tool = new AdOuLifecycleTool(new ActiveDirectoryToolOptions());
        var arguments = new JsonObject()
            .Add("operation", "move")
            .Add("identity", "OU=Ops\\,Tier 0,OU=Infrastructure,DC=lab,DC=local")
            .Add("target_parent_distinguished_name", "OU=Privileged,DC=lab,DC=local")
            .Add("apply", false);

        var json = await tool.InvokeAsync(arguments, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("OU=Ops\\,Tier 0,OU=Privileged,DC=lab,DC=local", root.GetProperty("distinguished_name").GetString());
    }
}

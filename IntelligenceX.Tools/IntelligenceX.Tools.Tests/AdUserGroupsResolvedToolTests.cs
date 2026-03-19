using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdUserGroupsResolvedToolTests {
    [Fact]
    public async Task InvokeAsync_MissingIdentity_ShouldFailValidationWithoutQueryingDirectory() {
        var tool = new AdUserGroupsResolvedTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(arguments: null, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("identity", root.GetProperty("error").GetString());
    }
}

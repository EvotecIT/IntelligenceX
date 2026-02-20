using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class AdNeverLoggedInAccountsToolTests {
    [Fact]
    public async Task InvokeAsync_WhenDomainMissing_ReturnsInvalidArgument() {
        var tool = new AdNeverLoggedInAccountsTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject(),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("domain_name", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenReferenceTimeInvalid_ReturnsInvalidArgument() {
        var tool = new AdNeverLoggedInAccountsTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("domain_name", "contoso.com")
                .Add("reference_time_utc", "not-a-date"),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("reference_time_utc", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}

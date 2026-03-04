using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class AdSpnStatsToolTests {
    private static readonly MethodInfo ResolvePositiveCappedOrDefaultMethod =
        typeof(AdSpnStatsTool).GetMethod("ResolvePositiveCappedOrDefault", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolvePositiveCappedOrDefault not found.");

    [Fact]
    public async Task InvokeAsync_WhenSpnContainsAndSpnExactProvided_ReturnsInvalidArgument() {
        var tool = new AdSpnStatsTool(new ActiveDirectoryToolOptions());

        var response = await tool.InvokeAsync(
                arguments: new JsonObject()
                    .Add("spn_contains", "HTTP")
                    .Add("spn_exact", "HTTP/server01.ad.evotec.xyz"),
                cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("mutually exclusive", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, 50, 200, 50)]
    [InlineData(0L, 50, 200, 50)]
    [InlineData(-7L, 50, 200, 50)]
    [InlineData(25L, 50, 200, 25)]
    [InlineData(999L, 50, 200, 200)]
    public void ResolvePositiveCappedOrDefault_PreservesDefaultAndCapsPositiveValues(long? requestedValue, int defaultValue, int maxInclusive,
        int expected) {
        var result = Assert.IsType<int>(ResolvePositiveCappedOrDefaultMethod.Invoke(null, new object?[] { requestedValue, defaultValue, maxInclusive }));
        Assert.Equal(expected, result);
    }
}

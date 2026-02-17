using System;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards routing metadata payload parsing so UI activity text remains stable across numeric JSON variants.
/// </summary>
public sealed class MainWindowRoutingMetaPayloadTests {
    private static readonly MethodInfo TryParseRoutingMetaPayloadMethod = typeof(MainWindow).GetMethod(
                                                            "TryParseRoutingMetaPayload",
                                                            BindingFlags.NonPublic | BindingFlags.Static)
                                                        ?? throw new InvalidOperationException("TryParseRoutingMetaPayload not found.");

    /// <summary>
    /// Ensures routing metadata parsing accepts numeric values represented as numbers and strings.
    /// </summary>
    [Theory]
    [InlineData("""{"strategy":"semantic_planner","selectedToolCount":8,"totalToolCount":21}""", 8, 21)]
    [InlineData("""{"strategy":"semantic_planner","selectedToolCount":"8","totalToolCount":"21"}""", 8, 21)]
    [InlineData("""{"strategy":"semantic_planner","selectedToolCount":"8.7","totalToolCount":"21.4"}""", 8, 21)]
    public void TryParseRoutingMetaPayload_AcceptsNumericVariants(string payload, int expectedSelected, int expectedTotal) {
        var parsed = Invoke(payload, out var strategy, out var selectedToolCount, out var totalToolCount);

        Assert.True(parsed);
        Assert.Equal("semantic planner", strategy);
        Assert.Equal(expectedSelected, selectedToolCount);
        Assert.Equal(expectedTotal, totalToolCount);
    }

    /// <summary>
    /// Ensures routing metadata count values are clamped safely for overflow and negative inputs.
    /// </summary>
    [Fact]
    public void TryParseRoutingMetaPayload_ClampsLargeAndNegativeCounts() {
        var payload = """{"strategy":"weighted_heuristic","selectedToolCount":"4294967296","totalToolCount":-4}""";

        var parsed = Invoke(payload, out var strategy, out var selectedToolCount, out var totalToolCount);

        Assert.True(parsed);
        Assert.Equal("weighted heuristic", strategy);
        Assert.Equal(0, selectedToolCount);
        Assert.Equal(0, totalToolCount);
    }

    /// <summary>
    /// Ensures selected count is normalized when payload counts arrive in an inconsistent state.
    /// </summary>
    [Fact]
    public void TryParseRoutingMetaPayload_ClampsSelectedCountToTotalWhenNeeded() {
        var payload = """{"strategy":"semantic_planner","selectedToolCount":"17","totalToolCount":"9"}""";

        var parsed = Invoke(payload, out _, out var selectedToolCount, out var totalToolCount);

        Assert.True(parsed);
        Assert.Equal(9, selectedToolCount);
        Assert.Equal(9, totalToolCount);
    }

    /// <summary>
    /// Ensures malformed count fields fail parsing so callers can show fallback activity text.
    /// </summary>
    [Fact]
    public void TryParseRoutingMetaPayload_ReturnsFalseWhenCountsAreInvalid() {
        var payload = """{"strategy":"semantic_planner","selectedToolCount":"eight","totalToolCount":"21"}""";

        var parsed = Invoke(payload, out _, out _, out _);

        Assert.False(parsed);
    }

    private static bool Invoke(string payload, out string strategy, out int selectedToolCount, out int totalToolCount) {
        var args = new object?[] { payload, null, 0, 0 };
        var result = TryParseRoutingMetaPayloadMethod.Invoke(null, args);

        strategy = Assert.IsType<string>(args[1]);
        selectedToolCount = Assert.IsType<int>(args[2]);
        totalToolCount = Assert.IsType<int>(args[3]);
        return Assert.IsType<bool>(result);
    }
}

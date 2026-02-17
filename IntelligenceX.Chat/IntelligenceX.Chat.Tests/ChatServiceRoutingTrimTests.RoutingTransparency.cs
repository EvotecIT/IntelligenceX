using System;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData(6, 12, true)]
    [InlineData(0, 12, false)]
    [InlineData(6, 0, false)]
    public void ShouldEmitRoutingTransparency_RequiresValidCounts(
        int selectedToolCount,
        int totalToolCount,
        bool expected) {
        var result = ShouldEmitRoutingTransparencyMethod.Invoke(
            null,
            new object?[] { selectedToolCount, totalToolCount });

        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Theory]
    [InlineData("semantic_planner", "semantic planning")]
    [InlineData("weighted_heuristic", "weighted relevance")]
    [InlineData("continuation_subset", "continuation context")]
    [InlineData("execution_contract_full_set", "explicit execution turn")]
    [InlineData("disabled", "disabled for this turn")]
    public void BuildRoutingSelectionMessage_UsesStrategySpecificText(string strategy, string expectedPhrase) {
        var result = BuildRoutingSelectionMessageMethod.Invoke(null, new object?[] { 7, 24, strategy });
        var text = Assert.IsType<string>(result);

        Assert.Contains("7", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("24", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedPhrase, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRoutingMetaPayload_IncludesStructuredRoutingContext() {
        var result = BuildRoutingMetaPayloadMethod.Invoke(null, new object?[] {
            "semantic_planner",
            true,
            false,
            false,
            8,
            21,
            5,
            true
        });
        var payload = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal("semantic_planner", root.GetProperty("strategy").GetString());
        Assert.True(root.GetProperty("weightedToolRouting").GetBoolean());
        Assert.Equal(8, root.GetProperty("selectedToolCount").GetInt32());
        Assert.Equal(21, root.GetProperty("totalToolCount").GetInt32());
        Assert.Equal(5, root.GetProperty("insightCount").GetInt32());
        Assert.True(root.GetProperty("plannerInsightsDetected").GetBoolean());
    }
}

using System;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData(6, 12, true)]
    [InlineData(0, 12, true)]
    [InlineData(6, 0, true)]
    [InlineData(13, 12, true)]
    [InlineData(0, 0, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, -1, false)]
    public void ShouldEmitRoutingTransparency_EmitsForNonNegativeDiagnosticStates(
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
    [InlineData("no_tools", "No tools are currently available")]
    public void BuildRoutingSelectionMessage_UsesStrategySpecificText(string strategy, string expectedPhrase) {
        var result = BuildRoutingSelectionMessageMethod.Invoke(null, new object?[] { 7, 24, strategy });
        var text = Assert.IsType<string>(result);

        if (!string.Equals(strategy, "no_tools", StringComparison.OrdinalIgnoreCase)) {
            Assert.Contains("7", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("24", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(expectedPhrase, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRoutingSelectionMessage_NormalizesInconsistentCounts() {
        var result = BuildRoutingSelectionMessageMethod.Invoke(null, new object?[] { 9, 4, "weighted_heuristic" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("4", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9 of 4", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRoutingStrategy_ReturnsNoToolsForDegenerateCountStates() {
        var insightsParameterType = ResolveRoutingStrategyMethod.GetParameters()[3].ParameterType;
        var insightType = insightsParameterType.GetGenericArguments()[0];
        var emptyInsights = Array.CreateInstance(insightType, 0);
        var result = ResolveRoutingStrategyMethod.Invoke(null, new object?[] {
            true,
            false,
            false,
            emptyInsights,
            5,
            0
        });
        var strategy = Assert.IsType<string>(result);

        Assert.Equal("no_tools", strategy);
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

    [Fact]
    public void BuildRoutingMetaPayload_ClampsSelectedCountToTotal() {
        var result = BuildRoutingMetaPayloadMethod.Invoke(null, new object?[] {
            "weighted_heuristic",
            true,
            false,
            false,
            14,
            6,
            2,
            false
        });
        var payload = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal(6, root.GetProperty("selectedToolCount").GetInt32());
        Assert.Equal(6, root.GetProperty("totalToolCount").GetInt32());
        Assert.False(root.GetProperty("reducedToolSet").GetBoolean());
    }

    [Fact]
    public void BuildRoutingMetaPayload_NormalizesDegenerateNoToolsCounts() {
        var result = BuildRoutingMetaPayloadMethod.Invoke(null, new object?[] {
            "no_tools",
            true,
            false,
            false,
            5,
            0,
            0,
            false
        });
        var payload = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal("no_tools", root.GetProperty("strategy").GetString());
        Assert.Equal(0, root.GetProperty("selectedToolCount").GetInt32());
        Assert.Equal(0, root.GetProperty("totalToolCount").GetInt32());
        Assert.False(root.GetProperty("reducedToolSet").GetBoolean());
    }
}

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public void ResolveRoutingStrategy_UsesStructuredPlannerInsightWithoutReasonText() {
        var plannerInsight = CreateToolRoutingInsight(
            toolName: "ad_replication_summary",
            confidence: "high",
            score: 1d,
            reason: "top ranked by planner",
            strategyName: "SemanticPlanner");
        var insights = CreateRoutingInsightsArray(plannerInsight);

        var result = ResolveRoutingStrategyMethod.Invoke(null, new object?[] {
            true,
            false,
            false,
            insights,
            4,
            12
        });

        Assert.Equal("semantic_planner", Assert.IsType<string>(result));
    }

    [Fact]
    public void HasPlannerInsight_FallsBackToReasonTextWhenStructuredStrategyIsUnknown() {
        var plannerInsight = CreateToolRoutingInsight(
            toolName: "ad_replication_summary",
            confidence: "high",
            score: 1d,
            reason: "semantic planner selection",
            strategyName: "Unknown");
        var insights = CreateRoutingInsightsArray(plannerInsight);

        var result = HasPlannerInsightMethod.Invoke(null, new object?[] { insights });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Theory]
    [InlineData("WeightedHeuristic", "weighted_heuristic")]
    [InlineData("ContinuationSubset", "continuation_subset")]
    [InlineData("SemanticPlanner", "semantic_planner")]
    [InlineData("Unknown", "")]
    public void ResolveRoutingInsightStrategyLabel_MapsStructuredStrategyValues(string strategyName, string expectedLabel) {
        var strategyValue = CreateToolRoutingInsightStrategy(strategyName);

        var result = ResolveRoutingInsightStrategyLabelMethod.Invoke(null, new[] { strategyValue });

        Assert.Equal(expectedLabel, Assert.IsType<string>(result));
    }

    [Fact]
    public void ResolveRoutingInsightStrategy_UsesStructuredStrategyWithoutReasonText() {
        var continuationInsight = CreateToolRoutingInsight(
            toolName: "ad_replication_summary",
            confidence: "high",
            score: 1d,
            reason: "reused prior subset window",
            strategyName: "ContinuationSubset");

        var result = ResolveRoutingInsightStrategyMethod.Invoke(
            null,
            new object?[] { continuationInsight, "weighted_heuristic" });

        Assert.Equal("continuation_subset", Assert.IsType<string>(result));
    }

    [Fact]
    public void ResolveRoutingInsightStrategy_FallsBackToReasonTextWhenStructuredStrategyIsUnknown() {
        var continuationInsight = CreateToolRoutingInsight(
            toolName: "ad_replication_summary",
            confidence: "high",
            score: 1d,
            reason: "continuation follow-up reuse",
            strategyName: "Unknown");

        var result = ResolveRoutingInsightStrategyMethod.Invoke(
            null,
            new object?[] { continuationInsight, "weighted_heuristic" });

        Assert.Equal("continuation_subset", Assert.IsType<string>(result));
    }

    [Fact]
    public void NormalizeRoutingToolCounts_KeepsRoutingStrategyAndMetaConsistentForDegenerateRawCounts() {
        var normalizedObj = NormalizeRoutingToolCountsMethod.Invoke(null, new object?[] { 5, 0 });
        var normalized = Assert.IsAssignableFrom<ITuple>(normalizedObj);
        var selectedToolCount = Assert.IsType<int>(normalized[0]);
        var totalToolCount = Assert.IsType<int>(normalized[1]);
        Assert.Equal(0, selectedToolCount);
        Assert.Equal(0, totalToolCount);

        var insightsParameterType = ResolveRoutingStrategyMethod.GetParameters()[3].ParameterType;
        var insightType = insightsParameterType.GetGenericArguments()[0];
        var emptyInsights = Array.CreateInstance(insightType, 0);
        var strategyResult = ResolveRoutingStrategyMethod.Invoke(null, new object?[] {
            true,
            false,
            false,
            emptyInsights,
            selectedToolCount,
            totalToolCount
        });
        var strategy = Assert.IsType<string>(strategyResult);
        Assert.Equal("no_tools", strategy);

        var payloadResult = BuildRoutingMetaPayloadMethod.Invoke(null, new object?[] {
            strategy,
            true,
            false,
            false,
            selectedToolCount,
            totalToolCount,
            0,
            false,
            null,
            null,
            null,
            false,
            null,
            null
        });
        var payload = Assert.IsType<string>(payloadResult);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal("no_tools", root.GetProperty("strategy").GetString());
        Assert.Equal(0, root.GetProperty("selectedToolCount").GetInt32());
        Assert.Equal(0, root.GetProperty("totalToolCount").GetInt32());
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
            true,
            null,
            null,
            null,
            false,
            null,
            null
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
            false,
            null,
            null,
            null,
            false,
            null,
            null
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
            false,
            null,
            null,
            null,
            false,
            null,
            null
        });
        var payload = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        Assert.Equal("no_tools", root.GetProperty("strategy").GetString());
        Assert.Equal(0, root.GetProperty("selectedToolCount").GetInt32());
        Assert.Equal(0, root.GetProperty("totalToolCount").GetInt32());
        Assert.False(root.GetProperty("reducedToolSet").GetBoolean());
    }

    [Fact]
    public void BuildRoutingMetaPayload_IncludesToolCandidateBudgetDiagnostics() {
        var result = BuildRoutingMetaPayloadMethod.Invoke(null, new object?[] {
            "weighted_heuristic",
            true,
            false,
            false,
            4,
            19,
            2,
            false,
            null,
            4,
            8192L,
            true,
            null,
            null
        });
        var payload = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var budget = root.GetProperty("toolCandidateBudget");
        Assert.Equal(4, budget.GetProperty("effective").GetInt32());
        Assert.True(budget.GetProperty("contextAwareBudgetApplied").GetBoolean());
        Assert.Equal(8192L, budget.GetProperty("effectiveModelContextLength").GetInt64());
        Assert.Equal(JsonValueKind.Null, budget.GetProperty("requested").ValueKind);
    }

    [Fact]
    public void BuildRoutingMetaPayload_IncludesDomainIntentContextWhenProvided() {
        var result = BuildRoutingMetaPayloadMethod.Invoke(null, new object?[] {
            "domain_signal_hint",
            true,
            false,
            false,
            3,
            9,
            1,
            false,
            null,
            5,
            16384L,
            false,
            "signal_hint",
            "public_domain"
        });
        var payload = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var domainIntent = root.GetProperty("domainIntent");
        Assert.Equal("signal_hint", domainIntent.GetProperty("source").GetString());
        Assert.Equal("public_domain", domainIntent.GetProperty("family").GetString());
    }

    private static object CreateToolRoutingInsight(string toolName, string confidence, double score, string reason, string strategyName) {
        var insightType = ResolveRoutingStrategyMethod.GetParameters()[3].ParameterType.GetGenericArguments()[0];
        var strategyProperty = insightType.GetProperty("Strategy", BindingFlags.Public | BindingFlags.Instance)
                               ?? throw new InvalidOperationException("ToolRoutingInsight.Strategy property not found.");
        var strategyType = strategyProperty.PropertyType;
        var strategyValue = Enum.Parse(strategyType, strategyName, ignoreCase: true);

        var value = Activator.CreateInstance(
            insightType,
            toolName,
            confidence,
            score,
            reason,
            strategyValue);

        return value ?? throw new InvalidOperationException("ToolRoutingInsight instance could not be created.");
    }

    private static object CreateToolRoutingInsightStrategy(string strategyName) {
        var insightType = ResolveRoutingStrategyMethod.GetParameters()[3].ParameterType.GetGenericArguments()[0];
        var strategyProperty = insightType.GetProperty("Strategy", BindingFlags.Public | BindingFlags.Instance)
                               ?? throw new InvalidOperationException("ToolRoutingInsight.Strategy property not found.");
        var strategyType = strategyProperty.PropertyType;
        return Enum.Parse(strategyType, strategyName, ignoreCase: true);
    }

    private static Array CreateRoutingInsightsArray(params object[] insights) {
        var insightType = ResolveRoutingStrategyMethod.GetParameters()[3].ParameterType.GetGenericArguments()[0];
        var array = Array.CreateInstance(insightType, insights.Length);
        for (var i = 0; i < insights.Length; i++) {
            array.SetValue(insights[i], i);
        }

        return array;
    }
}

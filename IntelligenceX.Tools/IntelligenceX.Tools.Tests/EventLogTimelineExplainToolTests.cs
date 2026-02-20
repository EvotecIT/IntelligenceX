using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogTimelineExplainToolTests {
    [Fact]
    public async Task TimelineExplain_WhenGoalInvalid_ReturnsInvalidArgument() {
        var tool = new EventLogTimelineExplainTool(new EventLogToolOptions());
        var args = new JsonObject().Add("investigation_goal", "unknown_goal");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("investigation_goal", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TimelineExplain_WhenGoalProfileProvided_UsesProfileRecommendation() {
        var tool = new EventLogTimelineExplainTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("investigation_goal", "actor_activity")
            .Add("timeline_count", 50)
            .Add("groups_count", 10);

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var timelineQuery = GetAnyProperty(root, "timeline_query", "timelineQuery");
        Assert.True(GetAnyProperty(timelineQuery, "use_correlation_profile", "useCorrelationProfile").GetBoolean());
        Assert.Equal("actor_activity", GetAnyProperty(timelineQuery, "correlation_profile", "correlationProfile").GetString());

        var keys = GetAnyProperty(timelineQuery, "correlation_keys", "correlationKeys")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Contains("who", keys);
        Assert.Contains("action", keys);
    }

    [Fact]
    public async Task TimelineExplain_WhenObservedKeysSparse_FallsBackToExplicitKeys() {
        var tool = new EventLogTimelineExplainTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("investigation_goal", "identity")
            .Add("correlation_keys_present", new JsonArray().Add("action"))
            .Add("include_ad_enrichment", false);

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var timelineQuery = GetAnyProperty(root, "timeline_query", "timelineQuery");
        Assert.False(GetAnyProperty(timelineQuery, "use_correlation_profile", "useCorrelationProfile").GetBoolean());
        Assert.Equal("identity", GetAnyProperty(timelineQuery, "correlation_profile", "correlationProfile").GetString());

        var keys = GetAnyProperty(timelineQuery, "correlation_keys", "correlationKeys")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Single(keys);
        Assert.Equal("action", keys[0]);

        var followUpTools = GetAnyProperty(root, "follow_up_tools", "followUpTools")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.DoesNotContain("ad_search", followUpTools);
    }

    private static JsonElement GetAnyProperty(JsonElement element, params string[] names) {
        for (var i = 0; i < names.Length; i++) {
            if (element.TryGetProperty(names[i], out var value)) {
                return value;
            }
        }

        var available = string.Join(
            ", ",
            element.EnumerateObject().Select(static property => property.Name));
        throw new KeyNotFoundException($"None of the properties [{string.Join(", ", names)}] were found. Available: {available}");
    }
}

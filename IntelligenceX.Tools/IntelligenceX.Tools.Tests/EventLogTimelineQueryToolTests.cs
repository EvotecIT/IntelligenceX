using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogTimelineQueryToolTests {
    [Fact]
    public async Task TimelineQuery_WhenNamedEventsAndCategoriesMissing_ReturnsInvalidArgument() {
        var tool = new EventLogTimelineQueryTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("Provide at least one of: named_events, categories.", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TimelineQuery_WhenCorrelationKeyInvalid_ReturnsInvalidArgument() {
        var tool = new EventLogTimelineQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("named_events", new JsonArray().Add("ad_user_logon"))
            .Add("correlation_keys", new JsonArray().Add("invalid_dimension"));

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("correlation", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TimelineQuery_WhenCorrelationProfileInvalid_ReturnsInvalidArgument() {
        var tool = new EventLogTimelineQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("named_events", new JsonArray().Add("ad_user_logon"))
            .Add("correlation_profile", "not_a_profile");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("correlation_profile", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TimelineQuery_WhenCorrelationProfileCombinedWithKeys_ReturnsInvalidArgument() {
        var tool = new EventLogTimelineQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("named_events", new JsonArray().Add("ad_user_logon"))
            .Add("correlation_profile", "identity")
            .Add("correlation_keys", new JsonArray().Add("who"));

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("cannot be combined", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TimelineQuery_WhenTimePeriodAndRangeBothProvided_ReturnsInvalidArgument() {
        var tool = new EventLogTimelineQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("named_events", new JsonArray().Add("ad_user_logon"))
            .Add("time_period", "last_7_days")
            .Add("start_time_utc", "2026-02-01T00:00:00Z");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("time_period cannot be combined", root.GetProperty("error").GetString());
    }
}

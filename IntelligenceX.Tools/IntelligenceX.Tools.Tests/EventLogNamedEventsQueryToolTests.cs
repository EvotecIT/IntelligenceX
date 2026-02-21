using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EventLogNamedEventsQueryToolTests {
    [Fact]
    public async Task InvokeAsync_WhenQuerySucceeds_EmitsChainingContractFields() {
        var tool = new EventLogNamedEventsQueryTool(new EventLogToolOptions());
        var namedEvent = SelectNamedEventQueryName();
        var start = DateTime.UtcNow.AddDays(1);
        var end = start.AddHours(1);

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("named_events", new JsonArray().Add(namedEvent))
                .Add("start_time_utc", start.ToString("O"))
                .Add("end_time_utc", end.ToString("O"))
                .Add("max_events", 1)
                .Add("max_threads", 1)
                .Add("include_payload", false),
            cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());

        Assert.True(root.TryGetProperty("next_actions", out var nextActions));
        Assert.True(root.TryGetProperty("cursor", out var cursor));
        Assert.True(root.TryGetProperty("resume_token", out var resumeToken));
        Assert.True(root.TryGetProperty("handoff", out var handoff));
        Assert.True(root.TryGetProperty("confidence", out var confidence));

        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, nextActions.ValueKind);
        Assert.True(nextActions.GetArrayLength() >= 2);
        Assert.Equal("ad_handoff_prepare", nextActions[0].GetProperty("tool").GetString());
        Assert.Equal("eventlog_entity_handoff", handoff.GetProperty("contract").GetString());
        Assert.False(string.IsNullOrWhiteSpace(cursor.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(resumeToken.GetString()));
        Assert.InRange(confidence.GetDouble(), 0d, 1d);
    }

    private static string SelectNamedEventQueryName() {
        var preferred = EventLogNamedEventsHelper.GetCatalogRows()
            .Where(static row => row.Available)
            .FirstOrDefault(row =>
                row.LogNames.Any(static logName =>
                    string.Equals(logName, "Application", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(logName, "System", StringComparison.OrdinalIgnoreCase)));

        if (preferred is not null) {
            return preferred.QueryName;
        }

        return EventLogNamedEventsHelper.GetCatalogRows()
            .First(static row => row.Available)
            .QueryName;
    }
}

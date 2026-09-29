using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EvtxV4IntegrationTests {
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Security4625.evtx");

    [Fact]
    public async Task EvtxQueryReadsFilteredSecurityEvent() {
        if (!OperatingSystem.IsWindows()) return;
        var tool = new EventLogEvtxQueryTool(CreateOptions());
        var result = await tool.InvokeAsync(new JsonObject()
            .Add("path", FixturePath)
            .Add("event_ids", new JsonArray().Add(4625))
            .Add("max_events", 1), CancellationToken.None);
        using var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean(), result);
        Assert.Equal(1, json.RootElement.GetProperty("count").GetInt32());
        Assert.Equal(4625, json.RootElement.GetProperty("events")[0].GetProperty("id").GetInt32());
        Assert.False(json.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task EvtxStatsCountsFilteredSecurityEvent() {
        if (!OperatingSystem.IsWindows()) return;
        var tool = new EventLogEvtxStatsTool(CreateOptions());
        var result = await tool.InvokeAsync(new JsonObject()
            .Add("path", FixturePath)
            .Add("event_ids", new JsonArray().Add(4625))
            .Add("max_events_scanned", 10), CancellationToken.None);
        using var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean(), result);
        Assert.Equal(1, json.RootElement.GetProperty("scanned_events").GetInt32());
        Assert.Equal(4625, json.RootElement.GetProperty("top_event_ids")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task EvtxSecuritySummaryRecognizesFailedLogon() {
        if (!OperatingSystem.IsWindows()) return;
        var tool = new EventLogEvtxSecuritySummaryTool(CreateOptions());
        var result = await tool.InvokeAsync(new JsonObject()
            .Add("path", FixturePath)
            .Add("report_kind", "failed_logons")
            .Add("max_events_scanned", 10), CancellationToken.None);
        using var json = JsonDocument.Parse(result);
        Assert.True(json.RootElement.GetProperty("ok").GetBoolean(), result);
        var report = json.RootElement.GetProperty("report");
        Assert.Equal(4625, report.GetProperty("event_id").GetInt32());
        Assert.Equal(1, report.GetProperty("matched_events").GetInt32());
    }

    private static EventLogToolOptions CreateOptions() {
        var options = new EventLogToolOptions();
        options.AllowedRoots.Add(Path.GetDirectoryName(FixturePath)!);
        return options;
    }
}

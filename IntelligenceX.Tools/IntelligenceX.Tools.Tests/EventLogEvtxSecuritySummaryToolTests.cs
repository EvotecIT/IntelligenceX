using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogEvtxSecuritySummaryToolTests {
    [Fact]
    public async Task InvokeAsync_WhenPathMissing_ReturnsInvalidArgument() {
        var tool = new EventLogEvtxSecuritySummaryTool(new EventLogToolOptions());

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("path", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenReportKindUnsupported_ReturnsInvalidArgument() {
        var tool = new EventLogEvtxSecuritySummaryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("path", "security.evtx")
            .Add("report_kind", "unknown_kind");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("report_kind", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenAllowedRootsEmpty_ReturnsAccessDenied() {
        var tool = new EventLogEvtxSecuritySummaryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("path", "security.evtx")
            .Add("report_kind", "failed_logons");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("access_denied", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WhenTimeRangeInvalid_ReturnsInvalidArgument() {
        var tool = new EventLogEvtxSecuritySummaryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("path", "security.evtx")
            .Add("report_kind", "user_logons")
            .Add("start_time_utc", "2026-02-20T12:00:00Z")
            .Add("end_time_utc", "2026-02-19T12:00:00Z");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("start_time_utc", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}

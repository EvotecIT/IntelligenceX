using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogLiveQueryToolTests {
    [Fact]
    public async Task LiveQuery_WhenXPathCombinedWithStructuredFilters_ReturnsInvalidArgument() {
        var tool = new EventLogLiveQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("log_name", "Security")
            .Add("xpath", "*[System[EventID=4624]]")
            .Add("level", "error");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("xpath cannot be combined", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveQuery_WhenKeywordsInvalid_ReturnsInvalidArgument() {
        var tool = new EventLogLiveQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("log_name", "Security")
            .Add("keywords", "not_a_keyword");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("keywords", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveQuery_WhenNamedDataFilterIsNotObject_ReturnsInvalidArgument() {
        var tool = new EventLogLiveQueryTool(new EventLogToolOptions());
        var args = new JsonObject()
            .Add("log_name", "Security")
            .Add("named_data_filter", "not_object");

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("named_data_filter", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }
}

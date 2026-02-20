using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogEvtxQueryToolTests {
    [Fact]
    public async Task EvtxQuery_WhenLevelInvalid_ReturnsInvalidArgument() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-evtx-query-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);
        try {
            var evtx = Path.Combine(tempRoot, "System.evtx");
            File.WriteAllText(evtx, "x");

            var options = new EventLogToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new EventLogEvtxQueryTool(options);

            var args = new JsonObject()
                .Add("path", evtx)
                .Add("level", "not_a_level");

            var json = await tool.InvokeAsync(args, CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
            Assert.Contains("level", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task EvtxQuery_WhenEventRecordIdsNotArray_ReturnsInvalidArgument() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-evtx-query-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);
        try {
            var evtx = Path.Combine(tempRoot, "System.evtx");
            File.WriteAllText(evtx, "x");

            var options = new EventLogToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new EventLogEvtxQueryTool(options);

            var args = new JsonObject()
                .Add("path", evtx)
                .Add("event_record_ids", "1");

            var json = await tool.InvokeAsync(args, CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
            Assert.Contains("event_record_ids", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public async Task EvtxQuery_WhenNamedDataFilterIsNotObject_ReturnsInvalidArgument() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-evtx-query-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);
        try {
            var evtx = Path.Combine(tempRoot, "System.evtx");
            File.WriteAllText(evtx, "x");

            var options = new EventLogToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new EventLogEvtxQueryTool(options);

            var args = new JsonObject()
                .Add("path", evtx)
                .Add("named_data_filter", "x");

            var json = await tool.InvokeAsync(args, CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
            Assert.Contains("named_data_filter", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }
}

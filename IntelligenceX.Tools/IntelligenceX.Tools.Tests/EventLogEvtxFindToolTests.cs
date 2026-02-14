using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogEvtxFindToolTests {
    [Fact]
    public async Task EvtxFind_WhenAllowedRootsEmpty_ReturnsAccessDenied() {
        var tool = new EventLogEvtxFindTool(new EventLogToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("access_denied", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public async Task EvtxFind_EmitsColumnsThatMapToRows() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-evtx-find-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);
        try {
            var evtx = Path.Combine(tempRoot, "ADO-System.evtx");
            File.WriteAllText(evtx, "x");

            var options = new EventLogToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new EventLogEvtxFindTool(options);

            var args = new JsonObject()
                .Add("query", "ADO System")
                .Add("log_name", "System")
                .Add("max_results", 10);

            var json = await tool.InvokeAsync(args, CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());

            var files = root.GetProperty("files").EnumerateArray().ToArray();
            Assert.Single(files);

            var render = root.GetProperty("render");
            var columns = render.GetProperty("columns").EnumerateArray()
                .Select(static x => x.GetProperty("key").GetString())
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
            Assert.NotEmpty(columns);

            foreach (var key in columns) {
                Assert.True(files[0].TryGetProperty(key!, out _), $"Missing row property for column key '{key}'.");
            }
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup: tests shouldn't fail on temp directory races/locks.
            }
        }
    }

    [Fact]
    public async Task EvtxFind_WhenMoreThanMaxResultsExist_SetsTruncatedAndLimitsRows() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-evtx-find-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tempRoot);
        try {
            File.WriteAllText(Path.Combine(tempRoot, "a.evtx"), "x");
            File.WriteAllText(Path.Combine(tempRoot, "b.evtx"), "x");
            File.WriteAllText(Path.Combine(tempRoot, "c.evtx"), "x");

            var options = new EventLogToolOptions();
            options.AllowedRoots.Add(tempRoot);
            var tool = new EventLogEvtxFindTool(options);

            var args = new JsonObject().Add("max_results", 2);
            var json = await tool.InvokeAsync(args, CancellationToken.None);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("ok").GetBoolean());

            var files = root.GetProperty("files").EnumerateArray().ToArray();
            Assert.Equal(2, files.Length);

            Assert.True(root.GetProperty("meta").GetProperty("truncated").GetBoolean());
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }
}


using System;
using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolResultV2Tests {
    private sealed record Row(string Path, string Type);

    [Fact]
    public void OkWriteActionModel_ShouldNotMutateCallerMetaObject() {
        var meta = new JsonObject()
            .Add("trace_id", "abc-123");

        var json = ToolResultV2.OkWriteActionModel(
            model: new { Result = "ok" },
            action: "demo_action",
            writeApplied: true,
            meta: meta);

        Assert.Null(meta.GetString("mode"));
        Assert.Null(meta.GetString("action"));
        Assert.False(meta.GetBoolean("write_applied", defaultValue: false));
        Assert.Equal("abc-123", meta.GetString("trace_id"));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var responseMeta = root.GetProperty("meta");
        Assert.Equal("apply", responseMeta.GetProperty("mode").GetString());
        Assert.Equal("demo_action", responseMeta.GetProperty("action").GetString());
        Assert.True(responseMeta.GetProperty("write_applied").GetBoolean());
        Assert.Equal("abc-123", responseMeta.GetProperty("trace_id").GetString());
    }

    [Fact]
    public void OkAutoTableResponse_ShouldEmitViewRowsEnvelope() {
        var model = new {
            Entries = new[] {
                new Row(Path: @"C:\Temp\a.txt", Type: "file")
            }
        };
        var rows = new[] {
            new Row(Path: @"C:\Temp\a.txt", Type: "file")
        };

        var json = ToolResultV2.OkAutoTableResponse(
            arguments: null,
            model: model,
            sourceRows: rows,
            viewRowsPath: "entries_view",
            title: "Entries",
            baseTruncated: false,
            maxTop: 100);

        Assert.Contains("\"ok\":true", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"entries_view\"", json, StringComparison.Ordinal);
        Assert.Contains("\"table\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OkFlatWithRenderValue_WithMeta_ShouldCloneMetaAndRender() {
        var root = new JsonObject().Add("status", "ok");
        var meta = new JsonObject().Add("count", 1);
        var render = JsonValue.From(new JsonArray().Add(new JsonObject().Add("kind", "table")));

        var json = ToolResultV2.OkFlatWithRenderValue(
            root: root,
            meta: meta,
            summaryMarkdown: "summary",
            render: render);

        meta.Add("count", 5);

        using var document = JsonDocument.Parse(json);
        var response = document.RootElement;
        Assert.True(response.GetProperty("ok").GetBoolean());
        Assert.Equal("ok", response.GetProperty("status").GetString());
        Assert.Equal(1, response.GetProperty("meta").GetProperty("count").GetInt32());
        Assert.Equal("table", response.GetProperty("render")[0].GetProperty("kind").GetString());
    }
}

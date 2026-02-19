using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.FileSystem;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class FileSystemToolBaseHelperTests {
    [Fact]
    public void ResolveBoundedOptionLimit_ShouldClampToMinAndCapToOptionsMax() {
        var tool = new HarnessTool(maxResults: 30);

        Assert.Equal(30, tool.ResolveLimit("max_matches", arguments: null));
        Assert.Equal(1, tool.ResolveLimit("max_matches", new JsonObject().Add("max_matches", 0)));
        Assert.Equal(30, tool.ResolveLimit("max_matches", new JsonObject().Add("max_matches", 999)));
        Assert.Equal(7, tool.ResolveLimit("max_matches", new JsonObject().Add("max_matches", 7)));
    }

    [Fact]
    public void BuildAutoTableResponse_ShouldReturnInvalidArgumentEnvelopeForUnsupportedColumns() {
        var rows = new[] { new AutoRow("file.txt") };
        var model = new AutoModel { Items = rows };

        var response = HarnessTool.BuildAutoResponse(
            arguments: new JsonObject().Add("columns", new JsonArray().Add("missing_column")),
            model: model,
            rows: rows);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
    }

    private sealed record AutoRow(string Name);

    private sealed class AutoModel {
        public IReadOnlyList<AutoRow> Items { get; init; } = Array.Empty<AutoRow>();
    }

    private sealed class HarnessTool : FileSystemToolBase {
        private static readonly ToolDefinition DefinitionValue = new(
            "fs_test_harness",
            "File system helper harness.",
            ToolSchema.Object().NoAdditionalProperties());

        public HarnessTool(int maxResults) : base(new FileSystemToolOptions { MaxResults = maxResults }) { }

        public override ToolDefinition Definition => DefinitionValue;

        public int ResolveLimit(string argumentName, JsonObject? arguments) {
            return ResolveBoundedOptionLimit(arguments, argumentName);
        }

        public static string BuildAutoResponse(JsonObject? arguments, AutoModel model, IReadOnlyList<AutoRow> rows) {
            return BuildAutoTableResponse(
                arguments: arguments,
                model: model,
                sourceRows: rows,
                viewRowsPath: "items_view",
                title: "Items",
                baseTruncated: false,
                maxTop: 100);
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }
    }
}

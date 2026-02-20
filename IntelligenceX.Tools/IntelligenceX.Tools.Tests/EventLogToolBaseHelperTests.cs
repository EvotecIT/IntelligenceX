using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogToolBaseHelperTests {
    [Fact]
    public void ResolveBoundedOptionLimit_ShouldClampToMinAndCapToOptionsMax() {
        var tool = new HarnessTool(maxResults: 60);

        Assert.Equal(60, tool.ResolveLimit("max_events", arguments: null));
        Assert.Equal(1, tool.ResolveLimit("max_events", new JsonObject().Add("max_events", 0)));
        Assert.Equal(1, tool.ResolveLimit("max_events", new JsonObject().Add("max_events", -2)));
        Assert.Equal(60, tool.ResolveLimit("max_events", new JsonObject().Add("max_events", 900)));
        Assert.Equal(15, tool.ResolveLimit("max_events", new JsonObject().Add("max_events", 15)));
    }

    [Fact]
    public void ResolveCappedMaxResults_ShouldHonorExplicitDefaultAndCap() {
        var tool = new HarnessTool(maxResults: 200);

        Assert.Equal(20, tool.ResolveCappedMax(arguments: null, defaultValue: 20, maxInclusive: 80));
        Assert.Equal(1, tool.ResolveCappedMax(new JsonObject().Add("max_results", 0), defaultValue: 20, maxInclusive: 80));
        Assert.Equal(80, tool.ResolveCappedMax(new JsonObject().Add("max_results", 500), defaultValue: 20, maxInclusive: 80));
        Assert.Equal(35, tool.ResolveCappedMax(new JsonObject().Add("max_results", 35), defaultValue: 20, maxInclusive: 80));
    }

    [Fact]
    public void ResolveOptionBoundedMaxResults_ShouldUseSharedOptionBoundedBehavior() {
        var tool = new HarnessTool(maxResults: 60);

        Assert.Equal(60, tool.ResolveOptionBoundedMax(arguments: null));
        Assert.Equal(1, tool.ResolveOptionBoundedMax(new JsonObject().Add("max_results", 0)));
        Assert.Equal(60, tool.ResolveOptionBoundedMax(new JsonObject().Add("max_results", 999)));
        Assert.Equal(22, tool.ResolveOptionBoundedMax(new JsonObject().Add("max_results", 22)));
    }

    [Fact]
    public void AddMaxResultsMeta_ShouldPopulateMetaField() {
        var meta = new JsonObject();
        HarnessTool.AddMax(meta, 88);

        Assert.Equal(88, meta.GetInt64("max_results"));
    }

    [Fact]
    public void ResolveSessionTimeoutMs_FromArguments_ShouldClampToBounds() {
        Assert.Null(HarnessTool.ResolveTimeoutFromArguments(arguments: null));
        Assert.Null(HarnessTool.ResolveTimeoutFromArguments(new JsonObject().Add("session_timeout_ms", 0)));
        Assert.Equal(250, HarnessTool.ResolveTimeoutFromArguments(new JsonObject().Add("session_timeout_ms", 10)));
        Assert.Equal(300_000, HarnessTool.ResolveTimeoutFromArguments(new JsonObject().Add("session_timeout_ms", 999_999)));
        Assert.Equal(1_000, HarnessTool.ResolveTimeoutFromArguments(
            new JsonObject().Add("session_timeout_ms", 500),
            minInclusive: 1_000,
            maxInclusive: 600_000));
    }

    [Fact]
    public void ResolveSessionTimeoutMs_FromRaw_ShouldClampToBounds() {
        Assert.Null(HarnessTool.ResolveTimeoutFromRaw(null));
        Assert.Null(HarnessTool.ResolveTimeoutFromRaw(0));
        Assert.Equal(250, HarnessTool.ResolveTimeoutFromRaw(10));
        Assert.Equal(300_000, HarnessTool.ResolveTimeoutFromRaw(999_999));
        Assert.Equal(1_000, HarnessTool.ResolveTimeoutFromRaw(500, minInclusive: 1_000, maxInclusive: 600_000));
    }

    [Fact]
    public void ResolveXPathOrDefault_ShouldReturnWildcardForMissingOrBlank() {
        Assert.Equal("*", HarnessTool.ResolveXPath(arguments: null));
        Assert.Equal("*", HarnessTool.ResolveXPath(new JsonObject()));
        Assert.Equal("*", HarnessTool.ResolveXPath(new JsonObject().Add("xpath", " ")));
        Assert.Equal("*[System]", HarnessTool.ResolveXPath(new JsonObject().Add("xpath", "*[System]")));
    }

    [Fact]
    public void BuildAutoTableResponse_ShouldReturnInvalidArgumentEnvelopeForUnsupportedColumns() {
        var rows = new[] { new AutoRow(1, "alpha") };
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

    [Fact]
    public void ErrorFromException_ShouldMapUnauthorizedAccessToAccessDeniedEnvelope() {
        var response = HarnessTool.MapException(new UnauthorizedAccessException("denied"));

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("access_denied", root.GetProperty("error_code").GetString());
    }

    private sealed record AutoRow(int Id, string DisplayName);

    private sealed class AutoModel {
        public IReadOnlyList<AutoRow> Items { get; init; } = Array.Empty<AutoRow>();
    }

    private sealed class HarnessTool : EventLogToolBase {
        private static readonly ToolDefinition DefinitionValue = new(
            "eventlog_test_harness",
            "Event log helper harness.",
            ToolSchema.Object().NoAdditionalProperties());

        public HarnessTool(int maxResults) : base(new EventLogToolOptions { MaxResults = maxResults }) { }

        public override ToolDefinition Definition => DefinitionValue;

        public int ResolveLimit(string argumentName, JsonObject? arguments) {
            return ResolveBoundedOptionLimit(arguments, argumentName);
        }

        public int ResolveCappedMax(JsonObject? arguments, int defaultValue, int? maxInclusive = null) {
            return ResolveCappedMaxResults(arguments, defaultValue, maxInclusive: maxInclusive);
        }

        public int ResolveOptionBoundedMax(JsonObject? arguments) {
            return ResolveOptionBoundedMaxResults(arguments);
        }

        public static void AddMax(JsonObject meta, int maxResults) {
            AddMaxResultsMeta(meta, maxResults);
        }

        public static int? ResolveTimeoutFromArguments(
            JsonObject? arguments,
            int minInclusive = 250,
            int maxInclusive = 300_000) {
            return ResolveSessionTimeoutMs(arguments, minInclusive: minInclusive, maxInclusive: maxInclusive);
        }

        public static int? ResolveTimeoutFromRaw(
            long? timeoutRaw,
            int minInclusive = 250,
            int maxInclusive = 300_000) {
            return ResolveSessionTimeoutMs(timeoutRaw, minInclusive: minInclusive, maxInclusive: maxInclusive);
        }

        public static string ResolveXPath(JsonObject? arguments, string argumentName = "xpath", string defaultXPath = "*") {
            return ResolveXPathOrDefault(arguments, argumentName, defaultXPath);
        }

        public static string BuildAutoResponse(JsonObject? arguments, AutoModel model, IReadOnlyList<AutoRow> rows) {
            return BuildAutoTableResponse(
                arguments: arguments,
                model: model,
                sourceRows: rows,
                viewRowsPath: "items_view",
                title: "Items",
                baseTruncated: false,
                scanned: rows.Count,
                maxTop: 100);
        }

        public static string MapException(Exception ex) {
            return ErrorFromException(ex);
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }
    }
}

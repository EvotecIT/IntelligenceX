using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using EventViewerX.Reports.Inventory;
using EventViewerX.Reports.Live;
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
    public void AddReadOnlyTriageChainingMeta_ShouldEmitNextActionsAndDiscoveryStatus() {
        var meta = new JsonObject();

        HarnessTool.AddReadOnlyChaining(
            meta: meta,
            currentTool: "eventlog_top_events",
            logName: "System",
            machineName: "dc01.contoso.local",
            suggestedMaxEvents: 10,
            scanned: 10,
            truncated: false,
            queryMode: "top_events");

        var nextActions = meta.GetArray("next_actions");
        Assert.NotNull(nextActions);
        Assert.True(nextActions!.Count >= 2);

        var hasLiveQuery = false;
        var hasLiveStats = false;
        foreach (var value in nextActions) {
            var action = value.AsObject();
            var tool = action?.GetString("tool");
            if (string.Equals(tool, "eventlog_live_query", StringComparison.OrdinalIgnoreCase)) {
                hasLiveQuery = true;
            } else if (string.Equals(tool, "eventlog_live_stats", StringComparison.OrdinalIgnoreCase)) {
                hasLiveStats = true;
            }
        }

        Assert.True(hasLiveQuery);
        Assert.True(hasLiveStats);

        var discovery = meta.GetObject("discovery_status");
        Assert.NotNull(discovery);
        Assert.Equal("remote", discovery!.GetString("scope"));
        Assert.Equal("System", discovery.GetString("log_name"));
        Assert.Equal("top_events", discovery.GetString("query_mode"));
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
    public void BuildAutoTableResponse_ShouldDropUnsupportedProjectionArgumentsAndReturnOkEnvelope() {
        var rows = new[] { new AutoRow(1, "alpha") };
        var model = new AutoModel { Items = rows };

        var response = HarnessTool.BuildAutoResponse(
            arguments: new JsonObject().Add("columns", new JsonArray().Add("missing_column")),
            model: model,
            rows: rows);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void BuildAutoTableResponse_ShouldMapCommonEventLogColumnAliases() {
        var rows = new[] { new EventAutoRow("2026-02-24T08:20:10Z", 4624, "AD0.ad.evotec.xyz") };
        var model = new EventAutoModel { Events = rows };

        var response = HarnessTool.BuildAutoEventResponse(
            arguments: new JsonObject()
                .Add("columns", new JsonArray().Add("time_created").Add("computer").Add("event_id"))
                .Add("sort_by", "time_created")
                .Add("sort_direction", "asc"),
            model: model,
            rows: rows);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        var viewRows = root.GetProperty("events_view");
        Assert.True(viewRows.GetArrayLength() > 0);
        var first = viewRows[0];
        Assert.True(first.TryGetProperty("time_created_utc", out _));
        Assert.True(first.TryGetProperty("machine_name", out _));
        Assert.True(first.TryGetProperty("id", out _));
    }

    [Fact]
    public void ErrorFromException_ShouldMapUnauthorizedAccessToAccessDeniedEnvelope() {
        var response = HarnessTool.MapException(new UnauthorizedAccessException("denied"));

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("access_denied", root.GetProperty("error_code").GetString());
    }

    [Fact]
    public void ErrorFromLiveQueryFailure_RemoteTimeout_ShouldIncludeRemoteHintsAndBeTransient() {
        var response = HarnessTool.MapLiveQueryFailure(
            failure: new LiveEventQueryFailure {
                Kind = LiveEventQueryFailureKind.Timeout,
                Message = "The operation timed out."
            },
            machineName: "dc01.contoso.local",
            logName: "Security");

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("timeout", root.GetProperty("error_code").GetString());
        Assert.True(root.GetProperty("is_transient").GetBoolean());
        Assert.Contains("Remote event log query failed", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        var hints = root.GetProperty("hints");
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("session_timeout_ms", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("export .evtx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ErrorFromLiveStatsFailure_LocalAccessDenied_ShouldIncludeLocalHints() {
        var response = HarnessTool.MapLiveStatsFailure(
            failure: new LiveStatsQueryFailure {
                Kind = LiveStatsQueryFailureKind.AccessDenied,
                Message = "Access denied."
            },
            machineName: null,
            logName: "System");

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("access_denied", root.GetProperty("error_code").GetString());
        Assert.Contains("Local event log stats query failed", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        var hints = root.GetProperty("hints");
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("Event Log read rights", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ErrorFromLiveQueryFailure_RemotePlatformUnsupported_ShouldUsePlatformSpecificHints() {
        var response = HarnessTool.MapLiveQueryFailure(
            failure: new LiveEventQueryFailure {
                Kind = LiveEventQueryFailureKind.Exception,
                Message = "EventLog access is not supported on this platform."
            },
            machineName: "dc03.contoso.local",
            logName: "System");

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("platform_not_supported", root.GetProperty("error_code").GetString());
        Assert.False(root.GetProperty("is_transient").GetBoolean());

        var hints = root.GetProperty("hints");
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("current platform/runtime", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("Windows-native runtime/host", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("Remote Event Log Management / RPC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ErrorFromCatalogFailure_Remote_ShouldIncludeRemoteCatalogHints() {
        var response = HarnessTool.MapCatalogFailure(
            failure: new EventCatalogFailure {
                Kind = EventCatalogFailureKind.Exception,
                Message = "Failed to open event log session."
            },
            machineName: "dc02.contoso.local",
            listingKind: "event log channel listing");

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("query_failed", root.GetProperty("error_code").GetString());
        Assert.Contains("Remote event log channel listing query failed", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

        var hints = root.GetProperty("hints");
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("Remote Event Log Management / RPC", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("export .evtx", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ErrorFromCatalogFailure_RemotePlatformUnsupported_ShouldUsePlatformSpecificHints() {
        var response = HarnessTool.MapCatalogFailure(
            failure: new EventCatalogFailure {
                Kind = EventCatalogFailureKind.Exception,
                Message = "EventLog access is not supported on this platform."
            },
            machineName: "dc04.contoso.local",
            listingKind: "event log channel listing");

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("platform_not_supported", root.GetProperty("error_code").GetString());

        var hints = root.GetProperty("hints");
        Assert.Contains(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("Windows-native runtime/host", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(hints.EnumerateArray(), static value =>
            value.GetString()!.Contains("machine_name", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record AutoRow(int Id, string DisplayName);

    private sealed class AutoModel {
        public IReadOnlyList<AutoRow> Items { get; init; } = Array.Empty<AutoRow>();
    }

    private sealed record EventAutoRow(string TimeCreatedUtc, int Id, string MachineName);

    private sealed class EventAutoModel {
        public IReadOnlyList<EventAutoRow> Events { get; init; } = Array.Empty<EventAutoRow>();
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

        public static void AddReadOnlyChaining(
            JsonObject meta,
            string currentTool,
            string logName,
            string? machineName,
            int suggestedMaxEvents,
            int scanned,
            bool truncated,
            string queryMode) {
            AddReadOnlyTriageChainingMeta(
                meta: meta,
                currentTool: currentTool,
                logName: logName,
                machineName: machineName,
                suggestedMaxEvents: suggestedMaxEvents,
                scanned: scanned,
                truncated: truncated,
                queryMode: queryMode);
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

        public static string BuildAutoEventResponse(JsonObject? arguments, EventAutoModel model, IReadOnlyList<EventAutoRow> rows) {
            return BuildAutoTableResponse(
                arguments: arguments,
                model: model,
                sourceRows: rows,
                viewRowsPath: "events_view",
                title: "Events",
                baseTruncated: false,
                scanned: rows.Count,
                maxTop: 100);
        }

        public static string MapException(Exception ex) {
            return ErrorFromException(ex);
        }

        public static string MapLiveQueryFailure(
            LiveEventQueryFailure? failure,
            string? machineName,
            string? logName) {
            return ErrorFromLiveQueryFailure(failure, machineName, logName);
        }

        public static string MapLiveStatsFailure(
            LiveStatsQueryFailure? failure,
            string? machineName,
            string? logName) {
            return ErrorFromLiveStatsFailure(failure, machineName, logName);
        }

        public static string MapCatalogFailure(
            EventCatalogFailure? failure,
            string? machineName,
            string listingKind) {
            return ErrorFromCatalogFailure(failure, machineName, listingKind);
        }

        protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(ToolResponse.OkModel(new { ok = true }));
        }
    }
}

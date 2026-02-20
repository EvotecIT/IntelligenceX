using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Live;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Reads events from a local Windows Event Log channel using an XPath query.
/// </summary>
public sealed class EventLogLiveQueryTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_live_query",
        "Read events from a Windows Event Log (local or remote machine) using log name + optional XPath filter.",
        ToolSchema.Object(
                ("log_name", ToolSchema.String("Windows Event Log name (for example: System, Security, Application).")),
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("xpath", ToolSchema.String("Optional XPath query (default: '*').")),
                ("max_events", ToolSchema.Integer("Optional maximum events to return (capped).")),
                ("oldest_first", ToolSchema.Boolean("If true, read from oldest to newest (default false).")),
                ("include_message", ToolSchema.Boolean("If true, include formatted message text (may be large).")),
                ("session_timeout_ms", ToolSchema.Integer("Optional remote session timeout in milliseconds (capped).")))
            .WithTableViewOptions()
            .Required("log_name")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogLiveQueryTool"/> class.
    /// </summary>
    public EventLogLiveQueryTool(EventLogToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var logName = arguments?.GetString("log_name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(logName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "log_name is required."));
        }

        var xpath = ResolveXPathOrDefault(arguments);

        var oldestFirst = arguments?.GetBoolean("oldest_first") ?? false;
        var includeMessage = arguments?.GetBoolean("include_message") ?? false;
        var machineName = ToolArgs.GetOptionalTrimmed(arguments, "machine_name");
        var sessionTimeoutMs = ResolveSessionTimeoutMs(arguments, minInclusive: MinSessionTimeoutMs, maxInclusive: MaxSessionTimeoutMs);

        var maxEvents = ResolveBoundedOptionLimit(arguments, "max_events");

        if (!LiveEventQueryExecutor.TryRead(
                request: new LiveEventQueryRequest {
                    LogName = logName,
                    MachineName = machineName,
                    XPath = xpath,
                    MaxEvents = maxEvents,
                    OldestFirst = oldestFirst,
                    IncludeMessage = includeMessage,
                    MaxMessageChars = Options.MaxMessageChars,
                    SessionTimeoutMs = sessionTimeoutMs
                },
                result: out var root,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromLiveQueryFailure(
                failure: failure,
                machineName: machineName,
                logName: logName));
        }

        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: "Live events (preview)",
            baseTruncated: root.Truncated,
            scanned: root.Events.Count,
            maxTop: MaxViewTop);
        return Task.FromResult(response);
    }
}

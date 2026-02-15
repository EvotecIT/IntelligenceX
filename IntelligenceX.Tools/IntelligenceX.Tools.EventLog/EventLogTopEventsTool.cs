using System;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Live;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Returns the most recent N events from a local or remote Windows Event Log channel (quick triage).
/// </summary>
public sealed class EventLogTopEventsTool : EventLogToolBase, ITool {
    private const int DefaultMaxEvents = 5;
    private const int MaxViewTop = 5000;

    private const int DefaultRemoteSessionTimeoutMs = 30_000;
    private const int MinSessionTimeoutMs = 250;
    private const int MaxSessionTimeoutMs = 300_000;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_top_events",
        "Return the most recent N events from a Windows Event Log (local or remote machine). Designed for quick triage (example: \"top 5 events from AD0 System\").",
        ToolSchema.Object(
                ("log_name", ToolSchema.String("Windows Event Log name (for example: System, Security, Application).")),
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("max_events", ToolSchema.Integer("Optional number of most-recent events to return (default 5, capped).")),
                ("include_message", ToolSchema.Boolean("If true, include formatted message text (truncated).")),
                ("session_timeout_ms", ToolSchema.Integer("Optional remote session timeout in milliseconds (capped).")))
            .WithTableViewOptions()
            .Required("log_name")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogTopEventsTool"/> class.
    /// </summary>
    public EventLogTopEventsTool(EventLogToolOptions options) : base(options) { }

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

        // Treat whitespace-only values as "not provided" to avoid accidental remote session behavior.
        var machineName = ToolArgs.GetOptionalTrimmed(arguments, "machine_name");
        if (machineName is { Length: > 260 }) {
            machineName = machineName.Substring(0, 260);
        }
        if (string.IsNullOrWhiteSpace(machineName)) {
            machineName = null;
        }
        if (machineName is not null) {
            foreach (var c in machineName) {
                if (char.IsControl(c)) {
                    return Task.FromResult(ToolResponse.Error("invalid_argument", "machine_name must not contain control characters."));
                }
            }
        }

        // Defensive: options are validated on construction, but keep "top events" semantics stable
        // even if an upstream host misconfigures MaxResults to 0/negative.
        var maxEventsCap = Options.MaxResults <= 0
            ? MaxViewTop
            : Math.Min(Options.MaxResults, MaxViewTop);
        if (maxEventsCap < 1) {
            maxEventsCap = DefaultMaxEvents;
        }
        var maxEvents = ToolArgs.GetPositiveCappedInt32OrDefault(
            arguments: arguments,
            key: "max_events",
            defaultValue: DefaultMaxEvents,
            maxInclusive: maxEventsCap);
        if (maxEvents < 1) {
            maxEvents = 1;
        }

        // Default off: formatting messages can be slow/fragile for remote sessions and is not always needed for triage.
        var includeMessage = arguments?.GetBoolean("include_message") ?? false;

        var sessionTimeoutMs = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("session_timeout_ms"), maxInclusive: MaxSessionTimeoutMs);
        if (sessionTimeoutMs.HasValue && sessionTimeoutMs.Value < MinSessionTimeoutMs) {
            sessionTimeoutMs = MinSessionTimeoutMs;
        }

        if (machineName is not null && !sessionTimeoutMs.HasValue) {
            sessionTimeoutMs = DefaultRemoteSessionTimeoutMs;
        }

        var maxMessageChars = includeMessage
            ? Math.Max(0, Math.Min(Options.MaxMessageChars, 1200))
            : 0;

        if (!LiveEventQueryExecutor.TryRead(
                request: new LiveEventQueryRequest {
                    LogName = logName.Trim(),
                    MachineName = machineName,
                    XPath = "*",
                    MaxEvents = maxEvents,
                    OldestFirst = false,
                    IncludeMessage = includeMessage,
                    MaxMessageChars = maxMessageChars,
                    SessionTimeoutMs = sessionTimeoutMs
                },
                result: out var root,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromLiveQueryFailure(failure));
        }

        if (!ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: root,
            sourceRows: root.Events,
            viewRowsPath: "events_view",
            title: $"Top {maxEvents} recent events (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root.Truncated,
            response: out var response)) {
            return Task.FromResult(ToolResponse.Error("tool_error", "Failed to build table view response envelope."));
        }
        return Task.FromResult(response);
    }
}

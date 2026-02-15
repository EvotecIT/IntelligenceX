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
    private const int MaxMachineNameLength = 260;
    private const int MaxLogNameLength = 260;
    private static readonly SemaphoreSlim RemoteReadGate = new(initialCount: 4, maxCount: 4);

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
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var logName = arguments?.GetString("log_name") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(logName)) {
            return ToolResponse.Error("invalid_argument", "log_name is required.");
        }
        logName = logName.Trim();
        if (logName.Length > MaxLogNameLength) {
            return ToolResponse.Error("invalid_argument", $"log_name must be <= {MaxLogNameLength} characters.");
        }
        foreach (var c in logName) {
            if (char.IsControl(c)) {
                return ToolResponse.Error("invalid_argument", "log_name must not contain control characters.");
            }
        }

        // Treat whitespace-only values as "not provided" to avoid accidental remote session behavior.
        var machineName = ToolArgs.GetOptionalTrimmed(arguments, "machine_name");
        if (string.IsNullOrWhiteSpace(machineName)) {
            machineName = null;
        }
        if (machineName is not null) {
            if (machineName.Length > MaxMachineNameLength) {
                return ToolResponse.Error("invalid_argument", $"machine_name must be <= {MaxMachineNameLength} characters.");
            }
            foreach (var c in machineName) {
                if (char.IsControl(c)) {
                    return ToolResponse.Error("invalid_argument", "machine_name must not contain control characters.");
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
        // Note: tool arguments are untrusted; treat invalid types as "not provided" rather than throwing.
        var includeMessage = ToolArgs.GetBoolean(arguments, "include_message", defaultValue: false);

        long? sessionTimeoutRaw = null;
        if (arguments is not null && arguments.TryGetValue("session_timeout_ms", out var sessionTimeoutValue)) {
            // Treat explicit null as "not provided".
            if (sessionTimeoutValue is not null && sessionTimeoutValue.Kind != JsonValueKind.Null) {
                if (sessionTimeoutValue.Kind != JsonValueKind.Number) {
                    return ToolResponse.Error("invalid_argument", "session_timeout_ms must be a number of milliseconds.");
                }
                // Be defensive with untrusted numeric representations (long/int/double) and avoid undefined casts.
                sessionTimeoutRaw = sessionTimeoutValue.Value switch {
                    long l => l,
                    int i => i,
                    double d => double.IsFinite(d) && d >= long.MinValue && d <= long.MaxValue
                        ? (long)Math.Truncate(d)
                        : null,
                    _ => sessionTimeoutValue.AsInt64()
                };
            }
        }

        var sessionTimeoutMs = ToolArgs.ToPositiveInt32OrNull(sessionTimeoutRaw, maxInclusive: MaxSessionTimeoutMs);
        if (sessionTimeoutMs.HasValue && sessionTimeoutMs.Value < MinSessionTimeoutMs) {
            sessionTimeoutMs = MinSessionTimeoutMs;
        }

        if (machineName is not null && !sessionTimeoutMs.HasValue) {
            sessionTimeoutMs = DefaultRemoteSessionTimeoutMs;
        }

        var maxMessageChars = includeMessage
            ? Math.Max(0, Math.Min(Options.MaxMessageChars, 1200))
            : 0;

        var request = new LiveEventQueryRequest {
            LogName = logName,
            MachineName = machineName,
            XPath = "*",
            MaxEvents = maxEvents,
            OldestFirst = false,
            IncludeMessage = includeMessage,
            MaxMessageChars = maxMessageChars,
            SessionTimeoutMs = sessionTimeoutMs
        };

        LiveEventQueryResult? root;
        LiveEventQueryFailure? failure;
        bool ok;

        if (machineName is null) {
            // Local reads are typically fast and stay on the current thread to reduce overhead.
            ok = LiveEventQueryExecutor.TryRead(
                request: request,
                result: out root,
                failure: out failure,
                cancellationToken: cancellationToken);
        } else {
            // Live querying is synchronous; for remote sessions, use a small concurrency gate to avoid saturating
            // the host with concurrent RPC/session work.
            await RemoteReadGate.WaitAsync(cancellationToken);
            try {
                ok = LiveEventQueryExecutor.TryRead(
                    request: request,
                    result: out root,
                    failure: out failure,
                    cancellationToken: cancellationToken);
            } finally {
                RemoteReadGate.Release();
            }
        }

        if (!ok) {
            return ErrorFromLiveQueryFailure(failure);
        }

        if (!ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: root!,
            sourceRows: root!.Events,
            viewRowsPath: "events_view",
            title: $"Top {maxEvents} recent events (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root!.Truncated,
            response: out var response)) {
            return ToolResponse.Error("tool_error", "Failed to build table view response envelope.");
        }
        return response;
    }
}

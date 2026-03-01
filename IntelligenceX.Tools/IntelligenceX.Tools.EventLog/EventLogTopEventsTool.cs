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
    private sealed record TopEventsRequest(
        string LogName,
        string? MachineName,
        int MaxEvents,
        bool IncludeMessage,
        int? SessionTimeoutMs);

    private const int DefaultMaxEvents = 5;
    private const int MaxViewTop = 5000;
    private const int MaxMachineNameLength = 260;
    private const int MaxLogNameLength = 260;
    private static readonly SemaphoreSlim RemoteReadGate = new(initialCount: 4, maxCount: 4);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<TopEventsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("log_name", out var logName, out var logNameError)) {
                return ToolRequestBindingResult<TopEventsRequest>.Failure(logNameError);
            }

            logName = logName.Trim();
            if (logName.Length > MaxLogNameLength) {
                return ToolRequestBindingResult<TopEventsRequest>.Failure($"log_name must be <= {MaxLogNameLength} characters.");
            }
            if (ContainsControlCharacters(logName)) {
                return ToolRequestBindingResult<TopEventsRequest>.Failure("log_name must not contain control characters.");
            }

            // Treat whitespace-only values as "not provided" to avoid accidental remote session behavior.
            var machineName = reader.OptionalString("machine_name");
            if (string.IsNullOrWhiteSpace(machineName)) {
                machineName = null;
            }

            if (machineName is not null) {
                if (machineName.Length > MaxMachineNameLength) {
                    return ToolRequestBindingResult<TopEventsRequest>.Failure($"machine_name must be <= {MaxMachineNameLength} characters.");
                }
                if (ContainsControlCharacters(machineName)) {
                    return ToolRequestBindingResult<TopEventsRequest>.Failure("machine_name must not contain control characters.");
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

            if (!TryReadSessionTimeoutRaw(arguments, out var sessionTimeoutRaw, out var timeoutError)) {
                return ToolRequestBindingResult<TopEventsRequest>.Failure(timeoutError ?? "session_timeout_ms is invalid.");
            }

            var sessionTimeoutMs = ResolveSessionTimeoutMs(
                timeoutRaw: sessionTimeoutRaw,
                minInclusive: MinSessionTimeoutMs,
                maxInclusive: MaxSessionTimeoutMs);
            if (machineName is not null && !sessionTimeoutMs.HasValue) {
                sessionTimeoutMs = DefaultRemoteSessionTimeoutMs;
            }

            return ToolRequestBindingResult<TopEventsRequest>.Success(new TopEventsRequest(
                LogName: logName,
                MachineName: machineName,
                MaxEvents: maxEvents,
                IncludeMessage: reader.Boolean("include_message", defaultValue: false),
                SessionTimeoutMs: sessionTimeoutMs));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<TopEventsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var maxMessageChars = request.IncludeMessage
            ? Math.Max(0, Math.Min(Options.MaxMessageChars, 1200))
            : 0;

        var liveRequest = new LiveEventQueryRequest {
            LogName = request.LogName,
            MachineName = request.MachineName,
            XPath = "*",
            MaxEvents = request.MaxEvents,
            OldestFirst = false,
            IncludeMessage = request.IncludeMessage,
            MaxMessageChars = maxMessageChars,
            SessionTimeoutMs = request.SessionTimeoutMs
        };

        LiveEventQueryResult? root;
        LiveEventQueryFailure? failure;
        bool ok;

        if (request.MachineName is null) {
            // Local reads are typically fast and stay on the current thread to reduce overhead.
            ok = LiveEventQueryExecutor.TryRead(
                request: liveRequest,
                result: out root,
                failure: out failure,
                cancellationToken: cancellationToken);
        } else {
            // Live querying is synchronous; for remote sessions, offload work so callers don't tie up request threads.
            // Use a small concurrency gate to avoid saturating the host with concurrent RPC/session work.
            await RemoteReadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            (bool Ok, LiveEventQueryResult? Root, LiveEventQueryFailure? Failure) remote;
            try {
                // Cancellation is best-effort: we honor it while waiting for the concurrency gate and before starting
                // the remote read. The underlying synchronous call may or may not observe it once running.
                cancellationToken.ThrowIfCancellationRequested();

                remote = await Task.Run(() => {
                    var okInner = LiveEventQueryExecutor.TryRead(
                        request: liveRequest,
                        result: out var remoteRoot,
                        failure: out var remoteFailure,
                        cancellationToken: cancellationToken);
                    return (Ok: okInner, Root: okInner ? remoteRoot : null, Failure: okInner ? null : remoteFailure);
                }, CancellationToken.None).ConfigureAwait(false);
            } finally {
                RemoteReadGate.Release();
            }

            ok = remote.Ok;
            root = remote.Root;
            failure = remote.Failure;
        }

        if (!ok) {
            return ErrorFromLiveQueryFailure(
                failure: failure,
                machineName: request.MachineName,
                logName: request.LogName);
        }

        return ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: root!,
            sourceRows: root!.Events,
            viewRowsPath: "events_view",
            title: $"Top {request.MaxEvents} recent events (preview)",
            baseTruncated: root.Truncated,
            scanned: root.Events.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => AddReadOnlyTriageChainingMeta(
                meta: meta,
                currentTool: "eventlog_top_events",
                logName: request.LogName,
                machineName: request.MachineName,
                suggestedMaxEvents: request.MaxEvents,
                scanned: root.Events.Count,
                truncated: root.Truncated,
                queryMode: "top_events"));
    }

    private static bool ContainsControlCharacters(string value) {
        for (var i = 0; i < value.Length; i++) {
            if (char.IsControl(value[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadSessionTimeoutRaw(JsonObject? arguments, out long? timeoutRaw, out string? error) {
        timeoutRaw = null;
        error = null;
        if (arguments is null) {
            return true;
        }

        foreach (var kv in arguments) {
            if (!string.Equals(kv.Key, "session_timeout_ms", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var value = kv.Value;
            // Treat explicit null as "not provided".
            if (value is null || value.Kind == JsonValueKind.Null) {
                return true;
            }

            if (value.Kind != JsonValueKind.Number) {
                error = "session_timeout_ms must be a number of milliseconds.";
                return false;
            }

            // Avoid relying on raw boxed numeric shapes. We only need bounded ms precision here, so prefer
            // the model's safe numeric accessors and truncate fractional milliseconds deterministically.
            var d = value.AsDouble();
            if (!d.HasValue) {
                timeoutRaw = null;
                return true;
            }

            if (!double.IsFinite(d.Value)) {
                error = "session_timeout_ms must be a finite number.";
                return false;
            }

            timeoutRaw = (long)Math.Truncate(d.Value);
            return true;
        }

        return true;
    }
}

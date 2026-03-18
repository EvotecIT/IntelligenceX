using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX.Reports.Inventory;
using EventViewerX.Reports.Live;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Runs a lightweight local or remote Event Log preflight before deeper channel queries.
/// </summary>
public sealed class EventLogConnectivityProbeTool : EventLogToolBase, ITool {
    private sealed record ProbeRequest(
        string? MachineName,
        string? LogName,
        int? SessionTimeoutMs);

    private sealed record ProbeResultModel(
        string Scope,
        string? MachineName,
        string? RequestedLogName,
        string ProbeStatus,
        int? SessionTimeoutMs,
        bool ChannelCatalogAccessible,
        bool LogReadValidated,
        int ChannelSampleCount,
        IReadOnlyList<string> ChannelSample,
        int SampleEventCount,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> RecommendedFollowUpTools);

    private static readonly ToolPipelineReliabilityOptions ReliabilityOptions =
        ToolPipelineReliabilityProfiles.FastNetworkProbeWith(static options => {
            options.CircuitKey = "eventlog_connectivity_probe";
        });

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_connectivity_probe",
        "Run a lightweight EventViewerX preflight to confirm local or remote Event Log access before deeper queries.",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for local machine.")),
                ("log_name", ToolSchema.String("Optional log/channel name to validate with a one-event live read after catalog discovery.")),
                ("session_timeout_ms", ToolSchema.Integer("Optional session timeout in milliseconds for remote preflight probes.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogConnectivityProbeTool"/> class.
    /// </summary>
    public EventLogConnectivityProbeTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync,
            reliability: ReliabilityOptions).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<ProbeRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var machineName = reader.OptionalString("machine_name");
            var logName = reader.OptionalString("log_name");
            var timeout = ResolveSessionTimeoutMs(arguments, minInclusive: 1_000, maxInclusive: 120_000);
            if (machineName is not null && !timeout.HasValue) {
                timeout = DefaultRemoteSessionTimeoutMs;
            }

            return ToolRequestBindingResult<ProbeRequest>.Success(new ProbeRequest(
                MachineName: machineName,
                LogName: logName,
                SessionTimeoutMs: timeout));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ProbeRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var scope = string.IsNullOrWhiteSpace(request.MachineName) ? "local" : "remote";
        var warnings = new List<string>();

        var catalogRequest = new EventCatalogQueryRequest {
            MachineName = request.MachineName,
            MaxResults = 10,
            SessionTimeoutMs = request.SessionTimeoutMs
        };

        var catalogOk = EventCatalogQueryExecutor.TryListChannels(
            request: catalogRequest,
            result: out var channelsRoot,
            failure: out var channelsFailure,
            cancellationToken: cancellationToken);
        if (!catalogOk && channelsFailure is not null) {
            warnings.Add("channel_catalog_probe: " + channelsFailure.Message);
        }

        var normalizedLogName = string.IsNullOrWhiteSpace(request.LogName) ? null : request.LogName.Trim();
        var liveReadValidated = false;
        var sampleEventCount = 0;
        LiveEventQueryFailure? liveFailure = null;
        if (normalizedLogName is not null) {
            var liveRequest = new LiveEventQueryRequest {
                LogName = normalizedLogName,
                MachineName = request.MachineName,
                XPath = "*",
                MaxEvents = 1,
                OldestFirst = false,
                IncludeMessage = false,
                MaxMessageChars = 0,
                SessionTimeoutMs = request.SessionTimeoutMs
            };

            liveReadValidated = LiveEventQueryExecutor.TryRead(
                request: liveRequest,
                result: out var liveRoot,
                failure: out liveFailure,
                cancellationToken: cancellationToken);
            if (liveReadValidated && liveRoot is not null) {
                sampleEventCount = liveRoot.Events.Count;
            } else if (liveFailure is not null) {
                warnings.Add("log_read_probe: " + liveFailure.Message);
            }
        }

        if (!catalogOk && !liveReadValidated) {
            return Task.FromResult(
                normalizedLogName is not null && liveFailure is not null
                    ? ErrorFromLiveQueryFailure(liveFailure, request.MachineName, normalizedLogName)
                    : ErrorFromCatalogFailure(channelsFailure, request.MachineName, "event log connectivity probe"));
        }

        var channelSample = channelsRoot?.Channels
            ?.Select(static row => row.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Take(5)
            .ToArray() ?? Array.Empty<string>();
        var probeStatus = warnings.Count == 0 ? "healthy" : "degraded";
        var recommendedFollowUpTools = normalizedLogName is not null
            ? new[] { "eventlog_top_events", "eventlog_live_query", "eventlog_timeline_query" }
            : new[] { "eventlog_channels_list", "eventlog_top_events", "eventlog_live_query" };
        var result = new ProbeResultModel(
            Scope: scope,
            MachineName: request.MachineName,
            RequestedLogName: normalizedLogName,
            ProbeStatus: probeStatus,
            SessionTimeoutMs: request.SessionTimeoutMs,
            ChannelCatalogAccessible: catalogOk,
            LogReadValidated: liveReadValidated,
            ChannelSampleCount: channelSample.Length,
            ChannelSample: channelSample,
            SampleEventCount: sampleEventCount,
            Warnings: warnings,
            RecommendedFollowUpTools: recommendedFollowUpTools);

        var facts = new List<(string Key, string Value)> {
            ("Scope", scope),
            ("Machine", string.IsNullOrWhiteSpace(request.MachineName) ? Environment.MachineName : request.MachineName!),
            ("Probe status", probeStatus),
            ("Channel catalog", catalogOk ? "ok" : "failed"),
            ("Validated log read", normalizedLogName is null ? "skipped" : (liveReadValidated ? "ok" : "failed"))
        };
        if (normalizedLogName is not null) {
            facts.Add(("Requested log", normalizedLogName));
            facts.Add(("Sample events", sampleEventCount.ToString(CultureInfo.InvariantCulture)));
        }
        if (request.SessionTimeoutMs.HasValue) {
            facts.Add(("Session timeout (ms)", request.SessionTimeoutMs.Value.ToString(CultureInfo.InvariantCulture)));
        }
        if (channelSample.Length > 0) {
            facts.Add(("Channel sample", string.Join(", ", channelSample)));
        }

        var meta = ToolOutputHints.Meta(count: Math.Max(channelSample.Length, sampleEventCount), truncated: false)
            .Add("scope", scope)
            .Add("machine_name", request.MachineName ?? string.Empty)
            .Add("requested_log_name", normalizedLogName ?? string.Empty)
            .Add("probe_status", probeStatus)
            .Add("channel_catalog_accessible", catalogOk)
            .Add("log_read_validated", liveReadValidated)
            .Add("channel_sample_count", channelSample.Length)
            .Add("sample_event_count", sampleEventCount)
            .Add("recommended_follow_up_tools", ToolJson.ToJsonArray(recommendedFollowUpTools));
        if (request.SessionTimeoutMs.HasValue) {
            meta.Add("session_timeout_ms", request.SessionTimeoutMs.Value);
        }

        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: result,
            title: "Event Log connectivity probe",
            facts: facts,
            meta: meta));
    }
}

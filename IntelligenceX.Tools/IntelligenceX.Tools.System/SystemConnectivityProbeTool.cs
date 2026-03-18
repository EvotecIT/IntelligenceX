using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.ComputerSystem;
using ComputerX.OperatingSystem;
using ComputerX.Time;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Runs a lightweight local or remote ComputerX preflight before deeper host diagnostics.
/// </summary>
public sealed class SystemConnectivityProbeTool : SystemToolBase, ITool {
    private sealed record ProbeRequest(
        string? ComputerName,
        string Target,
        int TimeoutMs,
        bool IncludeTimeSync);

    private sealed record ProbeResultModel(
        string Target,
        string Scope,
        string ProbeStatus,
        int TimeoutMs,
        bool IncludeTimeSync,
        bool OperatingSystemProbeSucceeded,
        bool ComputerSystemProbeSucceeded,
        bool TimeSyncProbeSucceeded,
        OsInfo? OperatingSystem,
        ComputerSystemInfo? ComputerSystem,
        TimeSyncInfo? TimeSync,
        IReadOnlyList<string> Warnings,
        IReadOnlyList<string> RecommendedFollowUpTools);

    private static readonly ToolPipelineReliabilityOptions ReliabilityOptions =
        ToolPipelineReliabilityProfiles.FastNetworkProbeWith(static options => {
            options.CircuitKey = "system_connectivity_probe";
        });

    private static readonly ToolDefinition DefinitionValue = new(
        "system_connectivity_probe",
        "Run a lightweight ComputerX preflight to confirm local or remote host reachability before deeper diagnostics.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("timeout_ms", ToolSchema.Integer("Optional probe timeout in milliseconds (capped). Default 8000.")),
                ("include_time_sync", ToolSchema.Boolean("When true, include a lightweight time-sync preflight in addition to OS/system discovery.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemConnectivityProbeTool"/> class.
    /// </summary>
    public SystemConnectivityProbeTool(SystemToolOptions options) : base(options) { }

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
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<ProbeRequest>.Success(new ProbeRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                TimeoutMs: ResolveTimeoutMs(arguments, defaultValue: 8_000, minInclusive: 500, maxInclusive: 30_000),
                IncludeTimeSync: reader.Boolean("include_time_sync", defaultValue: false)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<ProbeRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var windowsError = ValidateWindowsSupport("system_connectivity_probe");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        var scope = IsLocalTarget(request.ComputerName, request.Target) ? "local" : "remote";
        var warnings = new List<string>();

        OsInfo? osInfo = null;
        try {
            osInfo = await OsInfoQuery
                .GetAsync(request.Target, TimeSpan.FromMilliseconds(request.TimeoutMs), cancellationToken)
                .ConfigureAwait(false);
        } catch (Exception ex) {
            warnings.Add("os_probe: " + ex.Message);
        }

        ComputerSystemInfo? computerSystem = null;
        try {
            computerSystem = await ComputerSystemInfoQuery
                .GetAsync(request.Target, TimeSpan.FromMilliseconds(request.TimeoutMs), cancellationToken)
                .ConfigureAwait(false);
        } catch (Exception ex) {
            warnings.Add("computer_system_probe: " + ex.Message);
        }

        TimeSyncInfo? timeSync = null;
        if (request.IncludeTimeSync) {
            try {
                timeSync = string.Equals(scope, "local", StringComparison.OrdinalIgnoreCase)
                    ? TimeSync.GetLocalStatus(DateTime.UtcNow)
                    : await TimeSync.QueryRemoteStatusAsync(request.Target, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                warnings.Add("time_sync_probe: " + ex.Message);
            }
        }

        var successCount = 0;
        if (osInfo is not null) {
            successCount++;
        }
        if (computerSystem is not null) {
            successCount++;
        }
        if (timeSync is not null) {
            successCount++;
        }

        if (successCount == 0) {
            return ToolResultV2.Error(
                errorCode: "probe_failed",
                error: $"System connectivity probe failed for {scope} target '{request.Target}'.",
                hints: BuildFailureHints(scope, request.Target),
                isTransient: true);
        }

        var probeStatus = warnings.Count == 0 ? "healthy" : "degraded";
        var followUpTools = request.IncludeTimeSync
            ? new[] { "system_info", "system_metrics_summary", "system_time_sync" }
            : new[] { "system_info", "system_metrics_summary", "system_logical_disks_list" };
        var result = new ProbeResultModel(
            Target: request.Target,
            Scope: scope,
            ProbeStatus: probeStatus,
            TimeoutMs: request.TimeoutMs,
            IncludeTimeSync: request.IncludeTimeSync,
            OperatingSystemProbeSucceeded: osInfo is not null,
            ComputerSystemProbeSucceeded: computerSystem is not null,
            TimeSyncProbeSucceeded: timeSync is not null,
            OperatingSystem: osInfo,
            ComputerSystem: computerSystem,
            TimeSync: timeSync,
            Warnings: warnings,
            RecommendedFollowUpTools: followUpTools);

        var facts = new List<(string Key, string Value)> {
            ("Target", request.Target),
            ("Scope", scope),
            ("Probe status", probeStatus),
            ("Timeout (ms)", request.TimeoutMs.ToString(CultureInfo.InvariantCulture)),
            ("OS probe", osInfo is null ? "failed" : "ok"),
            ("Computer system probe", computerSystem is null ? "failed" : "ok")
        };
        if (osInfo is not null && !string.IsNullOrWhiteSpace(osInfo.Caption)) {
            facts.Add(("OS", osInfo.Caption!));
        }
        if (computerSystem is not null && !string.IsNullOrWhiteSpace(computerSystem.Domain)) {
            facts.Add(("Domain", computerSystem.Domain!));
        } else if (computerSystem is not null && !string.IsNullOrWhiteSpace(computerSystem.Workgroup)) {
            facts.Add(("Workgroup", computerSystem.Workgroup!));
        }
        if (request.IncludeTimeSync) {
            facts.Add(("Time sync probe", timeSync is null ? "failed" : "ok"));
            if (timeSync is not null) {
                facts.Add(("Time skew (seconds)", timeSync.TimeSkewSeconds.ToString("0.###", CultureInfo.InvariantCulture)));
            }
        }

        var meta = BuildFactsMeta(count: 1, truncated: false, target: request.Target, mutate: probeMeta => {
            probeMeta.Add("probe_status", probeStatus);
            probeMeta.Add("timeout_ms", request.TimeoutMs);
            probeMeta.Add("scope", scope);
            probeMeta.Add("warning_count", warnings.Count);
            probeMeta.Add("operating_system_probe_succeeded", osInfo is not null);
            probeMeta.Add("computer_system_probe_succeeded", computerSystem is not null);
            probeMeta.Add("time_sync_probe_succeeded", timeSync is not null);
            probeMeta.Add("recommended_follow_up_tools", ToolJson.ToJsonArray(result.RecommendedFollowUpTools));
        });

        return ToolResultV2.OkFactsModel(
            model: result,
            title: "System connectivity probe",
            facts: facts,
            meta: meta);
    }

    private static IReadOnlyList<string> BuildFailureHints(string scope, string target) {
        var hints = new List<string> {
            string.Equals(scope, "remote", StringComparison.OrdinalIgnoreCase)
                ? $"Verify computer_name '{target}' resolves and is reachable from this host."
                : "Verify the local runtime can query WMI/CIM-backed operating system details.",
            "Retry with a higher timeout_ms if the target is slow to respond.",
            "Use system_info after the probe succeeds to collect fuller host context."
        };

        if (string.Equals(scope, "remote", StringComparison.OrdinalIgnoreCase)) {
            hints.Add("Ensure the runtime identity has permission for remote ComputerX/WMI/CIM access on the target host.");
        }

        return hints;
    }
}

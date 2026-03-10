using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Updates;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns flattened Windows Update telemetry and freshness evaluation for the local or remote Windows host.
/// </summary>
public sealed class SystemWindowsUpdateTelemetryTool : SystemToolBase, ITool {
    private sealed record WindowsUpdateTelemetryRequest(
        string? ComputerName,
        string Target,
        bool IncludeEventTelemetry,
        int EventLookbackDays,
        int QueryTimeoutSeconds,
        int DetectStaleWarningAfterHours,
        int DetectStaleDownAfterHours);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_windows_update_telemetry",
        "Return flattened Windows Update telemetry and freshness evaluation for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_event_telemetry", ToolSchema.Boolean("When true, include event-log-based update telemetry signals. Default true.")),
                ("event_lookback_days", ToolSchema.Integer("Optional event lookback window in days. Default 14.")),
                ("query_timeout_seconds", ToolSchema.Integer("Optional per-query event access timeout in seconds. Default 8.")),
                ("detect_stale_warning_after_hours", ToolSchema.Integer("Optional freshness threshold for warning state in hours. Default 48.")),
                ("detect_stale_down_after_hours", ToolSchema.Integer("Optional freshness threshold for critical/down state in hours. Default 168.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:windows_update_telemetry", "intent:wsus_telemetry", "scope:host_windows_update" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemWindowsUpdateTelemetryTool"/> class.
    /// </summary>
    public SystemWindowsUpdateTelemetryTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<WindowsUpdateTelemetryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<WindowsUpdateTelemetryRequest>.Success(new WindowsUpdateTelemetryRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeEventTelemetry: reader.Boolean("include_event_telemetry", defaultValue: true),
                EventLookbackDays: ToolArgs.GetCappedInt32(arguments, "event_lookback_days", 14, 0, 90),
                QueryTimeoutSeconds: ToolArgs.GetCappedInt32(arguments, "query_timeout_seconds", 8, 1, 60),
                DetectStaleWarningAfterHours: ToolArgs.GetCappedInt32(arguments, "detect_stale_warning_after_hours", 48, 0, 24 * 180),
                DetectStaleDownAfterHours: ToolArgs.GetCappedInt32(arguments, "detect_stale_down_after_hours", 168, 0, 24 * 365)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<WindowsUpdateTelemetryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_windows_update_telemetry");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var telemetry = WindowsUpdateTelemetryQuery.Get(
                request.ComputerName,
                new WindowsUpdateTelemetryQueryOptions {
                    IncludeEventTelemetry = request.IncludeEventTelemetry,
                    EventLookback = TimeSpan.FromDays(request.EventLookbackDays),
                    QueryTimeout = TimeSpan.FromSeconds(request.QueryTimeoutSeconds),
                    DetectStaleWarningAfter = TimeSpan.FromHours(request.DetectStaleWarningAfterHours),
                    DetectStaleDownAfter = TimeSpan.FromHours(request.DetectStaleDownAfterHours)
                });

            var effectiveComputerName = string.IsNullOrWhiteSpace(telemetry.ComputerName) ? request.Target : telemetry.ComputerName;
            var meta = BuildFactsMeta(
                count: 1,
                truncated: false,
                target: effectiveComputerName,
                mutate: x => {
                    x.Add("include_event_telemetry", request.IncludeEventTelemetry);
                    x.Add("event_lookback_days", request.EventLookbackDays);
                    x.Add("query_timeout_seconds", request.QueryTimeoutSeconds);
                    x.Add("detect_stale_warning_after_hours", request.DetectStaleWarningAfterHours);
                    x.Add("detect_stale_down_after_hours", request.DetectStaleDownAfterHours);
                    x.Add("wsus_decision", telemetry.WsusDecision.ToString());
                });
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_windows_update_telemetry",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: telemetry,
                title: "Windows Update telemetry",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("WSUS Decision", telemetry.WsusDecision.ToString()),
                    ("Detection Missing", telemetry.DetectionMissing ? "true" : "false"),
                    ("Detection Stale Warning", telemetry.DetectionStaleWarning ? "true" : "false"),
                    ("Detection Stale Down", telemetry.DetectionStaleDown ? "true" : "false"),
                    ("Pending Reboot", telemetry.IsPendingReboot ? "true" : "false"),
                    ("Update Reboot Pending", telemetry.UpdateRebootPending ? "true" : "false"),
                    ("Effective Detection Utc", telemetry.EffectiveDetectionUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Effective Detection Age Hours", telemetry.EffectiveDetectionAgeHours?.ToString("0.##", CultureInfo.InvariantCulture) ?? "unknown")
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Windows Update telemetry query failed."));
        }
    }
}

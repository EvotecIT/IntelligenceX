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
/// Returns low-privilege Windows Update and WSUS client status for the local or remote Windows host.
/// </summary>
public sealed class SystemWindowsUpdateClientStatusTool : SystemToolBase, ITool {
    private sealed record WindowsUpdateClientStatusRequest(
        string? ComputerName,
        string Target,
        bool IncludeEventTelemetry,
        int EventLookbackDays,
        int QueryTimeoutSeconds);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_windows_update_client_status",
        "Return low-privilege Windows Update and WSUS client status for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_event_telemetry", ToolSchema.Boolean("When true, include event-log-based update telemetry signals. Default true.")),
                ("event_lookback_days", ToolSchema.Integer("Optional event lookback window in days. Default 14.")),
                ("query_timeout_seconds", ToolSchema.Integer("Optional per-query event access timeout in seconds. Default 8.")))
            .NoAdditionalProperties(),
        tags: new[] { "pack:system", "intent:windows_update_status", "intent:wsus_status", "scope:host_windows_update" });

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemWindowsUpdateClientStatusTool"/> class.
    /// </summary>
    public SystemWindowsUpdateClientStatusTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<WindowsUpdateClientStatusRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<WindowsUpdateClientStatusRequest>.Success(new WindowsUpdateClientStatusRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeEventTelemetry: reader.Boolean("include_event_telemetry", defaultValue: true),
                EventLookbackDays: ToolArgs.GetCappedInt32(arguments, "event_lookback_days", 14, 0, 90),
                QueryTimeoutSeconds: ToolArgs.GetCappedInt32(arguments, "query_timeout_seconds", 8, 1, 60)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<WindowsUpdateClientStatusRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_windows_update_client_status");
        if (windowsError is not null) {
            return Task.FromResult(windowsError);
        }

        var request = context.Request;
        try {
            var status = WindowsUpdateClientStatusQuery.Get(
                request.ComputerName,
                new WindowsUpdateClientStatusQueryOptions {
                    IncludeEventTelemetry = request.IncludeEventTelemetry,
                    EventLookback = TimeSpan.FromDays(request.EventLookbackDays),
                    QueryTimeout = TimeSpan.FromSeconds(request.QueryTimeoutSeconds)
                });

            var effectiveComputerName = string.IsNullOrWhiteSpace(status.ComputerName) ? request.Target : status.ComputerName;
            var meta = BuildFactsMeta(
                count: 1,
                truncated: false,
                target: effectiveComputerName,
                mutate: x => {
                    x.Add("include_event_telemetry", request.IncludeEventTelemetry);
                    x.Add("event_lookback_days", request.EventLookbackDays);
                    x.Add("query_timeout_seconds", request.QueryTimeoutSeconds);
                    x.Add("wsus_decision", status.WsusDecision.ToString());
                });
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_windows_update_client_status",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: status,
                title: "Windows Update client status",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("WSUS Decision", status.WsusDecision.ToString()),
                    ("WSUS Managed", FormatNullableBool(status.IsWsusManaged)),
                    ("Pending Reboot", status.IsPendingReboot ? "true" : "false"),
                    ("Registry Access Failed", status.RegistryAccessFailed ? "true" : "false"),
                    ("Event Telemetry Access Failed", status.EventTelemetryAccessFailed ? "true" : "false"),
                    ("Last Detection Success Utc", status.LastDetectionSuccessUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Last Install Success Utc", status.LastInstallSuccessUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unknown"),
                    ("Warnings", status.Warnings.Count.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null));
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(ex, defaultMessage: "Windows Update client status query failed."));
        }
    }

    private static string FormatNullableBool(bool? value) {
        return value.HasValue ? (value.Value ? "true" : "false") : "unknown";
    }
}

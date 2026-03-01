using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Time;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns local or remote time synchronization status (read-only).
/// </summary>
public sealed class SystemTimeSyncTool : SystemToolBase, ITool {
    private sealed record TimeSyncRequest(
        string? ComputerName,
        string Target,
        DateTime ReferenceUtc);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_time_sync",
        "Return time synchronization status (w32time running and skew seconds) for local or remote host (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("reference_time_utc", ToolSchema.String("Optional UTC reference timestamp (RFC3339/ISO-8601). Defaults to current UTC.")))
            .NoAdditionalProperties());

    private sealed record SystemTimeSyncResult(
        string ComputerName,
        string ReferenceTimeUtc,
        TimeSyncInfo Status);

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemTimeSyncTool"/> class.
    /// </summary>
    public SystemTimeSyncTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync).ConfigureAwait(false);
    }

    private ToolRequestBindingResult<TimeSyncRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var target = ResolveTargetComputerName(computerName);
            var referenceRaw = reader.OptionalString("reference_time_utc");

            DateTime referenceUtc;
            if (string.IsNullOrWhiteSpace(referenceRaw)) {
                referenceUtc = DateTime.UtcNow;
            } else if (!DateTime.TryParse(referenceRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) {
                return ToolRequestBindingResult<TimeSyncRequest>.Failure("reference_time_utc must be a valid ISO-8601 datetime.");
            } else {
                referenceUtc = parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
            }

            return ToolRequestBindingResult<TimeSyncRequest>.Success(new TimeSyncRequest(
                ComputerName: computerName,
                Target: target,
                ReferenceUtc: referenceUtc));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<TimeSyncRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var windowsError = ValidateWindowsSupport("system_time_sync");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;

        TimeSyncInfo status;
        try {
            var isLocalTarget = IsLocalTarget(request.ComputerName, request.Target);
            if (isLocalTarget) {
                status = TimeSync.GetLocalStatus(request.ReferenceUtc);
            } else {
                status = await TimeSync.QueryRemoteStatusAsync(request.Target, request.ReferenceUtc, cancellationToken).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            return ErrorFromException(ex, defaultMessage: "Time sync query failed.");
        }

        var model = new SystemTimeSyncResult(
            ComputerName: request.Target,
            ReferenceTimeUtc: request.ReferenceUtc.ToString("O"),
            Status: status);

        return ToolResultV2.OkFactsModel(
            model: model,
            title: "System time sync",
            facts: new[] {
                ("Computer", request.Target),
                ("ReferenceTimeUtc", request.ReferenceUtc.ToString("O")),
                ("W32TimeRunning", status.IsW32TimeRunning.ToString()),
                ("TimeSkewSeconds", status.TimeSkewSeconds.ToString("0.###", CultureInfo.InvariantCulture))
            },
            meta: null,
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null);
    }
}

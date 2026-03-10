using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns a read-only CPU and memory telemetry snapshot for the local or remote host.
/// </summary>
public sealed class SystemMetricsSummaryTool : SystemToolBase, ITool {
    private sealed record SystemMetricsSummaryRequest(
        string? ComputerName,
        string Target);

    private sealed record SystemMetricsSummaryResult(
        string ComputerName,
        long TotalPhysicalMemoryBytes,
        long FreePhysicalMemoryBytes,
        double MemoryUsagePercent,
        double CpuLoadPercent);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_metrics_summary",
        "Return a read-only CPU and memory telemetry snapshot for the local or remote host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemMetricsSummaryTool"/> class.
    /// </summary>
    public SystemMetricsSummaryTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<SystemMetricsSummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<SystemMetricsSummaryRequest>.Success(new SystemMetricsSummaryRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<SystemMetricsSummaryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_metrics_summary");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        try {
            var metrics = IsLocalTarget(request.ComputerName, request.Target)
                ? SystemMetrics.GetLocalUsage()
                : await SystemMetrics.QueryRemoteAsync(request.Target, cancellationToken).ConfigureAwait(false);

            var effectiveComputerName = string.IsNullOrWhiteSpace(metrics.ComputerName)
                ? request.Target
                : metrics.ComputerName;
            var model = new SystemMetricsSummaryResult(
                ComputerName: effectiveComputerName,
                TotalPhysicalMemoryBytes: metrics.TotalPhysicalMemory,
                FreePhysicalMemoryBytes: metrics.FreePhysicalMemory,
                MemoryUsagePercent: metrics.MemoryUsagePercentage,
                CpuLoadPercent: metrics.CpuLoadPercentage);

            var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
            AddReadOnlyPostureChainingMeta(
                meta: meta,
                currentTool: "system_metrics_summary",
                targetComputer: effectiveComputerName,
                isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                scanned: 1,
                truncated: false);

            return ToolResultV2.OkFactsModel(
                model: model,
                title: "System metrics summary",
                facts: new[] {
                    ("Computer", effectiveComputerName),
                    ("CPU Load (%)", metrics.CpuLoadPercentage.ToString("F2", CultureInfo.InvariantCulture)),
                    ("Memory Usage (%)", metrics.MemoryUsagePercentage.ToString("F2", CultureInfo.InvariantCulture)),
                    ("Total Physical Memory (bytes)", metrics.TotalPhysicalMemory.ToString(CultureInfo.InvariantCulture)),
                    ("Free Physical Memory (bytes)", metrics.FreePhysicalMemory.ToString(CultureInfo.InvariantCulture))
                },
                meta: meta,
                keyHeader: "Metric",
                valueHeader: "Value",
                truncated: false,
                render: null);
        } catch (global::System.Exception ex) {
            return ErrorFromException(ex, defaultMessage: "System metrics query failed.");
        }
    }
}

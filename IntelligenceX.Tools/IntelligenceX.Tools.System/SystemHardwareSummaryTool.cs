using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Hardware;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns a summarized hardware snapshot (CPU, memory, GPU).
/// </summary>
public sealed class SystemHardwareSummaryTool : SystemToolBase, ITool {
    private sealed record HardwareSummaryRequest(
        string? ComputerName,
        string Target,
        bool IncludeProcessors,
        bool IncludeMemoryModules,
        bool IncludeVideoControllers,
        int? NameSampleSize,
        int TimeoutMs);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_hardware_summary",
        "Return a summarized hardware snapshot (CPU, memory, GPU).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_processors", ToolSchema.Boolean("Include CPU summary fields. Default true.")),
                ("include_memory_modules", ToolSchema.Boolean("Include memory summary fields. Default true.")),
                ("include_video_controllers", ToolSchema.Boolean("Include GPU summary fields. Default true.")),
                ("name_sample_size", ToolSchema.Integer("Maximum processor/GPU name samples to include (capped).")),
                ("timeout_ms", ToolSchema.Integer("Optional query timeout in milliseconds (capped). Default 10000.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemHardwareSummaryTool"/> class.
    /// </summary>
    public SystemHardwareSummaryTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<HardwareSummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var includeProcessors = reader.Boolean("include_processors", defaultValue: true);
            var includeMemoryModules = reader.Boolean("include_memory_modules", defaultValue: true);
            var includeVideoControllers = reader.Boolean("include_video_controllers", defaultValue: true);
            if (!includeProcessors && !includeMemoryModules && !includeVideoControllers) {
                return ToolRequestBindingResult<HardwareSummaryRequest>.Failure(
                    "At least one include_* section must be true.");
            }

            var nameSampleArg = reader.OptionalInt64("name_sample_size");
            int? nameSampleSize = null;
            if (nameSampleArg.HasValue) {
                if (nameSampleArg.Value <= 0) {
                    return ToolRequestBindingResult<HardwareSummaryRequest>.Failure(
                        "name_sample_size must be greater than zero.");
                }

                nameSampleSize = (int)Math.Min(nameSampleArg.Value, 50);
            }

            return ToolRequestBindingResult<HardwareSummaryRequest>.Success(new HardwareSummaryRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeProcessors: includeProcessors,
                IncludeMemoryModules: includeMemoryModules,
                IncludeVideoControllers: includeVideoControllers,
                NameSampleSize: nameSampleSize,
                TimeoutMs: ResolveTimeoutMs(arguments)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<HardwareSummaryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        if (!HardwareSummaryQueryExecutor.TryExecute(
                request: new HardwareSummaryQueryRequest {
                    ComputerName = request.ComputerName,
                    IncludeProcessors = request.IncludeProcessors,
                    IncludeMemoryModules = request.IncludeMemoryModules,
                    IncludeVideoControllers = request.IncludeVideoControllers,
                    NameSampleSize = request.NameSampleSize,
                    Timeout = TimeSpan.FromMilliseconds(request.TimeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Hardware summary query failed."));
        }

        var result = queryResult!;
        var facts = new List<(string Key, string Value)>();
        var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
        facts.Add(("Computer", effectiveComputerName));
        if (request.IncludeProcessors) {
            facts.Add(("Processor Count", result.ProcessorCount.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Total CPU Cores", result.TotalCpuCores.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Total Logical Processors", result.TotalLogicalProcessors.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Max CPU Clock (MHz)", result.MaxCpuClockMhz.ToString(CultureInfo.InvariantCulture)));
        }
        if (request.IncludeMemoryModules) {
            facts.Add(("Memory Module Count", result.MemoryModuleCount.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Total Memory (bytes)", result.TotalMemoryBytes.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Max Memory Speed (MHz)", result.MaxMemorySpeedMhz.ToString(CultureInfo.InvariantCulture)));
        }
        if (request.IncludeVideoControllers) {
            facts.Add(("Video Controller Count", result.VideoControllerCount.ToString(CultureInfo.InvariantCulture)));
        }

        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: result,
            title: "Hardware summary",
            facts: facts,
            meta: BuildFactsMeta(
                count: 1,
                truncated: false,
                target: effectiveComputerName,
                mutate: meta => meta
                    .Add("timeout_ms", request.TimeoutMs)
                    .Add("include_processors", request.IncludeProcessors)
                    .Add("include_memory_modules", request.IncludeMemoryModules)
                    .Add("include_video_controllers", request.IncludeVideoControllers)),
            keyHeader: "Metric",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

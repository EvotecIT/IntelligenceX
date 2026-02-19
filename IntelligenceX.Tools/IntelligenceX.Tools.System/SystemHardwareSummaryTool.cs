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
    private static readonly ToolDefinition DefinitionValue = new(
        "system_hardware_summary",
        "Return a summarized hardware snapshot (CPU, memory, GPU).",
        ToolSchema.Object(
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
        cancellationToken.ThrowIfCancellationRequested();

        var includeProcessors = arguments?.GetBoolean("include_processors", defaultValue: true) ?? true;
        var includeMemoryModules = arguments?.GetBoolean("include_memory_modules", defaultValue: true) ?? true;
        var includeVideoControllers = arguments?.GetBoolean("include_video_controllers", defaultValue: true) ?? true;
        if (!includeProcessors && !includeMemoryModules && !includeVideoControllers) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "At least one include_* section must be true."));
        }

        var timeoutMs = ResolveTimeoutMs(arguments);
        var nameSampleArg = arguments?.GetInt64("name_sample_size");
        int? nameSampleSize = null;
        if (nameSampleArg.HasValue) {
            if (nameSampleArg.Value <= 0) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "name_sample_size must be greater than zero."));
            }
            nameSampleSize = (int)Math.Min(nameSampleArg.Value, 50);
        }

        if (!HardwareSummaryQueryExecutor.TryExecute(
                request: new HardwareSummaryQueryRequest {
                    IncludeProcessors = includeProcessors,
                    IncludeMemoryModules = includeMemoryModules,
                    IncludeVideoControllers = includeVideoControllers,
                    NameSampleSize = nameSampleSize,
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Hardware summary query failed."));
        }

        var result = queryResult!;
        var facts = new List<(string Key, string Value)>();
        if (includeProcessors) {
            facts.Add(("Processor Count", result.ProcessorCount.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Total CPU Cores", result.TotalCpuCores.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Total Logical Processors", result.TotalLogicalProcessors.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Max CPU Clock (MHz)", result.MaxCpuClockMhz.ToString(CultureInfo.InvariantCulture)));
        }
        if (includeMemoryModules) {
            facts.Add(("Memory Module Count", result.MemoryModuleCount.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Total Memory (bytes)", result.TotalMemoryBytes.ToString(CultureInfo.InvariantCulture)));
            facts.Add(("Max Memory Speed (MHz)", result.MaxMemorySpeedMhz.ToString(CultureInfo.InvariantCulture)));
        }
        if (includeVideoControllers) {
            facts.Add(("Video Controller Count", result.VideoControllerCount.ToString(CultureInfo.InvariantCulture)));
        }

        return Task.FromResult(ToolResponse.OkFactsModel(
            model: result,
            title: "Hardware summary",
            facts: facts,
            meta: ToolOutputHints.Meta(count: facts.Count, truncated: false)
                .Add("timeout_ms", timeoutMs)
                .Add("include_processors", includeProcessors)
                .Add("include_memory_modules", includeMemoryModules)
                .Add("include_video_controllers", includeVideoControllers),
            keyHeader: "Metric",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

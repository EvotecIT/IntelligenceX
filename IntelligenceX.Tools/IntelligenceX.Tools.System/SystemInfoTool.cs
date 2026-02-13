using System;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Runtime;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns basic OS/runtime information.
/// </summary>
public sealed class SystemInfoTool : SystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "system_info",
        "Return basic OS/runtime information (read-only).",
        ToolSchema.Object()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfoTool"/> class.
    /// </summary>
    public SystemInfoTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var attempt = SystemRuntimeQueryExecutor.TryExecute(
            request: new SystemRuntimeQueryRequest {
                IncludeOperatingSystemSummary = true,
                IncludeOperatingSystemDetail = true,
                IncludeComputerSystem = true
            },
            cancellationToken: cancellationToken);
        if (!attempt.Success) {
            return Task.FromResult(ErrorFromFailure(attempt.Failure, static x => x.Code, static x => x.Message, defaultMessage: "System runtime query failed."));
        }

        var result = attempt.Result!;
        var runtime = result.Runtime;

        var facts = new[] {
            ("Machine", runtime.MachineName),
            ("OS", runtime.OsDescription),
            ("Framework", runtime.FrameworkDescription)
        };
        return Task.FromResult(ToolResponse.OkFactsModel(
            model: result,
            title: "System Info",
            facts: facts,
            meta: null,
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}


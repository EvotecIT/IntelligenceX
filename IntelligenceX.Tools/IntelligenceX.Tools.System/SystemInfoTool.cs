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
    private sealed record SystemInfoRequest(
        string? ComputerName,
        string Target);

    private static readonly ToolDefinition DefinitionValue = new(
        "system_info",
        "Return basic OS/runtime information (read-only).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemInfoTool"/> class.
    /// </summary>
    public SystemInfoTool(SystemToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<SystemInfoRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, static reader => {
            var computerName = reader.OptionalString("computer_name");
            return ToolRequestBindingResult<SystemInfoRequest>.Success(new SystemInfoRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<SystemInfoRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var attempt = SystemRuntimeQueryExecutor.TryExecute(
            request: new SystemRuntimeQueryRequest {
                ComputerName = request.ComputerName,
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
        var effectiveComputerName = string.IsNullOrWhiteSpace(runtime.MachineName) ? request.Target : runtime.MachineName;

        var facts = new[] {
            ("Machine", effectiveComputerName),
            ("OS", runtime.OsDescription),
            ("Framework", runtime.FrameworkDescription)
        };
        var meta = BuildFactsMeta(count: 1, truncated: false, target: effectiveComputerName);
        AddReadOnlyPostureChainingMeta(
            meta: meta,
            currentTool: "system_info",
            targetComputer: effectiveComputerName,
            isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
            scanned: 1,
            truncated: false);
        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: result,
            title: "System Info",
            facts: facts,
            meta: meta,
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

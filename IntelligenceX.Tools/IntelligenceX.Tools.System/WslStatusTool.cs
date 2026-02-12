using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Wsl;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Tool that reports Windows Subsystem for Linux (WSL) distribution status.
/// </summary>
public sealed class WslStatusTool : SystemToolBase, ITool {
    private const int MaxViewTop = 500;

    private static readonly ToolDefinition DefinitionValue = new(
        "wsl_status",
        "Report Windows Subsystem for Linux (WSL) distribution status.",
        ToolSchema.Object(
                ("name", ToolSchema.String("Optional distribution name.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="WslStatusTool"/> class.
    /// </summary>
    public WslStatusTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var nameFilter = arguments?.GetString("name");

        var attempt = WslStatusQueryExecutor.TryExecute(
            request: new WslStatusQueryRequest {
                NameFilter = nameFilter,
                TimeoutMs = WslStatusQuery.DefaultTimeoutMs
            },
            cancellationToken: cancellationToken);
        if (!attempt.Success) {
            return Task.FromResult(ErrorFromWslFailure(attempt.Failure));
        }
        var result = attempt.Result?.Status ?? new WslStatusInfo();

        if (string.IsNullOrWhiteSpace(result.RawOutput) && result.Distributions.Count == 0) {
            return Task.FromResult(ToolResponse.Error("process_error", "WSL returned no output."));
        }

        if (result.Distributions.Count == 0) {
            var summaryRaw = ToolMarkdown.SummaryText(
                title: "WSL status",
                "No distributions were parsed. Raw output returned.");
            return Task.FromResult(ToolResponse.OkModel(
                model: result,
                meta: ToolOutputHints.Meta(count: 0, truncated: false),
                summaryMarkdown: summaryRaw,
                render: ToolOutputHints.RenderCode(language: "text", contentPath: "raw_output")));
        }

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: result.Distributions,
            viewRowsPath: "distributions_view",
            title: "WSL distributions (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            response: out var response);
        return Task.FromResult(response);
    }

    private static string ErrorFromWslFailure(WslStatusQueryFailure? failure) {
        if (failure?.Code == WslStatusQueryFailureCode.Cancelled) {
            throw new OperationCanceledException(failure.Message);
        }

        return failure?.Code switch {
            WslStatusQueryFailureCode.InvalidRequest => ToolResponse.Error("invalid_argument", failure.Message),
            WslStatusQueryFailureCode.PlatformNotSupported => ToolResponse.Error(
                errorCode: "unsupported_platform",
                error: failure.Message,
                hints: new[] { "Run this tool on Windows." },
                isTransient: false),
            _ => ToolResponse.Error(
                errorCode: "process_error",
                error: failure?.Message ?? "WSL status query failed.",
                hints: new[] { "Ensure WSL is installed and accessible from PATH." },
                isTransient: true)
        };
    }
}


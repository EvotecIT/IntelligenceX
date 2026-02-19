using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Storage;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists physical disks (read-only, capped).
/// </summary>
public sealed class SystemDisksListTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_disks_list",
        "List physical disks (read-only, capped).",
        ToolSchema.Object(
                ("model_contains", ToolSchema.String("Optional case-insensitive model substring filter.")),
                ("interface_contains", ToolSchema.String("Optional case-insensitive interface type substring filter.")),
                ("media_contains", ToolSchema.String("Optional case-insensitive media type substring filter.")),
                ("min_size_bytes", ToolSchema.Integer("Optional minimum disk size in bytes.")),
                ("max_entries", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemDisksListTool"/> class.
    /// </summary>
    public SystemDisksListTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var modelContains = ToolArgs.GetOptionalTrimmed(arguments, "model_contains");
        var interfaceContains = ToolArgs.GetOptionalTrimmed(arguments, "interface_contains");
        var mediaContains = ToolArgs.GetOptionalTrimmed(arguments, "media_contains");
        var max = ResolveBoundedOptionLimit(arguments, "max_entries");

        var minSizeArg = arguments?.GetInt64("min_size_bytes");
        long? minSize = null;
        if (minSizeArg.HasValue) {
            if (minSizeArg.Value < 0) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "min_size_bytes must be greater than or equal to zero."));
            }
            minSize = minSizeArg.Value;
        }

        if (!DiskInventoryQueryExecutor.TryExecute(
                request: new DiskInventoryQueryRequest {
                    ModelContains = modelContains,
                    InterfaceContains = interfaceContains,
                    MediaContains = mediaContains,
                    MinSizeBytes = minSize,
                    MaxResults = max
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Disk query failed."));
        }

        var result = queryResult ?? new DiskInventoryQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Disks,
            viewRowsPath: "disks_view",
            title: "Physical disks (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                if (!string.IsNullOrWhiteSpace(modelContains)) {
                    meta.Add("model_contains", modelContains);
                }
                if (!string.IsNullOrWhiteSpace(interfaceContains)) {
                    meta.Add("interface_contains", interfaceContains);
                }
                if (!string.IsNullOrWhiteSpace(mediaContains)) {
                    meta.Add("media_contains", mediaContains);
                }
                if (minSize.HasValue) {
                    meta.Add("min_size_bytes", minSize.Value);
                }
            });
        return Task.FromResult(response);
    }
}


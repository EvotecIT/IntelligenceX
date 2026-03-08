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
    private sealed record DisksListRequest(
        string? ComputerName,
        string Target,
        string? ModelContains,
        string? InterfaceContains,
        string? MediaContains,
        long? MinSizeBytes,
        int MaxEntries);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_disks_list",
        "List physical disks (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<DisksListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var minSize = reader.OptionalInt64("min_size_bytes");
            if (minSize.HasValue && minSize.Value < 0) {
                return ToolRequestBindingResult<DisksListRequest>.Failure(
                    "min_size_bytes must be greater than or equal to zero.");
            }

            return ToolRequestBindingResult<DisksListRequest>.Success(new DisksListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                ModelContains: reader.OptionalString("model_contains"),
                InterfaceContains: reader.OptionalString("interface_contains"),
                MediaContains: reader.OptionalString("media_contains"),
                MinSizeBytes: minSize,
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DisksListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!DiskInventoryQueryExecutor.TryExecute(
                request: new DiskInventoryQueryRequest {
                    ComputerName = request.ComputerName,
                    ModelContains = request.ModelContains,
                    InterfaceContains = request.InterfaceContains,
                    MediaContains = request.MediaContains,
                    MinSizeBytes = request.MinSizeBytes,
                    MaxResults = request.MaxEntries
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Disk query failed."));
        }

        var result = queryResult ?? new DiskInventoryQueryResult();
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Disks,
            viewRowsPath: "disks_view",
            title: "Physical disks (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, request.Target);
                AddMaxResultsMeta(meta, request.MaxEntries);
                if (!string.IsNullOrWhiteSpace(request.ModelContains)) {
                    meta.Add("model_contains", request.ModelContains);
                }
                if (!string.IsNullOrWhiteSpace(request.InterfaceContains)) {
                    meta.Add("interface_contains", request.InterfaceContains);
                }
                if (!string.IsNullOrWhiteSpace(request.MediaContains)) {
                    meta.Add("media_contains", request.MediaContains);
                }
                if (request.MinSizeBytes.HasValue) {
                    meta.Add("min_size_bytes", request.MinSizeBytes.Value);
                }
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_disks_list",
                    targetComputer: request.Target,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: result.Scanned,
                    truncated: result.Truncated);
            });
        return Task.FromResult(response);
    }
}

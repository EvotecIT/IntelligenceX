using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Storage;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists logical disks/volumes (read-only, capped).
/// </summary>
public sealed class SystemLogicalDisksListTool : SystemToolBase, ITool {
    private sealed record LogicalDisksListRequest(
        string? ComputerName,
        string Target,
        string? NameContains,
        string? FileSystem,
        DriveType? DriveType,
        long? MinSizeBytes,
        long? MinFreeBytes,
        int MaxEntries);

    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, DriveType> DriveTypeByName =
        new Dictionary<string, DriveType>(StringComparer.OrdinalIgnoreCase) {
            ["fixed"] = DriveType.Fixed,
            ["removable"] = DriveType.Removable,
            ["network"] = DriveType.Network,
            ["cdrom"] = DriveType.CDRom,
            ["ram"] = DriveType.Ram,
            ["unknown"] = DriveType.Unknown,
            ["no_root_directory"] = DriveType.NoRootDirectory
        };

    private static readonly IReadOnlyDictionary<DriveType, string> DriveTypeNames =
        new Dictionary<DriveType, string> {
            [DriveType.Fixed] = "fixed",
            [DriveType.Removable] = "removable",
            [DriveType.Network] = "network",
            [DriveType.CDRom] = "cdrom",
            [DriveType.Ram] = "ram",
            [DriveType.NoRootDirectory] = "no_root_directory",
            [DriveType.Unknown] = "unknown"
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_logical_disks_list",
        "List logical disks/volumes (read-only, capped).",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive filter against drive name/label.")),
                ("file_system", ToolSchema.String("Optional case-insensitive exact file-system filter (for example NTFS).")),
                ("drive_type", ToolSchema.String("Optional drive type filter.").Enum("any", "fixed", "removable", "network", "cdrom", "ram", "unknown", "no_root_directory")),
                ("min_size_bytes", ToolSchema.Integer("Optional minimum total size in bytes.")),
                ("min_free_bytes", ToolSchema.Integer("Optional minimum free space in bytes.")),
                ("max_entries", ToolSchema.Integer("Optional maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemLogicalDisksListTool"/> class.
    /// </summary>
    public SystemLogicalDisksListTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<LogicalDisksListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var minSize = reader.OptionalInt64("min_size_bytes");
            if (minSize.HasValue && minSize.Value < 0) {
                return ToolRequestBindingResult<LogicalDisksListRequest>.Failure(
                    "min_size_bytes must be greater than or equal to zero.");
            }

            var minFree = reader.OptionalInt64("min_free_bytes");
            if (minFree.HasValue && minFree.Value < 0) {
                return ToolRequestBindingResult<LogicalDisksListRequest>.Failure(
                    "min_free_bytes must be greater than or equal to zero.");
            }

            if (!ToolEnumBinders.TryParseOptional(
                    reader.OptionalString("drive_type"),
                    DriveTypeByName,
                    "drive_type",
                    out DriveType? driveType,
                    out var driveTypeError)) {
                return ToolRequestBindingResult<LogicalDisksListRequest>.Failure(
                    driveTypeError ?? "Invalid drive_type value.");
            }

            return ToolRequestBindingResult<LogicalDisksListRequest>.Success(new LogicalDisksListRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                NameContains: reader.OptionalString("name_contains"),
                FileSystem: reader.OptionalString("file_system"),
                DriveType: driveType,
                MinSizeBytes: minSize,
                MinFreeBytes: minFree,
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<LogicalDisksListRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        if (!LogicalDiskInventoryQueryExecutor.TryExecute(
                request: new LogicalDiskInventoryQueryRequest {
                    ComputerName = request.ComputerName,
                    NameContains = request.NameContains,
                    FileSystem = request.FileSystem,
                    DriveType = request.DriveType,
                    MinSizeBytes = request.MinSizeBytes,
                    MinFreeBytes = request.MinFreeBytes,
                    MaxResults = request.MaxEntries
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Logical disk query failed."));
        }

        var result = queryResult ?? new LogicalDiskInventoryQueryResult();
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Disks,
            viewRowsPath: "disks_view",
            title: "Logical disks (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, request.Target);
                AddMaxResultsMeta(meta, request.MaxEntries);
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                if (!string.IsNullOrWhiteSpace(request.FileSystem)) {
                    meta.Add("file_system", request.FileSystem);
                }
                if (request.DriveType.HasValue) {
                    meta.Add("drive_type", ToolEnumBinders.ToName(request.DriveType.Value, DriveTypeNames));
                }
                if (request.MinSizeBytes.HasValue) {
                    meta.Add("min_size_bytes", request.MinSizeBytes.Value);
                }
                if (request.MinFreeBytes.HasValue) {
                    meta.Add("min_free_bytes", request.MinFreeBytes.Value);
                }
                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_logical_disks_list",
                    targetComputer: request.Target,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: result.Scanned,
                    truncated: result.Truncated);
            });
        return Task.FromResult(response);
    }
}

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
        cancellationToken.ThrowIfCancellationRequested();

        var nameContains = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var fileSystem = ToolArgs.GetOptionalTrimmed(arguments, "file_system");
        var max = ResolveBoundedOptionLimit(arguments, "max_entries");

        var minSizeArg = arguments?.GetInt64("min_size_bytes");
        long? minSize = null;
        if (minSizeArg.HasValue) {
            if (minSizeArg.Value < 0) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "min_size_bytes must be greater than or equal to zero."));
            }
            minSize = minSizeArg.Value;
        }

        var minFreeArg = arguments?.GetInt64("min_free_bytes");
        long? minFree = null;
        if (minFreeArg.HasValue) {
            if (minFreeArg.Value < 0) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", "min_free_bytes must be greater than or equal to zero."));
            }
            minFree = minFreeArg.Value;
        }

        if (!ToolEnumBinders.TryParseOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "drive_type"),
                DriveTypeByName,
                "drive_type",
                out DriveType? driveType,
                out var driveTypeError)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", driveTypeError ?? "Invalid drive_type value."));
        }

        if (!LogicalDiskInventoryQueryExecutor.TryExecute(
                request: new LogicalDiskInventoryQueryRequest {
                    NameContains = nameContains,
                    FileSystem = fileSystem,
                    DriveType = driveType,
                    MinSizeBytes = minSize,
                    MinFreeBytes = minFree,
                    MaxResults = max
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Logical disk query failed."));
        }

        var result = queryResult ?? new LogicalDiskInventoryQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Disks,
            viewRowsPath: "disks_view",
            title: "Logical disks (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                if (!string.IsNullOrWhiteSpace(nameContains)) {
                    meta.Add("name_contains", nameContains);
                }
                if (!string.IsNullOrWhiteSpace(fileSystem)) {
                    meta.Add("file_system", fileSystem);
                }
                if (driveType.HasValue) {
                    meta.Add("drive_type", ToolEnumBinders.ToName(driveType.Value, DriveTypeNames));
                }
                if (minSize.HasValue) {
                    meta.Add("min_size_bytes", minSize.Value);
                }
                if (minFree.HasValue) {
                    meta.Add("min_free_bytes", minFree.Value);
                }
            });
        return Task.FromResult(response);
    }
}


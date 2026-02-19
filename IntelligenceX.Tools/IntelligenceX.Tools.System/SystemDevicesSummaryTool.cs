using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Devices;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists and summarizes USB/Device Manager devices (read-only, capped).
/// </summary>
public sealed class SystemDevicesSummaryTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_devices_summary",
        "List and summarize USB/Device Manager devices (read-only, capped).",
        ToolSchema.Object(
                ("include_usb", ToolSchema.Boolean("Include USB inventory rows. Default true.")),
                ("include_device_manager", ToolSchema.Boolean("Include Device Manager inventory rows. Default true.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive device-name filter.")),
                ("class_contains", ToolSchema.String("Optional case-insensitive class filter.")),
                ("manufacturer_contains", ToolSchema.String("Optional case-insensitive USB manufacturer filter.")),
                ("status_contains", ToolSchema.String("Optional case-insensitive Device Manager status filter.")),
                ("problem_only", ToolSchema.Boolean("When true, return only rows with non-empty problem code.")),
                ("max_entries", ToolSchema.Integer("Optional maximum rows to return (capped).")),
                ("timeout_ms", ToolSchema.Integer("Optional query timeout in milliseconds (capped). Default 10000.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemDevicesSummaryTool"/> class.
    /// </summary>
    public SystemDevicesSummaryTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var includeUsb = arguments?.GetBoolean("include_usb", defaultValue: true) ?? true;
        var includeDeviceManager = arguments?.GetBoolean("include_device_manager", defaultValue: true) ?? true;
        if (!includeUsb && !includeDeviceManager) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "At least one source (include_usb/include_device_manager) must be true."));
        }

        var nameContains = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var classContains = ToolArgs.GetOptionalTrimmed(arguments, "class_contains");
        var manufacturerContains = ToolArgs.GetOptionalTrimmed(arguments, "manufacturer_contains");
        var statusContains = ToolArgs.GetOptionalTrimmed(arguments, "status_contains");
        var problemOnly = arguments?.GetBoolean("problem_only", defaultValue: false) ?? false;
        var max = ResolveBoundedOptionLimit(arguments, "max_entries");
        var timeoutMs = ResolveTimeoutMs(arguments);

        if (!DeviceInventoryQueryExecutor.TryExecute(
                request: new DeviceInventoryQueryRequest {
                    IncludeUsb = includeUsb,
                    IncludeDeviceManager = includeDeviceManager,
                    NameContains = nameContains,
                    ClassContains = classContains,
                    ManufacturerContains = manufacturerContains,
                    StatusContains = statusContains,
                    ProblemOnly = problemOnly,
                    MaxResults = max,
                    Timeout = TimeSpan.FromMilliseconds(timeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Device inventory query failed."));
        }

        var result = queryResult ?? new DeviceInventoryQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Devices,
            viewRowsPath: "devices_view",
            title: "Devices (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                meta.Add("include_usb", includeUsb);
                meta.Add("include_device_manager", includeDeviceManager);
                meta.Add("problem_only", problemOnly);
                meta.Add("timeout_ms", timeoutMs);

                if (!string.IsNullOrWhiteSpace(nameContains)) {
                    meta.Add("name_contains", nameContains);
                }
                if (!string.IsNullOrWhiteSpace(classContains)) {
                    meta.Add("class_contains", classContains);
                }
                if (!string.IsNullOrWhiteSpace(manufacturerContains)) {
                    meta.Add("manufacturer_contains", manufacturerContains);
                }
                if (!string.IsNullOrWhiteSpace(statusContains)) {
                    meta.Add("status_contains", statusContains);
                }
            });
        return Task.FromResult(response);
    }
}

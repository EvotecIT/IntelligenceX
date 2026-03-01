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
    private sealed record DevicesSummaryRequest(
        bool IncludeUsb,
        bool IncludeDeviceManager,
        string? NameContains,
        string? ClassContains,
        string? ManufacturerContains,
        string? StatusContains,
        bool ProblemOnly,
        int MaxEntries,
        int TimeoutMs);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<DevicesSummaryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var includeUsb = reader.Boolean("include_usb", defaultValue: true);
            var includeDeviceManager = reader.Boolean("include_device_manager", defaultValue: true);
            if (!includeUsb && !includeDeviceManager) {
                return ToolRequestBindingResult<DevicesSummaryRequest>.Failure(
                    "At least one source (include_usb/include_device_manager) must be true.");
            }

            return ToolRequestBindingResult<DevicesSummaryRequest>.Success(new DevicesSummaryRequest(
                IncludeUsb: includeUsb,
                IncludeDeviceManager: includeDeviceManager,
                NameContains: reader.OptionalString("name_contains"),
                ClassContains: reader.OptionalString("class_contains"),
                ManufacturerContains: reader.OptionalString("manufacturer_contains"),
                StatusContains: reader.OptionalString("status_contains"),
                ProblemOnly: reader.Boolean("problem_only", defaultValue: false),
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries"),
                TimeoutMs: ResolveTimeoutMs(arguments)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DevicesSummaryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        if (!DeviceInventoryQueryExecutor.TryExecute(
                request: new DeviceInventoryQueryRequest {
                    IncludeUsb = request.IncludeUsb,
                    IncludeDeviceManager = request.IncludeDeviceManager,
                    NameContains = request.NameContains,
                    ClassContains = request.ClassContains,
                    ManufacturerContains = request.ManufacturerContains,
                    StatusContains = request.StatusContains,
                    ProblemOnly = request.ProblemOnly,
                    MaxResults = request.MaxEntries,
                    Timeout = TimeSpan.FromMilliseconds(request.TimeoutMs)
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(ErrorFromFailure(failure, static x => x.Code, static x => x.Message, defaultMessage: "Device inventory query failed."));
        }

        var result = queryResult ?? new DeviceInventoryQueryResult();
        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Devices,
            viewRowsPath: "devices_view",
            title: "Devices (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned,
            metaMutate: meta => {
                meta.Add("include_usb", request.IncludeUsb);
                meta.Add("include_device_manager", request.IncludeDeviceManager);
                meta.Add("problem_only", request.ProblemOnly);
                meta.Add("timeout_ms", request.TimeoutMs);

                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
                if (!string.IsNullOrWhiteSpace(request.ClassContains)) {
                    meta.Add("class_contains", request.ClassContains);
                }
                if (!string.IsNullOrWhiteSpace(request.ManufacturerContains)) {
                    meta.Add("manufacturer_contains", request.ManufacturerContains);
                }
                if (!string.IsNullOrWhiteSpace(request.StatusContains)) {
                    meta.Add("status_contains", request.StatusContains);
                }
            });
        return Task.FromResult(response);
    }
}

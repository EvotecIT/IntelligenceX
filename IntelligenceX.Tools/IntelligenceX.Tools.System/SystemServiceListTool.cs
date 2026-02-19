using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Services;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Lists Windows services (read-only, capped).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemServiceListTool : SystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly IReadOnlyDictionary<string, ServiceListStatusFilter> StatusFilterByName =
        new Dictionary<string, ServiceListStatusFilter>(StringComparer.OrdinalIgnoreCase) {
            ["running"] = ServiceListStatusFilter.Running,
            ["stopped"] = ServiceListStatusFilter.Stopped,
            ["paused"] = ServiceListStatusFilter.Paused
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "system_service_list",
        "List Windows services (read-only, capped).",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive filter against service name and display name.")),
                ("status", ToolSchema.String("Optional status filter.").Enum("any", "running", "stopped", "paused")),
                ("max_services", ToolSchema.Integer("Optional maximum services to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemServiceListTool"/> class.
    /// </summary>
    public SystemServiceListTool(SystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var nameContains = arguments?.GetString("name_contains");
        if (!ToolEnumBinders.TryParseOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "status"),
                StatusFilterByName,
                "status",
                out ServiceListStatusFilter? parsedStatus,
                out var statusError)) {
            return ToolResponse.Error("invalid_argument", statusError ?? "Invalid status value.");
        }

        var status = parsedStatus ?? ServiceListStatusFilter.Any;
        var max = ResolveBoundedOptionLimit(arguments, "max_services");

        var attempt = await ServiceListQueryExecutor.TryExecuteAsync(
            request: new ServiceListQueryRequest {
                NameContains = string.IsNullOrWhiteSpace(nameContains) ? null : nameContains.Trim(),
                StatusFilter = status,
                MaxResults = max,
                SortBy = ServiceListQuerySort.ServiceNameAsc
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!attempt.Success) {
            return ErrorFromFailure(attempt.Failure, static x => x.Code, static x => x.Message, defaultMessage: "Service query failed.");
        }

        var result = attempt.Result ?? new ServiceListQueryResult();
        var response = BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Services,
            viewRowsPath: "services_view",
            title: "Services (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.Truncated,
            scanned: result.Scanned);
        return response;
    }
}


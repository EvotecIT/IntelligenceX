using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Lists Windows Event Collector subscriptions on the local or remote machine.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogCollectorSubscriptionsListTool : EventLogToolBase, ITool {
    private const int MaxViewTop = 5000;

    private sealed record CollectorSubscriptionsListRequest(
        string? MachineName,
        string? NameContains,
        bool EnabledOnly,
        int MaxResults);

    private sealed record CollectorSubscriptionViewRow(
        string SubscriptionName,
        string MachineName,
        bool? IsEnabled,
        string? Description,
        string? ContentFormat,
        string? DeliveryMode,
        bool HasXml,
        int QueryCount,
        IReadOnlyList<string> Queries);

    private sealed record CollectorSubscriptionsListResult(
        int Count,
        bool Truncated,
        string? MachineName,
        bool EnabledOnly,
        string? NameContains,
        IReadOnlyList<CollectorSubscriptionViewRow> Items);

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_collector_subscriptions_list",
        "List Windows Event Collector subscriptions on the local or remote machine (read-only, capped).",
        ToolSchema.Object(
                ("machine_name", ToolSchema.String("Optional remote machine name/FQDN. Omit for the local machine.")),
                ("name_contains", ToolSchema.String("Optional case-insensitive subscription-name filter.")),
                ("enabled_only", ToolSchema.Boolean("When true, return only enabled collector subscriptions.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogCollectorSubscriptionsListTool"/> class.
    /// </summary>
    public EventLogCollectorSubscriptionsListTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<CollectorSubscriptionsListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => ToolRequestBindingResult<CollectorSubscriptionsListRequest>.Success(
            new CollectorSubscriptionsListRequest(
                MachineName: reader.OptionalString("machine_name"),
                NameContains: reader.OptionalString("name_contains"),
                EnabledOnly: reader.Boolean("enabled_only", defaultValue: false),
                MaxResults: ResolveOptionBoundedMaxResults(arguments))));
    }

    private Task<string> ExecuteAsync(
        ToolPipelineContext<CollectorSubscriptionsListRequest> context,
        CancellationToken cancellationToken) {
        if (!OperatingSystem.IsWindows()) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "platform_not_supported",
                error: "eventlog_collector_subscriptions_list is supported only on Windows."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var request = context.Request;
        var machineName = ToolArgs.NormalizeOptional(request.MachineName);

        IReadOnlyList<SubscriptionInfo> subscriptions;
        try {
            subscriptions = SearchEvents.GetCollectorSubscriptions(machineName)
                .ToArray();
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "Collector subscription query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed"));
        }

        IEnumerable<SubscriptionInfo> query = subscriptions;
        if (!string.IsNullOrWhiteSpace(request.NameContains)) {
            query = query.Where(subscription =>
                !string.IsNullOrWhiteSpace(subscription.Name)
                && subscription.Name.IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        if (request.EnabledOnly) {
            query = query.Where(static subscription => subscription.Enabled == true);
        }

        var matchedRows = query
            .OrderBy(static subscription => subscription.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var truncated = matchedRows.Count > request.MaxResults;
        var selectedRows = (truncated ? matchedRows.Take(request.MaxResults) : matchedRows)
            .Select(subscription => new CollectorSubscriptionViewRow(
                SubscriptionName: subscription.Name,
                MachineName: ToolArgs.NormalizeOptional(subscription.MachineName) ?? machineName ?? Environment.MachineName,
                IsEnabled: subscription.Enabled,
                Description: ToolArgs.NormalizeOptional(subscription.Description),
                ContentFormat: ToolArgs.NormalizeOptional(subscription.ContentFormat),
                DeliveryMode: ToolArgs.NormalizeOptional(subscription.DeliveryMode),
                HasXml: !string.IsNullOrWhiteSpace(subscription.RawXml),
                QueryCount: subscription.Queries?.Count ?? 0,
                Queries: subscription.Queries?
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .ToArray() ?? Array.Empty<string>()))
            .ToList();

        var result = new CollectorSubscriptionsListResult(
            Count: selectedRows.Count,
            Truncated: truncated,
            MachineName: machineName,
            EnabledOnly: request.EnabledOnly,
            NameContains: request.NameContains,
            Items: selectedRows);

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: SanitizeProjectionArguments(context.Arguments, selectedRows),
            model: result,
            sourceRows: selectedRows,
            viewRowsPath: "items_view",
            title: "Collector subscriptions (preview)",
            baseTruncated: truncated,
            scanned: matchedRows.Count,
            maxTop: MaxViewTop,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, request.MaxResults);
                meta.Add("enabled_only", request.EnabledOnly);
                meta.Add("matched", matchedRows.Count);
                if (!string.IsNullOrWhiteSpace(machineName)) {
                    meta.Add("machine_name", machineName);
                }
                if (!string.IsNullOrWhiteSpace(request.NameContains)) {
                    meta.Add("name_contains", request.NameContains);
                }
            });

        return Task.FromResult(response);
    }
}

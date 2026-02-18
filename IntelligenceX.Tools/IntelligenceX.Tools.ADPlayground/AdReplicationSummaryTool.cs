using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Summarizes Active Directory replication health (read-only).
/// </summary>
public sealed class AdReplicationSummaryTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultStaleThresholdHours = 24;
    private const int MaxStaleThresholdHours = 24 * 14;
    private const int DefaultMaxDetails = 1000;
    private const int DefaultMaxDomainControllers = 200;
    private const int DefaultMaxErrors = 25;
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_replication_summary",
        "Summarize Active Directory replication health (repadmin /replsummary-like) with optional edge details (read-only).",
        ToolSchema.Object(
                ("domain_controller", ToolSchema.String("Optional domain controller to query. When set, reports replication edges for that DC only.")),
                ("domain_name", ToolSchema.String("Optional DNS domain name to scope to a single domain. When omitted, uses the current forest.")),
                ("outbound", ToolSchema.Boolean("When true, summarizes links from the source perspective (source -> destination) instead of inbound (default false).")),
                ("by_source", ToolSchema.Boolean("When true, group summary rows by source (neighbor) instead of destination (domain controller). Default false.")),
                ("stale_threshold_hours", ToolSchema.Integer("Threshold used to flag stale replication (hours since last success). Default 24.")),
                ("bucket_hours", ToolSchema.Array(ToolSchema.Integer(), "Histogram bucket edges (hours) for largest per-server delta. Default [1,3,6,12,24].")),
                ("include_details", ToolSchema.Boolean("When true, include per-neighbor edge rows (can be large). Default false.")),
                ("max_details", ToolSchema.Integer("Maximum edge rows to include when include_details=true (capped). Default 1000.")),
                ("max_domain_controllers", ToolSchema.Integer("Upper bound on DCs enumerated when domain_controller is omitted (safety cap). Default 200.")),
                ("max_errors", ToolSchema.Integer("Maximum number of per-DC errors to return. Default 25.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdReplicationSummaryTool"/> class.
    /// </summary>
    public AdReplicationSummaryTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var outbound = arguments?.GetBoolean("outbound") ?? false;
        var bySource = arguments?.GetBoolean("by_source") ?? false;

        var requestedStale = arguments?.GetInt64("stale_threshold_hours");
        var staleThresholdHours = requestedStale.HasValue && requestedStale.Value > 0
            ? (int)Math.Min(requestedStale.Value, MaxStaleThresholdHours)
            : DefaultStaleThresholdHours;

        var includeDetails = arguments?.GetBoolean("include_details") ?? false;

        var requestedMaxDetails = arguments?.GetInt64("max_details");
        var maxDetails = requestedMaxDetails.HasValue && requestedMaxDetails.Value > 0
            ? (int)Math.Min(requestedMaxDetails.Value, Options.MaxResults)
            : Math.Min(DefaultMaxDetails, Options.MaxResults);

        var requestedMaxDcs = arguments?.GetInt64("max_domain_controllers");
        var maxDomainControllers = requestedMaxDcs.HasValue && requestedMaxDcs.Value > 0
            ? (int)Math.Min(requestedMaxDcs.Value, 10_000)
            : DefaultMaxDomainControllers;

        var requestedMaxErrors = arguments?.GetInt64("max_errors");
        var maxErrors = requestedMaxErrors.HasValue && requestedMaxErrors.Value > 0
            ? (int)Math.Min(requestedMaxErrors.Value, 10_000)
            : DefaultMaxErrors;

        var result = ReplicationSummaryQueryService.Query(
            new ReplicationSummaryQueryOptions {
                DomainController = arguments?.GetString("domain_controller"),
                DomainName = arguments?.GetString("domain_name"),
                Outbound = outbound,
                BySource = bySource,
                StaleThresholdHours = staleThresholdHours,
                BucketHours = ToolArgs.ReadPositiveInt32ArrayCapped(arguments?.GetArray("bucket_hours"), maxInclusive: 24 * 30),
                IncludeDetails = includeDetails,
                MaxDetails = maxDetails,
                MaxDomainControllers = maxDomainControllers,
                MaxErrors = maxErrors
            },
            cancellationToken);

        var anyTruncated = result.DetailsTruncated == true || result.ErrorsTruncated == true;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: result.Summary,
            viewRowsPath: "summary_view",
            title: "Active Directory: Replication Summary (preview)",
            maxTop: MaxViewTop,
            baseTruncated: anyTruncated,
            scanned: result.EdgesTotal));
    }
}

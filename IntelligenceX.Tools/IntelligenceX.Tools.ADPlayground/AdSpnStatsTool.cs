using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using ADPlayground.Kerberos;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Aggregates SPNs by service class/host to help audit Kerberos service exposure (read-only).
/// </summary>
public sealed class AdSpnStatsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_spn_stats",
        "Aggregate servicePrincipalName (SPN) values by service class/host (read-only).",
        ToolSchema.Object(
                ("spn_contains", ToolSchema.String("Optional substring match for SPNs (LDAP contains). When omitted, includes all SPNs.")),
                ("spn_exact", ToolSchema.String("Optional exact SPN match (mutually exclusive with spn_contains).")),
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "computer")),
                ("enabled_only", ToolSchema.Boolean("When true, filter out disabled accounts (userAccountControl bit 2). Default false.")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum directory objects to scan (capped).")),
                ("max_service_classes", ToolSchema.Integer("Maximum service classes returned (capped).")),
                ("max_hosts", ToolSchema.Integer("Maximum hosts returned (capped).")),
                ("include_examples", ToolSchema.Boolean("When true, include up to 3 example SPNs and accounts per service class.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSpnStatsTool"/> class.
    /// </summary>
    public AdSpnStatsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var spnContains = ToolArgs.GetOptionalTrimmed(arguments, "spn_contains");
        var spnExact = ToolArgs.GetOptionalTrimmed(arguments, "spn_exact");
        if (!string.IsNullOrWhiteSpace(spnContains) && !string.IsNullOrWhiteSpace(spnExact)) {
            return Error("invalid_argument", "spn_contains and spn_exact are mutually exclusive.");
        }

        var kind = LdapToolKinds.ParseSpnAccountKind(ToolArgs.GetOptionalTrimmed(arguments, "kind"));
        var enabledOnly = ToolArgs.GetBoolean(arguments, "enabled_only");
        var includeExamples = ToolArgs.GetBoolean(arguments, "include_examples");

        var requestedMax = arguments?.GetInt64("max_results");
        var maxObjects = requestedMax.HasValue && requestedMax.Value > 0
            ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
            : Options.MaxResults;

        var requestedServiceClasses = arguments?.GetInt64("max_service_classes");
        var maxServiceClasses = requestedServiceClasses.HasValue && requestedServiceClasses.Value > 0
            ? (int)Math.Min(requestedServiceClasses.Value, 200)
            : 50;

        var requestedHosts = arguments?.GetInt64("max_hosts");
        var maxHosts = requestedHosts.HasValue && requestedHosts.Value > 0
            ? (int)Math.Min(requestedHosts.Value, 500)
            : 50;

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        var query = new SpnStatsService.SpnStatsQueryOptions {
            DomainController = dc,
            SearchBaseDn = baseDn,
            SpnContains = spnContains,
            SpnExact = spnExact,
            Kind = kind,
            EnabledOnly = enabledOnly,
            PageSize = 250,
            MaxObjects = maxObjects,
            MaxServiceClasses = maxServiceClasses,
            MaxHosts = maxHosts,
            IncludeExamples = includeExamples,
            MaxExamplesPerBucket = 3
        };

        var stats = await SpnStatsService.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return BuildAutoTableResponse(
            arguments: arguments,
            model: stats,
            sourceRows: stats.ServiceClasses,
            viewRowsPath: "service_classes_view",
            title: "Active Directory: SPN Stats (preview)",
            maxTop: MaxViewTop,
            baseTruncated: stats.Truncated,
            scanned: stats.ScannedObjects);
    }
}

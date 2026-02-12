using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Lists Active Directory groups (read-only).
/// </summary>
public sealed class AdGroupsListTool : ActiveDirectoryToolBase, ITool {
    private const int MaxPageSizeCap = 2000;

    private static readonly string[] DefaultAttributes = {
        "distinguishedName",
        "cn",
        "name",
        "sAMAccountName",
        "description",
        "whenCreated"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase) {
        "distinguishedName",
        "cn",
        "name",
        "sAMAccountName",
        "description",
        "mail",
        "managedBy",
        "groupType",
        "member",
        "memberOf",
        "whenCreated",
        "whenChanged"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_groups_list",
        "List Active Directory groups (read-only, capped). Omit filters to list all groups (up to max_results).",
        ToolSchema.Object(
                ("name_contains", ToolSchema.String("Optional case-insensitive substring match against cn/name/sAMAccountName. Use '*' or omit to list all groups.")),
                ("name_prefix", ToolSchema.String("Optional case-insensitive prefix match against cn/name/sAMAccountName (e.g. 'ADM_').")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")),
                ("offset", ToolSchema.Integer("Offset into the ordered result set (0-based). Default 0.")),
                ("page_size", ToolSchema.Integer("Maximum results to return for this page (capped). Default 200.")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional attributes to include (allowlist enforced).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGroupsListTool"/> class.
    /// </summary>
    public AdGroupsListTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var nameContains = ToolArgs.GetOptionalTrimmed(arguments, "name_contains");
        var namePrefix = ToolArgs.GetOptionalTrimmed(arguments, "name_prefix");

        var requestedMax = arguments?.GetInt64("max_results");
        var maxResults = requestedMax.HasValue && requestedMax.Value > 0
            ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
            : Options.MaxResults;

        var offset = Math.Max(arguments?.GetInt64("offset") ?? 0, 0);
        var requestedPageSize = arguments?.GetInt64("page_size");
        // Preserve historic behavior: by default return up to max_results unless caller opts into smaller paging.
        var pageSize = requestedPageSize.HasValue && requestedPageSize.Value > 0
            ? (int)Math.Min(requestedPageSize.Value, MaxPageSizeCap)
            : maxResults;

        var maxValuesPerAttribute = ToolArgs.GetCappedInt32(
            arguments,
            "max_values_per_attribute",
            LdapQueryPolicy.DefaultMaxValuesPerAttribute,
            1,
            LdapQueryPolicy.MaxValuesPerAttributeCap);

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        var attributes = ToolArgs.ReadAllowedStrings(arguments?.GetArray("attributes"), AllowedAttributes);
        if (attributes.Count == 0) {
            attributes.AddRange(DefaultAttributes);
        }
        LdapQueryPolicy.EnsureIncluded(attributes, "name");
        // Ensure stable columns for UI rendering even when the caller provides a custom attribute set.
        LdapQueryPolicy.EnsureIncluded(attributes, "sAMAccountName");
        LdapQueryPolicy.EnsureIncluded(attributes, "distinguishedName");

        if (!AdGroupsListService.TryQuery(
                options: new AdGroupsListService.GroupsListQueryOptions {
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    NameContains = nameContains,
                    NamePrefix = namePrefix,
                    MaxResults = maxResults,
                    Offset = offset,
                    PageSize = pageSize,
                    MaxValuesPerAttribute = maxValuesPerAttribute,
                    Attributes = attributes
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = queryResult!;
        AdDynamicTableView.TryBuildResponseFromOutputRows(
            arguments: arguments,
            model: result,
            rows: result.Results,
            title: "Active Directory: Groups (preview)",
            rowsPath: "results_view",
            baseTruncated: result.IsTruncated,
            response: out var response,
            scanned: result.Scanned,
            metaMutate: meta => meta
                .Add("max_results", maxResults)
                .Add("offset", offset)
                .Add("page_size", pageSize));
        return Task.FromResult(response);
    }
}


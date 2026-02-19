using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Executes a read-only LDAP query using the ADPlayground engine with bounded paging and an opaque cursor.
/// This is intended for "less defined" exploratory queries that may return many results.
/// </summary>
public sealed class AdLdapQueryPagedTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ldap_query_paged",
        "Run a read-only LDAP query with paging (cursor) support (read-only). Useful for large result sets.",
        ToolSchema.Object(
                ("ldap_filter", ToolSchema.String("LDAP filter, e.g. '(objectClass=user)'.")),
                ("scope", ToolSchema.String("Search scope.").Enum("subtree", "onelevel", "base")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override (host/FQDN).")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Attributes to include. If omitted, returns a small safe default set.")),
                ("allow_sensitive_attributes", ToolSchema.Boolean("When true, allows requesting sensitive attributes (not recommended). Default false.")),
                ("page_size", ToolSchema.Integer($"LDAP page size (capped). Default {LdapToolAdLdapQueryPagedRequest.DefaultPageSize}.")),
                ("max_pages", ToolSchema.Integer($"Maximum pages to read in this call (capped). Default {LdapToolAdLdapQueryPagedRequest.DefaultMaxPages}.")),
                ("cursor", ToolSchema.String("Opaque paging cursor (base64) from a previous call.")),
                ("max_results", ToolSchema.Integer("Maximum results to return (capped).")),
                ("max_attributes", ToolSchema.Integer("Maximum attributes to include (capped). Default 20.")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("timeout_ms", ToolSchema.Integer($"Operation timeout in milliseconds (capped). Default {LdapToolAdLdapQueryPagedRequest.DefaultTimeoutMs}.")))
            .WithTableViewOptions()
            .Required("ldap_filter")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLdapQueryPagedTool"/> class.
    /// </summary>
    public AdLdapQueryPagedTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = ToolArgs.GetTrimmedOrDefault(arguments, "ldap_filter", string.Empty);
        var scope = ToolArgs.GetOptionalTrimmed(arguments, "scope");
        var allowSensitive = ToolArgs.GetBoolean(arguments, "allow_sensitive_attributes");

        var requestedMaxAttrs = arguments?.GetInt64("max_attributes");
        var maxAttributes = requestedMaxAttrs.HasValue && requestedMaxAttrs.Value > 0
            ? (int)Math.Min(requestedMaxAttrs.Value, LdapQueryPolicy.MaxAttributesCap)
            : LdapQueryPolicy.DefaultMaxAttributes;

        var requestedMaxValues = arguments?.GetInt64("max_values_per_attribute");
        var maxValuesPerAttribute = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
            ? (int)Math.Min(requestedMaxValues.Value, LdapQueryPolicy.MaxValuesPerAttributeCap)
            : LdapQueryPolicy.DefaultMaxValuesPerAttribute;

        var maxResults = ResolveMaxResults(arguments);

        var requestedPageSize = arguments?.GetInt64("page_size");
        var pageSize = requestedPageSize.HasValue && requestedPageSize.Value > 0
            ? (int)Math.Min(requestedPageSize.Value, LdapToolAdLdapQueryPagedRequest.MaxPageSizeCap)
            : LdapToolAdLdapQueryPagedRequest.DefaultPageSize;

        var requestedMaxPages = arguments?.GetInt64("max_pages");
        var maxPages = requestedMaxPages.HasValue && requestedMaxPages.Value > 0
            ? (int)Math.Min(requestedMaxPages.Value, LdapToolAdLdapQueryPagedRequest.MaxPagesCap)
            : LdapToolAdLdapQueryPagedRequest.DefaultMaxPages;

        var timeoutMs = (int)Math.Min(
            Math.Max(arguments?.GetInt64("timeout_ms") ?? LdapToolAdLdapQueryPagedRequest.DefaultTimeoutMs, LdapToolAdLdapQueryPagedRequest.MinTimeoutMs),
            LdapToolAdLdapQueryPagedRequest.MaxTimeoutMs);

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);
        var cursor = ToolArgs.GetOptionalTrimmed(arguments, "cursor");
        if (!LdapToolAdLdapQueryPagedService.TryExecute(
                request: new LdapToolAdLdapQueryPagedRequest {
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    LdapFilter = filter,
                    Scope = scope,
                    RequestedAttributes = ToolArgs.ReadStringArray(arguments?.GetArray("attributes")),
                    AllowSensitiveAttributes = allowSensitive,
                    MaxResults = maxResults,
                    ToolMaxResultsCap = Options.MaxResults,
                    MaxAttributes = maxAttributes,
                    MaxValuesPerAttribute = maxValuesPerAttribute,
                    PageSize = pageSize,
                    MaxPages = maxPages,
                    Cursor = cursor,
                    TimeoutMs = timeoutMs
                },
                result: out var pageResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = pageResult!;
        var root = new {
            result.DomainController,
            result.SearchBaseDn,
            result.LdapFilter,
            result.Scope,
            result.TimeoutMs,
            result.PageSize,
            result.MaxPages,
            result.PagesRead,
            result.MaxResults,
            result.MaxAttributes,
            result.MaxValuesPerAttribute,
            result.Count,
            result.LimitReached,
            result.HasMore,
            result.IsTruncated,
            result.Cursor,
            result.NextCursor,
            result.Attributes,
            result.Results
        };

        AdDynamicTableView.TryBuildResponseFromOutputRows(
            arguments: arguments,
            model: root,
            rows: result.Results,
            title: "Active Directory: LDAP Query (paged preview)",
            rowsPath: "results_view",
            baseTruncated: result.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }

}

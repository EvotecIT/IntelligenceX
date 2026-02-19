using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Runs a read-only LDAP search and returns useful facet counts (read-only).
/// </summary>
public sealed class AdSearchFacetsTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultPageSize = 500;
    private const int MaxPageSizeCap = 5000;
    private const int DefaultMaxPages = 3;
    private const int MaxPagesCap = 50;
    private const int DefaultSampleSize = 20;
    private const int MaxSampleSize = 200;
    private const int DefaultMaxFacetValues = 20;
    private const int MaxFacetValuesCap = 200;

    private static readonly int[] DefaultPwdAgeBucketsDays = { 7, 30, 90, 180, 365 };

    private static readonly string[] DefaultAttributes = {
        "distinguishedName",
        "objectClass",
        "objectSid",
        "cn",
        "name",
        "sAMAccountName",
        "userPrincipalName",
        "dNSHostName",
        "displayName",
        "mail",
        "userAccountControl",
        "pwdLastSet"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase) {
        "distinguishedName",
        "objectClass",
        "objectSid",
        "objectGUID",
        "cn",
        "name",
        "sAMAccountName",
        "userPrincipalName",
        "dNSHostName",
        "displayName",
        "mail",
        "description",
        "whenCreated",
        "whenChanged",
        "userAccountControl",
        "adminCount",
        "pwdLastSet",
        "lastLogonTimestamp"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_search_facets",
        "Search Active Directory (read-only) and return facet counts (by OU/container, enabled/disabled, UAC flags, password age buckets).",
        ToolSchema.Object(
                ("ldap_filter", ToolSchema.String("Optional LDAP filter override. If omitted, a safe filter is built from kind + search_text.")),
                ("kind", ToolSchema.String("Object kind to search (used when ldap_filter is omitted). Default user.").Enum("user", "group", "computer", "any")),
                ("search_text", ToolSchema.String("Optional search text (cn/name/sAMAccountName/UPN/DNS). Used when ldap_filter is omitted.")),
                ("scope", ToolSchema.String("Search scope.").Enum("subtree", "onelevel", "base")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override (host/FQDN).")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional attributes to include in samples (allowlist enforced).")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes in samples (capped). Default 50.")),
                ("page_size", ToolSchema.Integer("LDAP page size (capped). Default 500.")),
                ("max_pages", ToolSchema.Integer("Maximum pages to read in this call (capped). Default 3.")),
                ("max_results", ToolSchema.Integer("Maximum results to scan (capped).")),
                ("max_facet_values", ToolSchema.Integer("Maximum facet values to return per facet (capped). Default 20.")),
                ("facet_by_container", ToolSchema.Boolean("Facet: counts by container DN (parent DN). Default true.")),
                ("container_facet_mode", ToolSchema.String("How to group containers when facet_by_container=true. Default parent_dn.").Enum("parent_dn", "top_container", "ou_depth")),
                ("container_ou_depth", ToolSchema.Integer("When container_facet_mode=ou_depth: group by OU depth from the top-level OU (1 = top-level OU). Default 1.")),
                ("facet_by_enabled", ToolSchema.Boolean("Facet: counts by enabled/disabled from userAccountControl. Default true.")),
                ("facet_uac_flags", ToolSchema.Array(ToolSchema.String(), "Facet: UAC flags to count (names). Default common flags.")),
                ("facet_pwd_age_buckets_days", ToolSchema.Array(ToolSchema.Integer(), "Facet: password age bucket edges (days) for pwdLastSet. Default [7,30,90,180,365].")),
                ("include_samples", ToolSchema.Boolean("When true, include a sample of objects (capped). Default true.")),
                ("sample_size", ToolSchema.Integer("Max objects included in samples when include_samples=true (capped). Default 20.")),
                ("timeout_ms", ToolSchema.Integer("Operation timeout in milliseconds (capped). Default 10000.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSearchFacetsTool"/> class.
    /// </summary>
    public AdSearchFacetsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var maxResults = ResolveBoundedMaxResults(arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);

        var requestedPageSize = arguments?.GetInt64("page_size");
        var pageSize = requestedPageSize.HasValue && requestedPageSize.Value > 0
            ? (int)Math.Min(requestedPageSize.Value, MaxPageSizeCap)
            : DefaultPageSize;

        var requestedMaxPages = arguments?.GetInt64("max_pages");
        var maxPages = requestedMaxPages.HasValue && requestedMaxPages.Value > 0
            ? (int)Math.Min(requestedMaxPages.Value, MaxPagesCap)
            : DefaultMaxPages;

        var requestedSampleSize = arguments?.GetInt64("sample_size");
        var sampleSize = requestedSampleSize.HasValue && requestedSampleSize.Value > 0
            ? (int)Math.Min(requestedSampleSize.Value, MaxSampleSize)
            : DefaultSampleSize;

        var requestedMaxFacet = arguments?.GetInt64("max_facet_values");
        var maxFacetValues = requestedMaxFacet.HasValue && requestedMaxFacet.Value > 0
            ? (int)Math.Min(requestedMaxFacet.Value, MaxFacetValuesCap)
            : DefaultMaxFacetValues;

        var maxValuesPerAttribute = ToolArgs.GetCappedInt32(
            arguments,
            "max_values_per_attribute",
            LdapQueryPolicy.DefaultMaxValuesPerAttribute,
            1,
            LdapQueryPolicy.MaxValuesPerAttributeCap);

        var includeSamples = ToolArgs.GetBoolean(arguments, "include_samples", true);
        var facetByContainer = ToolArgs.GetBoolean(arguments, "facet_by_container", true);
        var facetByEnabled = ToolArgs.GetBoolean(arguments, "facet_by_enabled", true);

        var containerFacetMode = ContainerFacetHelper.ParseMode(ToolArgs.GetOptionalTrimmed(arguments, "container_facet_mode"));
        var containerOuDepth = (int)Math.Min(Math.Max(arguments?.GetInt64("container_ou_depth") ?? 1, 1), 20);

        var uacFlags = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("facet_uac_flags"));
        if (uacFlags.Count == 0) {
            uacFlags.AddRange(new[] {
                "ACCOUNTDISABLE",
                "DONT_EXPIRE_PASSWORD",
                "PASSWD_NOTREQD",
                "DONT_REQ_PREAUTH",
                "TRUSTED_FOR_DELEGATION",
                "TRUSTED_TO_AUTH_FOR_DELEGATION"
            });
        }

        var pwdBuckets = ToolArgs.ReadPositiveInt32ArrayCapped(arguments?.GetArray("facet_pwd_age_buckets_days"), maxInclusive: 3650);
        if (pwdBuckets.Count == 0) {
            pwdBuckets.AddRange(DefaultPwdAgeBucketsDays);
        }
        pwdBuckets = pwdBuckets.Distinct().Where(static x => x > 0).OrderBy(static x => x).ToList();

        var timeoutMs = (int)Math.Min(Math.Max(arguments?.GetInt64("timeout_ms") ?? 10_000, 200), 120_000);
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        var scope = LdapQueryPolicy.ParseScope(ToolArgs.GetOptionalTrimmed(arguments, "scope"));
        var ldapFilter = ToolArgs.GetOptionalTrimmed(arguments, "ldap_filter") ?? string.Empty;
        var kindArg = ToolArgs.GetOptionalTrimmed(arguments, "kind");
        var kind = string.IsNullOrWhiteSpace(kindArg) ? "user" : kindArg.Trim().ToLowerInvariant();
        var searchText = (ToolArgs.GetOptionalTrimmed(arguments, "search_text") ?? string.Empty).Trim();

        var attributes = ResolveAttributes(
            arguments: arguments,
            attributesKey: "attributes",
            allowedAttributes: AllowedAttributes,
            defaultAttributes: DefaultAttributes,
            requiredAttributes: new[] { "distinguishedName", "objectClass", "userAccountControl", "pwdLastSet" });

        if (!LdapToolSearchFacetsService.TryExecute(
                request: new LdapToolSearchFacetsQueryRequest {
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    LdapFilter = ldapFilter,
                    Kind = kind,
                    SearchText = searchText,
                    Scope = scope,
                    Attributes = attributes,
                    MaxValuesPerAttribute = maxValuesPerAttribute,
                    PageSize = pageSize,
                    MaxPages = maxPages,
                    MaxResults = maxResults,
                    MaxFacetValues = maxFacetValues,
                    FacetByContainer = facetByContainer,
                    ContainerFacetMode = containerFacetMode,
                    ContainerOuDepth = containerOuDepth,
                    FacetByEnabled = facetByEnabled,
                    UacFlags = uacFlags,
                    PasswordAgeBucketsDays = pwdBuckets,
                    IncludeSamples = includeSamples,
                    SampleSize = sampleSize,
                    Timeout = timeout
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var facts = new[] {
            ("Scanned", queryResult!.CountScanned.ToString()),
            ("Truncated", queryResult.IsTruncated ? "true" : "false"),
            ("Pages read", queryResult.PagesRead.ToString()),
            ("Max results", queryResult.MaxResults.ToString())
        };

        var meta = ToolOutputHints.Meta(count: 1, truncated: queryResult.IsTruncated, scanned: null, previewCount: facts.Length);

        return Task.FromResult(ToolResponse.OkFactsModel(
            model: queryResult,
            title: "Active Directory: Search Facets",
            facts: facts,
            meta: meta,
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: queryResult.IsTruncated,
            render: null));
    }
}


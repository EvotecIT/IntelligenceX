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
/// Lists group members with resolved object details in a single LDAP query (read-only).
/// </summary>
public sealed class AdGroupMembersResolvedTool : ActiveDirectoryToolBase, ITool {
    private static readonly string[] DefaultAttributes = {
        "distinguishedName",
        "objectClass",
        "cn",
        "name",
        "sAMAccountName",
        "userPrincipalName",
        "dNSHostName"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase) {
        "distinguishedName",
        "objectClass",
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
        "managedBy"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_group_members_resolved",
        "Get members of an Active Directory group (read-only) with resolved object details in one call. Prefer this over ad_group_members+ad_object_get loops.",
        ToolSchema.Object(
                ("identity", ToolSchema.String("Group identity (DN, samAccountName, cn, or name).")),
                ("search_base_dn", ToolSchema.String("Optional base DN override for searching the group and members (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("include_nested", ToolSchema.Boolean("When true, include nested members using LDAP_MATCHING_RULE_IN_CHAIN. Default false.")),
                ("max_results", ToolSchema.Integer("Maximum members to return (capped).")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional member attributes to include (allowlist enforced).")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")))
            .WithTableViewOptions()
            .Required("identity")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGroupMembersResolvedTool"/> class.
    /// </summary>
    public AdGroupMembersResolvedTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = ToolArgs.GetOptionalTrimmed(arguments, "identity") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identity)) {
            return Task.FromResult(Error("identity is required."));
        }

        var maxResults = ResolveBoundedMaxResults(arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);

        var includeNested = ToolArgs.GetBoolean(arguments, "include_nested");

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
        LdapQueryPolicy.EnsureIncluded(attributes, "distinguishedName");
        LdapQueryPolicy.EnsureIncluded(attributes, "objectClass");

        if (!AdGroupMembersResolvedService.TryQuery(
                options: new AdGroupMembersResolvedService.GroupMembersResolvedQueryOptions {
                    Identity = identity,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    IncludeNested = includeNested,
                    MaxResults = maxResults,
                    MaxValuesPerAttribute = maxValuesPerAttribute,
                    Attributes = attributes
                },
                status: out var status,
                result: out var root,
                failure: out var groupFailure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(groupFailure));
        }

        if (status == AdGroupLookupStatus.NotFound) {
            return Task.FromResult(Error("Group not found."));
        }

        if (status == AdGroupLookupStatus.Ambiguous) {
            return Task.FromResult(Error("invalid_argument", "Multiple groups matched identity. Provide a more specific identity or search_base_dn."));
        }

        if (status == AdGroupLookupStatus.MissingDistinguishedName) {
            return Task.FromResult(Error("Failed to read group distinguishedName."));
        }

        if (root is null) {
            return Task.FromResult(Error("exception", "Group resolved-members query returned no result."));
        }

        AdDynamicTableView.TryBuildResponseFromOutputRows(
            arguments: arguments,
            model: root,
            rows: root.Results,
            title: "Active Directory: Group Members Resolved (preview)",
            rowsPath: "results_view",
            baseTruncated: root.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }
}


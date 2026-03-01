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
    private sealed record GroupMembersResolvedRequest(
        string Identity,
        bool IncludeNested,
        int MaxValuesPerAttribute,
        IReadOnlyList<string> RequestedAttributes);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<GroupMembersResolvedRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("identity", out var identity, out var identityError)) {
                return ToolRequestBindingResult<GroupMembersResolvedRequest>.Failure(identityError, errorCode: "error");
            }

            return ToolRequestBindingResult<GroupMembersResolvedRequest>.Success(new GroupMembersResolvedRequest(
                Identity: identity,
                IncludeNested: reader.Boolean("include_nested"),
                MaxValuesPerAttribute: reader.CappedInt32(
                    "max_values_per_attribute",
                    LdapQueryPolicy.DefaultMaxValuesPerAttribute,
                    1,
                    LdapQueryPolicy.MaxValuesPerAttributeCap),
                RequestedAttributes: reader.StringArray("attributes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GroupMembersResolvedRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var maxResults = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        var attributes = FilterAllowedStrings(request.RequestedAttributes, AllowedAttributes);
        if (attributes.Count == 0) {
            attributes.AddRange(DefaultAttributes);
        }
        LdapQueryPolicy.EnsureIncluded(attributes, "distinguishedName");
        LdapQueryPolicy.EnsureIncluded(attributes, "objectClass");

        if (!AdGroupMembersResolvedService.TryQuery(
                options: new AdGroupMembersResolvedService.GroupMembersResolvedQueryOptions {
                    Identity = request.Identity,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    IncludeNested = request.IncludeNested,
                    MaxResults = maxResults,
                    MaxValuesPerAttribute = request.MaxValuesPerAttribute,
                    Attributes = attributes
                },
                status: out var status,
                result: out var root,
                failure: out var groupFailure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(groupFailure));
        }

        if (status == AdGroupLookupStatus.NotFound) {
            return Task.FromResult(ToolResultV2.Error("error", "Group not found."));
        }

        if (status == AdGroupLookupStatus.Ambiguous) {
            return Task.FromResult(ToolResultV2.Error("invalid_argument", "Multiple groups matched identity. Provide a more specific identity or search_base_dn."));
        }

        if (status == AdGroupLookupStatus.MissingDistinguishedName) {
            return Task.FromResult(ToolResultV2.Error("error", "Failed to read group distinguishedName."));
        }

        if (root is null) {
            return Task.FromResult(ToolResultV2.Error("exception", "Group resolved-members query returned no result."));
        }

        if (!AdDynamicTableView.TryBuildResponseFromOutputRows(
                arguments: context.Arguments,
                model: root,
                rows: root.Results,
                title: "Active Directory: Group Members Resolved (preview)",
                rowsPath: "results_view",
                baseTruncated: root.IsTruncated,
                response: out var response)) {
            return Task.FromResult(ToolResultV2.Error("query_failed", "Failed to build resolved group-members table view response."));
        }

        return Task.FromResult(response);
    }

    private static List<string> FilterAllowedStrings(IReadOnlyList<string> values, IReadOnlySet<string> allowedValues) {
        var normalized = new List<string>(values.Count);
        for (var i = 0; i < values.Count; i++) {
            var value = values[i];
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            var trimmed = value.Trim();
            if (allowedValues.Contains(trimmed) && !normalized.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }
}

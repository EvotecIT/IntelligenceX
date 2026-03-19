using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists a user's direct or recursive group memberships with resolved group details in one call (read-only).
/// </summary>
public sealed class AdUserGroupsResolvedTool : ActiveDirectoryToolBase, ITool {
    private sealed record UserGroupsResolvedRequest(
        string Identity,
        string? DomainName,
        bool IncludeRecursive,
        int MaxResults,
        IReadOnlyList<string> RequestedAttributes);

    private sealed record UserGroupsResolvedResult(
        string Identity,
        string DomainName,
        string DistinguishedName,
        string SamAccountName,
        string UserPrincipalName,
        bool IncludeRecursive,
        int Count,
        bool IsTruncated,
        IReadOnlyList<string> Attributes,
        IReadOnlyList<LdapToolOutputRow> Results);

    private static readonly string[] DefaultAttributes = {
        "distinguishedName",
        "objectClass",
        "cn",
        "name",
        "sAMAccountName",
        "displayName",
        "mail"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase) {
        "distinguishedName",
        "objectClass",
        "cn",
        "name",
        "sAMAccountName",
        "displayName",
        "mail",
        "description",
        "managedBy",
        "whenCreated",
        "whenChanged",
        "groupType",
        "memberOf"
    };

    private const int MaxResultsCap = 1000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_user_groups_resolved",
        "Get a user's group memberships (read-only) with resolved group details in one call. Use this for access-footprint verification after user lifecycle changes.",
        ToolSchema.Object(
                ("identity", ToolSchema.String("User identity (DN, samAccountName, UPN, mail, or name).")),
                ("domain_name", ToolSchema.String("Optional domain DNS name override.")),
                ("include_recursive", ToolSchema.Boolean("When true, include recursively resolved parent groups. Default false.")),
                ("max_results", ToolSchema.Integer("Maximum groups to return (capped).")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional group attributes to include (allowlist enforced).")))
            .WithTableViewOptions()
            .Required("identity")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdUserGroupsResolvedTool"/> class.
    /// </summary>
    public AdUserGroupsResolvedTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<UserGroupsResolvedRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("identity", out var identity, out var identityError)) {
                return ToolRequestBindingResult<UserGroupsResolvedRequest>.Failure(identityError);
            }

            var requestedMaxResults = reader.OptionalInt64("max_results");
            var maxResults = requestedMaxResults.HasValue && requestedMaxResults.Value > 0
                ? (int)Math.Min(requestedMaxResults.Value, MaxResultsCap)
                : 200;

            return ToolRequestBindingResult<UserGroupsResolvedRequest>.Success(new UserGroupsResolvedRequest(
                Identity: identity,
                DomainName: reader.OptionalString("domain_name"),
                IncludeRecursive: reader.Boolean("include_recursive"),
                MaxResults: maxResults,
                RequestedAttributes: reader.StringArray("attributes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<UserGroupsResolvedRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;

        var requestedAttributes = FilterAllowedStrings(request.RequestedAttributes, AllowedAttributes);
        if (requestedAttributes.Count == 0) {
            requestedAttributes.AddRange(DefaultAttributes);
        }

        var helper = new DirectoryObjectHelper();
        var membershipSnapshot = helper.GetUserGroups(
            identity: request.Identity,
            domainName: request.DomainName,
            recursive: request.IncludeRecursive);

        var rows = BuildResolvedGroupRows(
            helper,
            membershipSnapshot,
            membershipSnapshot.DomainName,
            request.IncludeRecursive,
            request.MaxResults,
            requestedAttributes,
            out var isTruncated);

        var result = new UserGroupsResolvedResult(
            Identity: membershipSnapshot.Identity,
            DomainName: membershipSnapshot.DomainName,
            DistinguishedName: membershipSnapshot.DistinguishedName,
            SamAccountName: membershipSnapshot.SamAccountName,
            UserPrincipalName: membershipSnapshot.UserPrincipalName,
            IncludeRecursive: request.IncludeRecursive,
            Count: rows.Count,
            IsTruncated: isTruncated,
            Attributes: requestedAttributes,
            Results: rows);

        if (!AdDynamicTableView.TryBuildResponseFromOutputRows(
                arguments: context.Arguments,
                model: result,
                rows: rows,
                title: "Active Directory: User Groups Resolved (preview)",
                rowsPath: "results_view",
                baseTruncated: isTruncated,
                response: out var response,
                scanned: rows.Count)) {
            return Task.FromResult(ToolResultV2.Error("query_failed", "Failed to build resolved user-groups table view response."));
        }

        return Task.FromResult(response);
    }

    private static IReadOnlyList<LdapToolOutputRow> BuildResolvedGroupRows(
        DirectoryObjectHelper helper,
        DirectoryUserGroupsSnapshot membershipSnapshot,
        string? domainName,
        bool includeRecursive,
        int maxResults,
        IReadOnlyList<string> attributes,
        out bool isTruncated) {
        var rows = new List<LdapToolOutputRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendDirectGroups(rows, seen, helper, membershipSnapshot, domainName, attributes, maxResults);
        if (includeRecursive) {
            AppendRecursiveGroups(rows, seen, helper, membershipSnapshot, domainName, attributes, maxResults);
        }

        isTruncated = rows.Count >= maxResults
            && ((includeRecursive && membershipSnapshot.RecursiveGroups.Count > rows.Count)
                || membershipSnapshot.DirectGroups.Count > rows.Count);
        return rows;
    }

    private static void AppendDirectGroups(
        ICollection<LdapToolOutputRow> rows,
        ISet<string> seen,
        DirectoryObjectHelper helper,
        DirectoryUserGroupsSnapshot membershipSnapshot,
        string? domainName,
        IReadOnlyList<string> attributes,
        int maxResults) {
        for (var i = 0; i < membershipSnapshot.DirectGroups.Count; i++) {
            if (rows.Count >= maxResults) {
                return;
            }

            var distinguishedName = membershipSnapshot.DirectGroups[i];
            if (string.IsNullOrWhiteSpace(distinguishedName) || !seen.Add(distinguishedName.Trim())) {
                continue;
            }

            var snapshot = helper.GetGroup(distinguishedName, domainName, attributes);
            rows.Add(CreateRow(snapshot, isDirectMembership: true, nesting: 1));
        }
    }

    private static void AppendRecursiveGroups(
        ICollection<LdapToolOutputRow> rows,
        ISet<string> seen,
        DirectoryObjectHelper helper,
        DirectoryUserGroupsSnapshot membershipSnapshot,
        string? domainName,
        IReadOnlyList<string> attributes,
        int maxResults) {
        for (var i = 0; i < membershipSnapshot.RecursiveGroups.Count; i++) {
            if (rows.Count >= maxResults) {
                return;
            }

            var group = membershipSnapshot.RecursiveGroups[i];
            if (!string.Equals(group.ObjectType.ToString(), "Group", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(group.DistinguishedName)
                || !seen.Add(group.DistinguishedName.Trim())) {
                continue;
            }

            var snapshot = helper.GetGroup(group.DistinguishedName, domainName, attributes);
            rows.Add(CreateRow(snapshot, isDirectMembership: false, nesting: Math.Max(1, group.Nesting)));
        }
    }

    private static LdapToolOutputRow CreateRow(DirectoryObjectSnapshot snapshot, bool isDirectMembership, int nesting) {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["distinguishedName"] = snapshot.DistinguishedName,
            ["objectClass"] = "group",
            ["cn"] = snapshot.Name,
            ["name"] = snapshot.Name,
            ["sAMAccountName"] = snapshot.SamAccountName,
            ["displayName"] = snapshot.DisplayName,
            ["mail"] = snapshot.Mail,
            ["description"] = snapshot.Description,
            ["managedBy"] = snapshot.ManagedBy,
            ["groupType"] = snapshot.GroupType,
            ["memberOf"] = snapshot.MemberOf,
            ["direct_membership"] = isDirectMembership,
            ["nesting"] = nesting
        };

        if (snapshot.WhenCreated.HasValue) {
            attributes["whenCreated"] = snapshot.WhenCreated.Value;
        }

        if (snapshot.WhenChanged.HasValue) {
            attributes["whenChanged"] = snapshot.WhenChanged.Value;
        }

        return new LdapToolOutputRow {
            Attributes = attributes
                .Where(static pair => pair.Value is not null
                                      && (!(pair.Value is string stringValue) || !string.IsNullOrWhiteSpace(stringValue)))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
        };
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

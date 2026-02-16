using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Reads group members from Active Directory (read-only).
/// </summary>
public sealed class AdGroupMembersTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_group_members",
        "Get members of an Active Directory group (read-only). Returns member distinguished names.",
        ToolSchema.Object(
                ("identity", ToolSchema.String("Group identity (DN, samAccountName, cn, or name).")),
                ("search_base_dn", ToolSchema.String("Optional base DN override.")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_members", ToolSchema.Integer("Maximum members to return (capped).")))
            .Required("identity")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGroupMembersTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public AdGroupMembersTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var identity = ToolArgs.GetOptionalTrimmed(arguments, "identity") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identity)) {
            return Task.FromResult(Error("identity is required."));
        }

        var requestedMax = arguments?.GetInt64("max_members");
        var maxMembers = requestedMax.HasValue && requestedMax.Value > 0
            ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
            : Options.MaxResults;

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        if (!AdGroupMembersService.TryQuery(
                options: new AdGroupMembersService.GroupMembersQueryOptions {
                    Identity = identity,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxMembers = maxMembers
                },
                status: out var status,
                result: out var output,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        if (status == AdGroupLookupStatus.NotFound) {
            return Task.FromResult(Error("Group not found."));
        }

        if (status == AdGroupLookupStatus.Ambiguous) {
            return Task.FromResult(Error("invalid_argument", "Multiple groups matched identity. Provide a more specific identity or search_base_dn."));
        }

        if (output is null) {
            return Task.FromResult(Error("exception", "Group member query returned no result."));
        }

        return Task.FromResult(ToolResponse.OkModel(output));
    }
}

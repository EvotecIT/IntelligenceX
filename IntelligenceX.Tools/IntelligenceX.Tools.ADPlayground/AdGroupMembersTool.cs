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
    private sealed record GroupMembersRequest(
        string Identity,
        int MaxMembers);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<GroupMembersRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("identity", out var identity, out var identityError)) {
                return ToolRequestBindingResult<GroupMembersRequest>.Failure(identityError, errorCode: "error");
            }

            var requestedMax = reader.OptionalInt64("max_members");
            var maxMembers = requestedMax.HasValue && requestedMax.Value > 0
                ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
                : Options.MaxResults;

            return ToolRequestBindingResult<GroupMembersRequest>.Success(new GroupMembersRequest(
                Identity: identity,
                MaxMembers: maxMembers));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GroupMembersRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        if (!AdGroupMembersService.TryQuery(
                options: new AdGroupMembersService.GroupMembersQueryOptions {
                    Identity = request.Identity,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxMembers = request.MaxMembers
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
            return Task.FromResult(ToolResultV2.Error("exception", "Group member query returned no result."));
        }

        return Task.FromResult(ToolResultV2.OkModel(output));
    }
}

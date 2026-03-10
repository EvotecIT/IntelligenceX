using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.Identity;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Returns local user/group inventory for the local or remote Windows host.
/// </summary>
public sealed class SystemLocalIdentityInventoryTool : SystemToolBase, ITool {
    private sealed record LocalIdentityInventoryRequest(
        string? ComputerName,
        string Target,
        bool IncludeGroupMembers,
        bool OnlyPrivilegedGroups,
        IReadOnlyList<string> PrivilegedGroupNames,
        int MaxEntries);

    private sealed record LocalIdentityViewRow(
        string RowType,
        string Name,
        string Domain,
        string? Sid,
        string? GroupName,
        bool? IsPrivilegedGroup,
        bool? IsNestedGroup,
        string? SourceClass,
        string? FullName,
        string? Description,
        bool? Disabled,
        bool? PasswordRequired,
        bool? PasswordExpires,
        bool? Lockout,
        string? Status);

    private sealed record LocalIdentityInventoryResponse(
        string ComputerName,
        DateTime CollectedAtUtc,
        int UserCount,
        int GroupCount,
        int GroupMemberCount,
        IReadOnlyList<LocalUserAccountInfo> Users,
        IReadOnlyList<LocalGroupInfo> Groups);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "system_local_identity_inventory",
        "Return local user/group inventory for the local or remote Windows host.",
        ToolSchema.Object(
                ("computer_name", ToolSchema.String("Optional remote computer name. Omit for local machine.")),
                ("include_group_members", ToolSchema.Boolean("When true, include group member enumeration. Default true.")),
                ("only_privileged_groups", ToolSchema.Boolean("When true, return only privileged groups. Default false.")),
                ("privileged_group_names", ToolSchema.Array(ToolSchema.String(), "Optional privileged group names override.")),
                ("max_entries", ToolSchema.Integer("Optional maximum flattened rows to return in the preview view (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemLocalIdentityInventoryTool"/> class.
    /// </summary>
    public SystemLocalIdentityInventoryTool(SystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<LocalIdentityInventoryRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var computerName = reader.OptionalString("computer_name");
            var privilegedGroupNames = reader.DistinctStringArray("privileged_group_names");
            return ToolRequestBindingResult<LocalIdentityInventoryRequest>.Success(new LocalIdentityInventoryRequest(
                ComputerName: computerName,
                Target: ResolveTargetComputerName(computerName),
                IncludeGroupMembers: reader.Boolean("include_group_members", defaultValue: true),
                OnlyPrivilegedGroups: reader.Boolean("only_privileged_groups", defaultValue: false),
                PrivilegedGroupNames: privilegedGroupNames,
                MaxEntries: ResolveBoundedOptionLimit(arguments, "max_entries")));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<LocalIdentityInventoryRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var windowsError = ValidateWindowsSupport("system_local_identity_inventory");
        if (windowsError is not null) {
            return windowsError;
        }

        var request = context.Request;
        var attempt = await LocalIdentityInventoryQueryExecutor.TryExecuteAsync(
                request: new LocalIdentityInventoryQueryRequest {
                    ComputerName = request.ComputerName,
                    IncludeGroupMembers = request.IncludeGroupMembers,
                    IncludeOnlyPrivilegedGroups = request.OnlyPrivilegedGroups,
                    PrivilegedGroupNames = request.PrivilegedGroupNames
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!attempt.Success) {
            return ErrorFromFailure(
                attempt.Failure,
                static x => x.Code,
                static x => x.Message,
                defaultMessage: "Local identity inventory query failed.");
        }

        var result = attempt.Result ?? new LocalIdentityInventoryQueryResult();
        var effectiveComputerName = string.IsNullOrWhiteSpace(result.ComputerName) ? request.Target : result.ComputerName;
        var groupMemberCount = result.Groups.Sum(static group => group.Members?.Count ?? 0);
        var model = new LocalIdentityInventoryResponse(
            ComputerName: effectiveComputerName,
            CollectedAtUtc: result.CollectedAtUtc,
            UserCount: result.Users.Count,
            GroupCount: result.Groups.Count,
            GroupMemberCount: groupMemberCount,
            Users: result.Users,
            Groups: result.Groups);
        var viewRows = BuildViewRows(result, request.MaxEntries, out var truncated);
        var scanned = result.Users.Count + result.Groups.Count + groupMemberCount;

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: model,
            sourceRows: viewRows,
            viewRowsPath: "identity_view",
            title: "Local identity inventory (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddComputerNameMeta(meta, effectiveComputerName);
                AddMaxResultsMeta(meta, request.MaxEntries);
                meta.Add("include_group_members", request.IncludeGroupMembers);
                meta.Add("only_privileged_groups", request.OnlyPrivilegedGroups);
                if (request.PrivilegedGroupNames.Count > 0) {
                    meta.Add("privileged_group_names", string.Join(", ", request.PrivilegedGroupNames));
                }

                AddReadOnlyPostureChainingMeta(
                    meta: meta,
                    currentTool: "system_local_identity_inventory",
                    targetComputer: effectiveComputerName,
                    isRemoteScope: !IsLocalTarget(request.ComputerName, request.Target),
                    scanned: scanned,
                    truncated: truncated);
            });

        return response;
    }

    private static IReadOnlyList<LocalIdentityViewRow> BuildViewRows(LocalIdentityInventoryQueryResult result, int maxEntries, out bool truncated) {
        var rows = new List<LocalIdentityViewRow>();

        foreach (var user in result.Users) {
            rows.Add(new LocalIdentityViewRow(
                RowType: "user",
                Name: user.Name,
                Domain: user.Domain,
                Sid: user.Sid,
                GroupName: null,
                IsPrivilegedGroup: null,
                IsNestedGroup: null,
                SourceClass: null,
                FullName: user.FullName,
                Description: user.Description,
                Disabled: user.Disabled,
                PasswordRequired: user.PasswordRequired,
                PasswordExpires: user.PasswordExpires,
                Lockout: user.Lockout,
                Status: user.Status));
        }

        foreach (var group in result.Groups) {
            rows.Add(new LocalIdentityViewRow(
                RowType: "group",
                Name: group.Name,
                Domain: group.Domain,
                Sid: group.Sid,
                GroupName: null,
                IsPrivilegedGroup: group.IsPrivilegedGroup,
                IsNestedGroup: null,
                SourceClass: null,
                FullName: null,
                Description: group.Description,
                Disabled: null,
                PasswordRequired: null,
                PasswordExpires: null,
                Lockout: null,
                Status: null));

            foreach (var member in group.Members) {
                rows.Add(new LocalIdentityViewRow(
                    RowType: "group_member",
                    Name: member.Name,
                    Domain: member.Domain,
                    Sid: member.Sid,
                    GroupName: group.Name,
                    IsPrivilegedGroup: group.IsPrivilegedGroup,
                    IsNestedGroup: member.IsGroup,
                    SourceClass: member.SourceClass,
                    FullName: null,
                    Description: null,
                    Disabled: null,
                    PasswordRequired: null,
                    PasswordExpires: null,
                    Lockout: null,
                    Status: null));
            }
        }

        return CapRows(rows, maxEntries, out _, out truncated);
    }
}

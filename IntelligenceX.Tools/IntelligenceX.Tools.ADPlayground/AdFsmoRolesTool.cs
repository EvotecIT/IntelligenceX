using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Domains;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns FSMO role holders and best-practice checks (read-only).
/// </summary>
public sealed class AdFsmoRolesTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_fsmo_roles",
        "Get FSMO role holders and evaluate common role placement best practices (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When omitted, uses current domain context for domain roles.")),
                ("include_best_practices", ToolSchema.Boolean("When true, include best-practice evaluation rows. Default true.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record FsmoRoleRow(
        string Role,
        string ServerName,
        string Site,
        bool IsOnline,
        bool IsWritable);

    private sealed record FsmoCheckRow(
        string CheckType,
        bool Passed,
        string Description);

    private sealed record AdFsmoRolesResult(
        string? DomainName,
        bool IncludeBestPractices,
        int RoleHolderCount,
        int BestPracticeCount,
        IReadOnlyList<FsmoRoleRow> RoleHolders,
        IReadOnlyList<FsmoCheckRow> BestPracticeChecks);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdFsmoRolesTool"/> class.
    /// </summary>
    public AdFsmoRolesTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var includeBestPractices = ToolArgs.GetBoolean(arguments, "include_best_practices", defaultValue: true);

        var checker = new FsmoRoleChecker();
        if (!TryExecute(
                action: () => {
                    var holders = checker.GetRoleHolders(domainName).ToArray();
                    var checks = includeBestPractices
                        ? checker.CheckBestPractices(domainName).ToArray()
                        : Array.Empty<FsmoBestPracticeCheck>();
                    return (Holders: (IReadOnlyList<FsmoRoleHolder>)holders, Checks: (IReadOnlyList<FsmoBestPracticeCheck>)checks);
                },
                result: out var query,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "FSMO query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }
        var holders = query.Holders;
        var checks = query.Checks;

        var roleRows = holders
            .Select(static holder => new FsmoRoleRow(
                Role: holder.Role.ToString(),
                ServerName: holder.ServerName,
                Site: holder.Site,
                IsOnline: holder.IsOnline,
                IsWritable: holder.IsWritable))
            .ToArray();

        var checkRows = checks
            .Select(static check => new FsmoCheckRow(
                CheckType: check.Type.ToString(),
                Passed: check.Passed,
                Description: check.Description))
            .ToArray();

        var result = new AdFsmoRolesResult(
            DomainName: domainName,
            IncludeBestPractices: includeBestPractices,
            RoleHolderCount: roleRows.Length,
            BestPracticeCount: checkRows.Length,
            RoleHolders: roleRows,
            BestPracticeChecks: checkRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: roleRows,
            viewRowsPath: "roles_view",
            title: "Active Directory: FSMO Roles (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            scanned: roleRows.Length,
            metaMutate: meta => {
                meta.Add("include_best_practices", includeBestPractices);
                meta.Add("best_practice_count", checkRows.Length);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
            }));
    }
}


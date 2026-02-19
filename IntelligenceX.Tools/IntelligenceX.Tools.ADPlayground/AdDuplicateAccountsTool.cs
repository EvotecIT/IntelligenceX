using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.DirectoryServices;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns duplicate-account posture (CNF conflicts and duplicate sAMAccountName values) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDuplicateAccountsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_duplicate_accounts",
        "Check duplicate-account posture (conflict objects and duplicate sAMAccountName values) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("include_conflict_dns", ToolSchema.Boolean("When true, include conflict-object DN rows in details payload.")),
                ("include_duplicate_details", ToolSchema.Boolean("When true, include duplicate sAMAccountName detail rows.")),
                ("conflicts_only", ToolSchema.Boolean("When true, return only domains where conflict objects were found.")),
                ("duplicates_only", ToolSchema.Boolean("When true, return only domains with duplicate sAMAccountName values.")),
                ("max_detail_rows_per_domain", ToolSchema.Integer("Maximum detail rows per domain. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DuplicateAccountsRow(
        string DomainName,
        int ConflictObjectCount,
        int DuplicateSamCount,
        bool AnyFinding);

    private sealed record DuplicateAccountsConflictDetailRow(
        string DomainName,
        string DistinguishedName);

    private sealed record DuplicateAccountsDuplicateDetailRow(
        string DomainName,
        string SamAccountName,
        int EntryCount,
        IReadOnlyList<string> DistinguishedNames,
        IReadOnlyList<string> ObjectClasses);

    private sealed record DuplicateAccountsError(
        string Domain,
        string Message);

    private sealed record AdDuplicateAccountsResult(
        string? DomainName,
        string? ForestName,
        bool IncludeConflictDns,
        bool IncludeDuplicateDetails,
        bool ConflictsOnly,
        bool DuplicatesOnly,
        int MaxDetailRowsPerDomain,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DuplicateAccountsError> Errors,
        IReadOnlyList<DuplicateAccountsRow> Rows,
        IReadOnlyList<DuplicateAccountsConflictDetailRow> ConflictDetails,
        IReadOnlyList<DuplicateAccountsDuplicateDetailRow> DuplicateDetails);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDuplicateAccountsTool"/> class.
    /// </summary>
    public AdDuplicateAccountsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var includeConflictDns = ToolArgs.GetBoolean(arguments, "include_conflict_dns", defaultValue: false);
        var includeDuplicateDetails = ToolArgs.GetBoolean(arguments, "include_duplicate_details", defaultValue: false);
        var conflictsOnly = ToolArgs.GetBoolean(arguments, "conflicts_only", defaultValue: false);
        var duplicatesOnly = ToolArgs.GetBoolean(arguments, "duplicates_only", defaultValue: false);
        var maxDetailRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_detail_rows_per_domain", 100, 1, 5000);
        var maxResults = ResolveBoundedMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "duplicate-account",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<DuplicateAccountsRow>(targetDomains.Length);
        var conflictDetails = new List<DuplicateAccountsConflictDetailRow>(targetDomains.Length * 4);
        var duplicateDetails = new List<DuplicateAccountsDuplicateDetailRow>(targetDomains.Length * 4);
        var errors = new List<DuplicateAccountsError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var view = DuplicateAccountService.Evaluate(domain);
                rows.Add(new DuplicateAccountsRow(
                    DomainName: view.DomainName,
                    ConflictObjectCount: view.ConflictObjectCount,
                    DuplicateSamCount: view.DuplicateSamCount,
                    AnyFinding: view.ConflictObjectCount > 0 || view.DuplicateSamCount > 0));

                if (includeConflictDns) {
                    foreach (var dn in view.ConflictObjects.Take(maxDetailRowsPerDomain)) {
                        conflictDetails.Add(new DuplicateAccountsConflictDetailRow(
                            DomainName: view.DomainName,
                            DistinguishedName: dn));
                    }
                }

                if (includeDuplicateDetails) {
                    foreach (var duplicate in view.Duplicates.Take(maxDetailRowsPerDomain)) {
                        duplicateDetails.Add(new DuplicateAccountsDuplicateDetailRow(
                            DomainName: view.DomainName,
                            SamAccountName: duplicate.SamAccountName,
                            EntryCount: duplicate.DistinguishedNames.Length,
                            DistinguishedNames: duplicate.DistinguishedNames,
                            ObjectClasses: duplicate.ObjectClasses));
                    }
                }
            },
            errorFactory: (domain, ex) => new DuplicateAccountsError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !conflictsOnly || row.ConflictObjectCount > 0)
            .Where(row => !duplicatesOnly || row.DuplicateSamCount > 0)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedConflictDetails = FilterByProjectedSet(conflictDetails, projectedDomains, static detail => detail.DomainName);
        var projectedDuplicateDetails = FilterByProjectedSet(duplicateDetails, projectedDomains, static detail => detail.DomainName);

        var result = new AdDuplicateAccountsResult(
            DomainName: domainName,
            ForestName: forestName,
            IncludeConflictDns: includeConflictDns,
            IncludeDuplicateDetails: includeDuplicateDetails,
            ConflictsOnly: conflictsOnly,
            DuplicatesOnly: duplicatesOnly,
            MaxDetailRowsPerDomain: maxDetailRowsPerDomain,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            ConflictDetails: projectedConflictDetails,
            DuplicateDetails: projectedDuplicateDetails);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Duplicate Accounts (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("include_conflict_dns", includeConflictDns);
                meta.Add("include_duplicate_details", includeDuplicateDetails);
                meta.Add("conflicts_only", conflictsOnly);
                meta.Add("duplicates_only", duplicatesOnly);
                meta.Add("max_detail_rows_per_domain", maxDetailRowsPerDomain);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}


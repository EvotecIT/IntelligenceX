using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.DirectoryOps;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns OU accidental-deletion protection posture for one domain or forest scope (read-only).
/// </summary>
public sealed class AdOuProtectionTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_ou_protection",
        "Check organizational-unit accidental-deletion protection posture for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("unprotected_only", ToolSchema.Boolean("When true, return only domains with unprotected OUs.")),
                ("include_unprotected_ous", ToolSchema.Boolean("When true, include unprotected OU detail rows.")),
                ("max_ou_rows_per_domain", ToolSchema.Integer("Maximum unprotected OU rows per domain. Default 100.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record OuProtectionRow(
        string DomainName,
        int OuCount,
        int ProtectedCount,
        int UnprotectedCount,
        double ProtectedPercent,
        bool AnyFinding);

    private sealed record OuProtectionDetailRow(
        string DomainName,
        string Name,
        string DistinguishedName);

    private sealed record OuProtectionError(
        string Domain,
        string Message);

    private sealed record AdOuProtectionResult(
        string? DomainName,
        string? ForestName,
        bool UnprotectedOnly,
        bool IncludeUnprotectedOus,
        int MaxOuRowsPerDomain,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<OuProtectionError> Errors,
        IReadOnlyList<OuProtectionRow> Rows,
        IReadOnlyList<OuProtectionDetailRow> Details);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdOuProtectionTool"/> class.
    /// </summary>
    public AdOuProtectionTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var unprotectedOnly = ToolArgs.GetBoolean(arguments, "unprotected_only", defaultValue: false);
        var includeUnprotectedOus = ToolArgs.GetBoolean(arguments, "include_unprotected_ous", defaultValue: false);
        var maxOuRowsPerDomain = ToolArgs.GetCappedInt32(arguments, "max_ou_rows_per_domain", 100, 1, 5000);
        var maxResults = ResolveMaxResultsClampToOne(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "OU protection",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<OuProtectionRow>(targetDomains.Length);
        var details = new List<OuProtectionDetailRow>(targetDomains.Length * 4);
        var errors = new List<OuProtectionError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var snapshot = OuProtectionService.GetSnapshot(domain);
                var total = snapshot.Ous.Count;
                var unprotected = snapshot.Ous
                    .Where(static ou => !ou.ProtectedFromAccidentalDeletion)
                    .ToArray();
                var protectedCount = total - unprotected.Length;
                var protectedPercent = total == 0
                    ? 100.0
                    : Math.Round((double)protectedCount * 100.0 / total, 2, MidpointRounding.AwayFromZero);

                rows.Add(new OuProtectionRow(
                    DomainName: snapshot.DomainName,
                    OuCount: total,
                    ProtectedCount: protectedCount,
                    UnprotectedCount: unprotected.Length,
                    ProtectedPercent: protectedPercent,
                    AnyFinding: unprotected.Length > 0));

                if (includeUnprotectedOus) {
                    foreach (var ou in unprotected.Take(maxOuRowsPerDomain)) {
                        details.Add(new OuProtectionDetailRow(
                            DomainName: snapshot.DomainName,
                            Name: ou.Name,
                            DistinguishedName: ou.DistinguishedName));
                    }
                }
            },
            errorFactory: (domain, ex) => new OuProtectionError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !unprotectedOnly || row.UnprotectedCount > 0)
            .ToArray();

        var projectedRows = CapRows(filtered, maxResults, out var scanned, out var truncated);
        var projectedDomains = BuildProjectedSet(projectedRows, static row => row.DomainName);
        var projectedDetails = FilterByProjectedSet(details, projectedDomains, static detail => detail.DomainName);

        var result = new AdOuProtectionResult(
            DomainName: domainName,
            ForestName: forestName,
            UnprotectedOnly: unprotectedOnly,
            IncludeUnprotectedOus: includeUnprotectedOus,
            MaxOuRowsPerDomain: maxOuRowsPerDomain,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows,
            Details: projectedDetails);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: OU Protection Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("unprotected_only", unprotectedOnly);
                meta.Add("include_unprotected_ous", includeUnprotectedOus);
                meta.Add("max_ou_rows_per_domain", maxOuRowsPerDomain);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}

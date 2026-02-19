using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.DomainControllers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Reports per-DC System State backup recency posture (read-only).
/// </summary>
public sealed class AdSystemStateBackupTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_system_state_backup",
        "Check domain controller System State backup recency and missing backup posture (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, checks one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("threshold_days", ToolSchema.Integer("Backup age threshold in days for stale signal. Default 30.")),
                ("missing_only", ToolSchema.Boolean("When true, return only domain controllers with no backup timestamp.")),
                ("stale_only", ToolSchema.Boolean("When true, return only rows where backup age exceeds threshold_days.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record SystemStateBackupRow(
        string DomainName,
        string DomainController,
        DateTime? LastBackup,
        int? AgeDays,
        bool IsMissing,
        bool IsStale,
        int ThresholdDays);

    private sealed record SystemStateBackupError(
        string Domain,
        string Message);

    private sealed record AdSystemStateBackupResult(
        string? DomainName,
        string? ForestName,
        int ThresholdDays,
        bool MissingOnly,
        bool StaleOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<SystemStateBackupError> Errors,
        IReadOnlyList<SystemStateBackupRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSystemStateBackupTool"/> class.
    /// </summary>
    public AdSystemStateBackupTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var thresholdDays = ToolArgs.GetCappedInt32(arguments, "threshold_days", 30, 1, 3650);
        var missingOnly = ToolArgs.GetBoolean(arguments, "missing_only", defaultValue: false);
        var staleOnly = ToolArgs.GetBoolean(arguments, "stale_only", defaultValue: false);
        var maxResults = ResolveMaxResultsClampToOne(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "System State backup",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<SystemStateBackupRow>(targetDomains.Length * 8);
        var errors = new List<SystemStateBackupError>();

        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var checker = new SystemStateBackupChecker(
                    thresholdDays: thresholdDays,
                    enumerateDcs: () => DomainHelper.EnumerateDomainControllers(domain, cancellationToken: cancellationToken));

                foreach (var status in checker.GetStatusReport()) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var ageDays = status.AgeDays;
                    var isMissing = !status.LastBackup.HasValue;
                    var isStale = ageDays.HasValue && ageDays.Value > thresholdDays;
                    rows.Add(new SystemStateBackupRow(
                        DomainName: domain,
                        DomainController: status.DomainController,
                        LastBackup: status.LastBackup,
                        AgeDays: ageDays,
                        IsMissing: isMissing,
                        IsStale: isStale,
                        ThresholdDays: thresholdDays));
                }
            },
            errorFactory: (domain, ex) => new SystemStateBackupError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !missingOnly || row.IsMissing)
            .Where(row => !staleOnly || row.IsStale)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<SystemStateBackupRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdSystemStateBackupResult(
            DomainName: domainName,
            ForestName: forestName,
            ThresholdDays: thresholdDays,
            MissingOnly: missingOnly,
            StaleOnly: staleOnly,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "backups_view",
            title: "Active Directory: System State Backup Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("threshold_days", thresholdDays);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("missing_only", missingOnly);
                meta.Add("stale_only", staleOnly);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}

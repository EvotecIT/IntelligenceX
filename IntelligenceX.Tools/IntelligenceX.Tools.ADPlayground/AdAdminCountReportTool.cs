using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists accounts with <c>adminCount=1</c> across discovered forests/domains (read-only).
/// </summary>
public sealed class AdAdminCountReportTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_admin_count_report",
        "List accounts with adminCount=1 across discovered forests/domains with optional text and staleness filters (read-only).",
        ToolSchema.Object(
                ("forest_name_contains", ToolSchema.String("Optional case-insensitive substring filter for forest name.")),
                ("domain_name_contains", ToolSchema.String("Optional case-insensitive substring filter for domain name.")),
                ("sam_account_name_contains", ToolSchema.String("Optional case-insensitive substring filter for sAMAccountName.")),
                ("stale_days", ToolSchema.Integer("Optional minimum days since most recent logon. Accounts with no logon are included when set.")),
                ("reference_time_utc", ToolSchema.String("Optional ISO-8601 UTC reference time used for stale_days calculations (default now).")),
                ("max_results", ToolSchema.Integer("Maximum account rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdminCountRow(
        string ForestName,
        string DomainName,
        string SamAccountName,
        DateTime? LastLogon,
        DateTime? LastLogonTimestamp,
        bool NeverLoggedOn,
        int? DaysSinceLastLogon);

    private sealed record AdAdminCountReportResult(
        string? ForestNameContains,
        string? DomainNameContains,
        string? SamAccountNameContains,
        int? StaleDays,
        DateTime ReferenceTimeUtc,
        int Scanned,
        bool Truncated,
        IReadOnlyList<AdminCountRow> Accounts);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdAdminCountReportTool"/> class.
    /// </summary>
    public AdAdminCountReportTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestNameContains = ToolArgs.GetOptionalTrimmed(arguments, "forest_name_contains");
        var domainNameContains = ToolArgs.GetOptionalTrimmed(arguments, "domain_name_contains");
        var samAccountNameContains = ToolArgs.GetOptionalTrimmed(arguments, "sam_account_name_contains");
        var staleDays = ToolArgs.ToPositiveInt32OrNull(arguments?.GetInt64("stale_days"));
        var maxResults = ResolveMaxResults(arguments);

        if (!ToolTime.TryParseUtcOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "reference_time_utc"),
                out var referenceTimeUtc,
                out var referenceTimeError)) {
            return Task.FromResult(Error("invalid_argument", $"reference_time_utc: {referenceTimeError}"));
        }

        var referenceUtc = referenceTimeUtc ?? DateTime.UtcNow;

        if (!TryExecute(
                action: static () => new AdminCountReporter().GetReport().ToArray(),
                result: out var reportRows,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "AdminCount report query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var projected = reportRows
            .Where(row => MatchesText(row.ForestName, forestNameContains))
            .Where(row => MatchesText(row.DomainName, domainNameContains))
            .Where(row => MatchesText(row.SamAccountName, samAccountNameContains))
            .Select(row => ProjectRow(row, referenceUtc))
            .Where(row => !staleDays.HasValue || row.NeverLoggedOn || (row.DaysSinceLastLogon.HasValue && row.DaysSinceLastLogon.Value >= staleDays.Value))
            .OrderByDescending(static row => row.DaysSinceLastLogon ?? int.MaxValue)
            .ThenBy(static row => row.ForestName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.DomainName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.SamAccountName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = CapRows(
            allRows: projected,
            maxResults: maxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdAdminCountReportResult(
            ForestNameContains: forestNameContains,
            DomainNameContains: domainNameContains,
            SamAccountNameContains: samAccountNameContains,
            StaleDays: staleDays,
            ReferenceTimeUtc: referenceUtc,
            Scanned: scanned,
            Truncated: truncated,
            Accounts: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "accounts_view",
            title: "Active Directory: AdminCount Report (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddOptionalStringMeta(meta, "forest_name_contains", forestNameContains);
                AddOptionalStringMeta(meta, "domain_name_contains", domainNameContains);
                AddOptionalStringMeta(meta, "sam_account_name_contains", samAccountNameContains);
                if (staleDays.HasValue) {
                    meta.Add("stale_days", staleDays.Value);
                }
                meta.Add("reference_time_utc", ToolTime.FormatUtc(referenceUtc));
                AddMaxResultsMeta(meta, maxResults);
            }));
    }

    private static AdminCountRow ProjectRow(AdminCountReportEntry source, DateTime referenceUtc) {
        var lastLogon = source.LastLogon?.ToUniversalTime();
        var lastLogonTimestamp = source.LastLogonTimestamp?.ToUniversalTime();
        var mostRecentLogon = MostRecent(lastLogon, lastLogonTimestamp);
        var daysSinceLastLogon = mostRecentLogon.HasValue
            ? Math.Max(0, (int)(referenceUtc - mostRecentLogon.Value).TotalDays)
            : (int?)null;

        return new AdminCountRow(
            ForestName: source.ForestName,
            DomainName: source.DomainName,
            SamAccountName: source.SamAccountName,
            LastLogon: lastLogon,
            LastLogonTimestamp: lastLogonTimestamp,
            NeverLoggedOn: !mostRecentLogon.HasValue,
            DaysSinceLastLogon: daysSinceLastLogon);
    }

    private static DateTime? MostRecent(DateTime? first, DateTime? second) {
        if (!first.HasValue) {
            return second;
        }
        if (!second.HasValue) {
            return first;
        }

        return first.Value >= second.Value ? first : second;
    }

    private static bool MatchesText(string? value, string? needle) {
        if (string.IsNullOrWhiteSpace(needle)) {
            return true;
        }

        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists accounts that appear to never have logged in for a domain (read-only).
/// </summary>
public sealed class AdNeverLoggedInAccountsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private const int DefaultGracePeriodDays = 14;
    private const int MaxGracePeriodDays = 3650;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_never_logged_in_accounts",
        "List accounts created before a grace period whose lastLogon/lastLogonTimestamp indicate no logon activity (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to query.")),
                ("grace_period_days", ToolSchema.Integer("Minimum account age in days before considering the account never-logged-in. Default 14.")),
                ("reference_time_utc", ToolSchema.String("Optional ISO-8601 UTC reference time used to compute account age (default now).")),
                ("max_results", ToolSchema.Integer("Maximum account rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record NeverLoggedInAccountRow(
        string SamAccountName,
        DateTime Created,
        int AccountAgeDays,
        DateTime? LastLogon,
        DateTime? LastLogonTimestamp);

    private sealed record AdNeverLoggedInAccountsResult(
        string DomainName,
        int GracePeriodDays,
        DateTime ReferenceTimeUtc,
        int Scanned,
        bool Truncated,
        IReadOnlyList<NeverLoggedInAccountRow> Accounts);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdNeverLoggedInAccountsTool"/> class.
    /// </summary>
    public AdNeverLoggedInAccountsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainQueryRequest(arguments, out var request, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var gracePeriodDays = ToolArgs.GetCappedInt32(
            arguments,
            key: "grace_period_days",
            defaultValue: DefaultGracePeriodDays,
            minInclusive: 1,
            maxInclusive: MaxGracePeriodDays);

        if (!ToolTime.TryParseUtcOptional(
                ToolArgs.GetOptionalTrimmed(arguments, "reference_time_utc"),
                out var referenceTimeUtc,
                out var referenceTimeError)) {
            return Task.FromResult(Error("invalid_argument", $"reference_time_utc: {referenceTimeError}"));
        }

        var referenceUtc = referenceTimeUtc ?? DateTime.UtcNow;

        if (!TryExecute(
                action: () => InactiveUserDetector.GetNeverLoggedInAccounts(request.DomainName, gracePeriodDays).ToArray(),
                result: out var accounts,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Never-logged-in accounts query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var projected = accounts
            .Select(account => new NeverLoggedInAccountRow(
                SamAccountName: account.SamAccountName,
                Created: account.Created,
                AccountAgeDays: Math.Max(0, (int)(referenceUtc - account.Created.ToUniversalTime()).TotalDays),
                LastLogon: account.LastLogon,
                LastLogonTimestamp: account.LastLogonTimestamp))
            .OrderByDescending(static row => row.AccountAgeDays)
            .ThenBy(static row => row.SamAccountName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = CapRows(
            allRows: projected,
            maxResults: request.MaxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdNeverLoggedInAccountsResult(
            DomainName: request.DomainName,
            GracePeriodDays: gracePeriodDays,
            ReferenceTimeUtc: referenceUtc,
            Scanned: scanned,
            Truncated: truncated,
            Accounts: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "accounts_view",
            title: "Active Directory: Never Logged-In Accounts (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("grace_period_days", gracePeriodDays);
                meta.Add("reference_time_utc", ToolTime.FormatUtc(referenceUtc));
                AddDomainAndMaxResultsMeta(meta, request.DomainName, request.MaxResults);
            }));
    }
}

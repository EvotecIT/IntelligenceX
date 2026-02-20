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
/// Returns managed service account usage posture for one Active Directory domain (read-only).
/// </summary>
public sealed class AdServiceAccountUsageTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_service_account_usage",
        "Inspect managed service account (MSA/gMSA) usage, principal bindings, and stale-computer indicators in one domain (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to query.")),
                ("account_type", ToolSchema.String("Optional account type filter.").Enum("any", "msa", "gmsa")),
                ("used_only", ToolSchema.Boolean("When true, include only service accounts with at least one enabled principal.")),
                ("some_computers_stale_only", ToolSchema.Boolean("When true, include only accounts where at least one mapped computer is stale.")),
                ("all_computers_stale_only", ToolSchema.Boolean("When true, include only accounts where all mapped computers are stale.")),
                ("include_principals", ToolSchema.Boolean("When true, include principal DN list for each account.")),
                ("include_principal_infos", ToolSchema.Boolean("When true, include principal details (implies include_principals=true).")),
                ("max_results", ToolSchema.Integer("Maximum service account rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private enum ServiceAccountTypeFilter {
        Any,
        Msa,
        Gmsa
    }

    private sealed record ServiceAccountPrincipalInfoRow(
        string Domain,
        string SamAccountName,
        string PrincipalType,
        DateTime? PasswordLastSet,
        int? PasswordAgeInDays,
        DateTime? LastLogonTimestamp,
        int? LastLogonAgeInDays,
        bool Enabled);

    private sealed record ServiceAccountUsageRow(
        string Domain,
        string SamAccountName,
        string AccountType,
        DateTime? PasswordLastSet,
        int? PasswordAgeInDays,
        DateTime? LastLogonTimestamp,
        int? LastLogonAgeInDays,
        bool IsUsed,
        bool SomeComputersStale,
        bool AllComputersStale,
        int PrincipalCount,
        IReadOnlyList<string>? Principals,
        IReadOnlyList<ServiceAccountPrincipalInfoRow>? PrincipalInfos);

    private sealed record AdServiceAccountUsageResult(
        string DomainName,
        string AccountType,
        bool UsedOnly,
        bool SomeComputersStaleOnly,
        bool AllComputersStaleOnly,
        bool IncludePrincipals,
        bool IncludePrincipalInfos,
        int Scanned,
        bool Truncated,
        IReadOnlyList<ServiceAccountUsageRow> Accounts);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdServiceAccountUsageTool"/> class.
    /// </summary>
    public AdServiceAccountUsageTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryReadRequiredDomainQueryRequest(arguments, out var request, out var argumentError)) {
            return Task.FromResult(argumentError!);
        }

        var accountTypeRaw = ToolArgs.GetOptionalTrimmed(arguments, "account_type");
        if (!TryParseAccountType(accountTypeRaw, out var accountTypeFilter, out var accountTypeError)) {
            return Task.FromResult(Error("invalid_argument", accountTypeError ?? "account_type must be one of: any, msa, gmsa."));
        }

        var usedOnly = ToolArgs.GetBoolean(arguments, "used_only", defaultValue: false);
        var someComputersStaleOnly = ToolArgs.GetBoolean(arguments, "some_computers_stale_only", defaultValue: false);
        var allComputersStaleOnly = ToolArgs.GetBoolean(arguments, "all_computers_stale_only", defaultValue: false);
        var includePrincipalInfos = ToolArgs.GetBoolean(arguments, "include_principal_infos", defaultValue: false);
        var includePrincipals = includePrincipalInfos || ToolArgs.GetBoolean(arguments, "include_principals", defaultValue: false);

        if (!TryExecute(
                action: () => ManagedServiceAccountUsageAnalyzer.GetUsage(request.DomainName).ToArray(),
                result: out var usage,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Managed service account usage query failed.",
                fallbackErrorCode: "query_failed",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = usage
            .Where(item => MatchesAccountType(item, accountTypeFilter))
            .Where(item => !usedOnly || item.IsUsed)
            .Where(item => !someComputersStaleOnly || item.SomeComputersStale)
            .Where(item => !allComputersStaleOnly || item.AllComputersStale)
            .Select(item => ToRow(item, includePrincipals, includePrincipalInfos))
            .OrderBy(static row => row.SamAccountName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var rows = CapRows(
            allRows: filtered,
            maxResults: request.MaxResults,
            scanned: out var scanned,
            truncated: out var truncated);

        var result = new AdServiceAccountUsageResult(
            DomainName: request.DomainName,
            AccountType: ToAccountTypeName(accountTypeFilter),
            UsedOnly: usedOnly,
            SomeComputersStaleOnly: someComputersStaleOnly,
            AllComputersStaleOnly: allComputersStaleOnly,
            IncludePrincipals: includePrincipals,
            IncludePrincipalInfos: includePrincipalInfos,
            Scanned: scanned,
            Truncated: truncated,
            Accounts: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "accounts_view",
            title: "Active Directory: Managed Service Account Usage (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("account_type", ToAccountTypeName(accountTypeFilter));
                meta.Add("used_only", usedOnly);
                meta.Add("some_computers_stale_only", someComputersStaleOnly);
                meta.Add("all_computers_stale_only", allComputersStaleOnly);
                meta.Add("include_principals", includePrincipals);
                meta.Add("include_principal_infos", includePrincipalInfos);
                AddDomainAndMaxResultsMeta(meta, request.DomainName, request.MaxResults);
            }));
    }

    private static ServiceAccountUsageRow ToRow(
        ServiceAccountUsageInfo item,
        bool includePrincipals,
        bool includePrincipalInfos) {
        var principals = includePrincipals
            ? item.Principals
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : null;

        var principalInfos = includePrincipalInfos
            ? item.PrincipalInfos
                .Where(static value => value is not null)
                .Select(static value => new ServiceAccountPrincipalInfoRow(
                    Domain: value.Domain,
                    SamAccountName: value.SamAccountName,
                    PrincipalType: value.PrincipalType,
                    PasswordLastSet: value.PasswordLastSet,
                    PasswordAgeInDays: value.PasswordAgeInDays,
                    LastLogonTimestamp: value.LastLogonTimestamp,
                    LastLogonAgeInDays: value.LastLogonAgeInDays,
                    Enabled: value.Enabled))
                .OrderBy(static value => value.SamAccountName, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : null;

        return new ServiceAccountUsageRow(
            Domain: item.Domain,
            SamAccountName: item.SamAccountName,
            AccountType: item.AccountType,
            PasswordLastSet: item.PasswordLastSet,
            PasswordAgeInDays: item.PasswordAgeInDays,
            LastLogonTimestamp: item.LastLogonTimestamp,
            LastLogonAgeInDays: item.LastLogonAgeInDays,
            IsUsed: item.IsUsed,
            SomeComputersStale: item.SomeComputersStale,
            AllComputersStale: item.AllComputersStale,
            PrincipalCount: item.Principals.Count,
            Principals: principals,
            PrincipalInfos: principalInfos);
    }

    private static bool MatchesAccountType(ServiceAccountUsageInfo item, ServiceAccountTypeFilter filter) {
        return filter switch {
            ServiceAccountTypeFilter.Msa => string.Equals(item.AccountType, "MSA", StringComparison.OrdinalIgnoreCase),
            ServiceAccountTypeFilter.Gmsa => string.Equals(item.AccountType, "gMSA", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static bool TryParseAccountType(
        string? raw,
        out ServiceAccountTypeFilter filter,
        out string? error) {
        filter = ServiceAccountTypeFilter.Any;
        error = null;

        if (string.IsNullOrWhiteSpace(raw)) {
            return true;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        filter = normalized switch {
            "any" => ServiceAccountTypeFilter.Any,
            "msa" => ServiceAccountTypeFilter.Msa,
            "gmsa" => ServiceAccountTypeFilter.Gmsa,
            _ => filter
        };

        if (normalized is "any" or "msa" or "gmsa") {
            return true;
        }

        error = "account_type must be one of: any, msa, gmsa.";
        return false;
    }

    private static string ToAccountTypeName(ServiceAccountTypeFilter filter) {
        return filter switch {
            ServiceAccountTypeFilter.Msa => "msa",
            ServiceAccountTypeFilter.Gmsa => "gmsa",
            _ => "any"
        };
    }
}

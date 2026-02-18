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
/// Returns domain controller security posture rows (SMB/LDAP/audit/spooler) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDomainControllerSecurityTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_controller_security",
        "Get domain controller security posture (SMB/LDAP/channel-binding/audit/spooler) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("domain_controller", ToolSchema.String("Optional single domain controller. Requires domain_name.")),
                ("insecure_only", ToolSchema.Boolean("When true, return only rows with at least one security finding.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DomainControllerSecurityRow(
        string DomainName,
        string DomainController,
        string SiteName,
        string OsVersion,
        bool IsOsSupported,
        bool SmbSigningRequired,
        bool PolicySmbSigningRequired,
        bool LdapSigningRequired,
        bool LdapInsecureBinds,
        string LdapChannelBindingMode,
        bool PrintSpoolerRunning,
        bool MissingAuditPolicy,
        bool MissingAdvancedAuditPolicy,
        bool AnyFinding);

    private sealed record DomainControllerSecurityError(
        string Scope,
        string Message);

    private sealed record AdDomainControllerSecurityResult(
        string? DomainName,
        string? ForestName,
        string? DomainController,
        bool InsecureOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DomainControllerSecurityError> Errors,
        IReadOnlyList<DomainControllerSecurityRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainControllerSecurityTool"/> class.
    /// </summary>
    public AdDomainControllerSecurityTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var domainController = ToolArgs.GetOptionalTrimmed(arguments, "domain_controller");
        var insecureOnly = ToolArgs.GetBoolean(arguments, "insecure_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        if (!string.IsNullOrWhiteSpace(domainController) && string.IsNullOrWhiteSpace(domainName)) {
            return ToolResponse.Error(
                "invalid_argument",
                "domain_controller requires domain_name so audit and policy context can be resolved.");
        }

        var targetDomains = string.IsNullOrWhiteSpace(domainName)
            ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[] { domainName! };

        if (targetDomains.Length == 0) {
            return ToolResponse.Error(
                "query_failed",
                "No domains resolved for domain-controller-security query. Provide domain_name or ensure forest discovery is available.");
        }

        var rows = new List<DomainControllerSecurityRow>(targetDomains.Length * 8);
        var errors = new List<DomainControllerSecurityError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var snapshot = string.IsNullOrWhiteSpace(domainController)
                    ? await DomainControllerSecurityService.GetSnapshotAsync(domain, cancellationToken).ConfigureAwait(false)
                    : await DomainControllerSecurityService.GetSnapshotForControllerAsync(domain, domainController!, cancellationToken).ConfigureAwait(false);

                var missingAuditPolicy = snapshot.MissingAuditPolicy
                    .Select(static x => x.DomainController)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var missingAdvancedAuditPolicy = snapshot.MissingAdvancedAuditPolicy
                    .Select(static x => x.DomainController)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var dc in snapshot.Controllers) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(domainController) &&
                        !string.Equals(dc.DomainController, domainController, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    var missingAudit = missingAuditPolicy.Contains(dc.DomainController);
                    var missingAdvanced = missingAdvancedAuditPolicy.Contains(dc.DomainController);
                    var missingChannelBinding = dc.LdapChannelBindingMode != LdapChannelBindingMode.Required;
                    var spoolerRunning = dc.PrintSpoolerRunning == true;
                    var anyFinding =
                        !dc.SmbSigningRequired ||
                        !dc.PolicySmbSigningRequired ||
                        !dc.LdapSigningRequired ||
                        dc.LdapInsecureBinds ||
                        missingChannelBinding ||
                        spoolerRunning ||
                        missingAudit ||
                        missingAdvanced;

                    rows.Add(new DomainControllerSecurityRow(
                        DomainName: domain,
                        DomainController: dc.DomainController,
                        SiteName: dc.SiteName,
                        OsVersion: dc.OsVersion,
                        IsOsSupported: dc.IsOsSupported,
                        SmbSigningRequired: dc.SmbSigningRequired,
                        PolicySmbSigningRequired: dc.PolicySmbSigningRequired,
                        LdapSigningRequired: dc.LdapSigningRequired,
                        LdapInsecureBinds: dc.LdapInsecureBinds,
                        LdapChannelBindingMode: dc.LdapChannelBindingMode?.ToString() ?? "Unknown",
                        PrintSpoolerRunning: spoolerRunning,
                        MissingAuditPolicy: missingAudit,
                        MissingAdvancedAuditPolicy: missingAdvanced,
                        AnyFinding: anyFinding));
                }
            } catch (Exception ex) {
                errors.Add(new DomainControllerSecurityError(domain, ex.Message));
            }
        }

        var filtered = rows
            .Where(row => !insecureOnly || row.AnyFinding)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<DomainControllerSecurityRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDomainControllerSecurityResult(
            DomainName: domainName,
            ForestName: forestName,
            DomainController: domainController,
            InsecureOnly: insecureOnly,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Domain Controller Security (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("insecure_only", insecureOnly);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
                if (!string.IsNullOrWhiteSpace(domainController)) {
                    meta.Add("domain_controller", domainController);
                }
            });
        return response;
    }
}

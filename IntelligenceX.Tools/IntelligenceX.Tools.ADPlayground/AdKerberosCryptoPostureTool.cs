using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Kerberos;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns Kerberos crypto posture counts (RC4/AES/pre-auth) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdKerberosCryptoPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_kerberos_crypto_posture",
        "Get Kerberos crypto posture counts (RC4-only, AES-disabled, pre-auth-disabled) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used to enumerate domains when domain_name is omitted.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record KerberosCryptoPostureRow(
        string DomainName,
        int UsersRc4Only,
        int ComputersRc4Only,
        int UsersAesDisabled,
        int ComputersAesDisabled,
        int UsersPreAuthDisabled,
        int TotalSignals);

    private sealed record KerberosCryptoPostureError(
        string Domain,
        string Message);

    private sealed record AdKerberosCryptoPostureResult(
        string? DomainName,
        string? ForestName,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<KerberosCryptoPostureError> Errors,
        IReadOnlyList<KerberosCryptoPostureRow> Domains);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdKerberosCryptoPostureTool"/> class.
    /// </summary>
    public AdKerberosCryptoPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

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
                "No domains resolved for Kerberos crypto posture query. Provide domain_name or ensure forest discovery is available.");
        }

        var rows = new List<KerberosCryptoPostureRow>(targetDomains.Length);
        var errors = new List<KerberosCryptoPostureError>();
        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var view = await KerberosCryptoPostureService.EvaluateAsync(domain).ConfigureAwait(false);
                rows.Add(new KerberosCryptoPostureRow(
                    DomainName: view.DomainName,
                    UsersRc4Only: view.UsersRc4Only,
                    ComputersRc4Only: view.ComputersRc4Only,
                    UsersAesDisabled: view.UsersAesDisabled,
                    ComputersAesDisabled: view.ComputersAesDisabled,
                    UsersPreAuthDisabled: view.UsersPreAuthDisabled,
                    TotalSignals: view.UsersRc4Only + view.ComputersRc4Only + view.UsersAesDisabled + view.ComputersAesDisabled + view.UsersPreAuthDisabled));
            } catch (Exception ex) {
                errors.Add(new KerberosCryptoPostureError(domain, ex.Message));
            }
        }

        var scanned = rows.Count;
        IReadOnlyList<KerberosCryptoPostureRow> projectedRows = scanned > maxResults
            ? rows.Take(maxResults).ToArray()
            : rows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdKerberosCryptoPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: Kerberos Crypto Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            });
        return response;
    }
}

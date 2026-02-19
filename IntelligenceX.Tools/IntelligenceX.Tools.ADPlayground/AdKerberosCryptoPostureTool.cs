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

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var maxResults = ResolveMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "Kerberos crypto posture",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return targetDomainError!;
        }

        var rows = new List<KerberosCryptoPostureRow>(targetDomains.Length);
        var errors = new List<KerberosCryptoPostureError>();
        await RunPerTargetCollectionAsync(
                targets: targetDomains,
                collectAsync: async domain => {
                var view = await KerberosCryptoPostureService.EvaluateAsync(domain).ConfigureAwait(false);
                rows.Add(new KerberosCryptoPostureRow(
                    DomainName: view.DomainName,
                    UsersRc4Only: view.UsersRc4Only,
                    ComputersRc4Only: view.ComputersRc4Only,
                    UsersAesDisabled: view.UsersAesDisabled,
                    ComputersAesDisabled: view.ComputersAesDisabled,
                    UsersPreAuthDisabled: view.UsersPreAuthDisabled,
                    TotalSignals: view.UsersRc4Only + view.ComputersRc4Only + view.UsersAesDisabled + view.ComputersAesDisabled + view.UsersPreAuthDisabled));
            },
            errorFactory: (domain, ex) => new KerberosCryptoPostureError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var projectedRows = CapRows(rows, maxResults, out var scanned, out var truncated);

        var result = new AdKerberosCryptoPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows);

        return BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: Kerberos Crypto Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            });
    }
}


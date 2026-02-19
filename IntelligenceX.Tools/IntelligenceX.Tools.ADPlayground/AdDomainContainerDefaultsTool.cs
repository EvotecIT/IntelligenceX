using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Domains;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns default user/computer container settings (redircmp/redirusr posture) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDomainContainerDefaultsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_container_defaults",
        "Get default user/computer container redirection settings and change indicators for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("changed_only", ToolSchema.Boolean("When true, return only domains where user/computer default container was changed.")),
                ("max_results", ToolSchema.Integer("Maximum domain rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DomainContainerDefaultsRow(
        string DomainName,
        string? DefaultComputerContainer,
        string? DefaultUserContainer,
        bool ComputerContainerChanged,
        bool UserContainerChanged,
        bool AnyChanged);

    private sealed record DomainContainerDefaultsError(
        string Domain,
        string Message);

    private sealed record AdDomainContainerDefaultsResult(
        string? DomainName,
        string? ForestName,
        bool ChangedOnly,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DomainContainerDefaultsError> Errors,
        IReadOnlyList<DomainContainerDefaultsRow> Domains);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainContainerDefaultsTool"/> class.
    /// </summary>
    public AdDomainContainerDefaultsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var changedOnly = ToolArgs.GetBoolean(arguments, "changed_only", defaultValue: false);
        var maxResults = ResolveMaxResults(arguments);

        if (!TryResolveTargetDomains(
                domainName: domainName,
                forestName: forestName,
                cancellationToken: cancellationToken,
                queryName: "domain-container-defaults",
                targetDomains: out var targetDomains,
                errorResponse: out var targetDomainError)) {
            return Task.FromResult(targetDomainError!);
        }

        var rows = new List<DomainContainerDefaultsRow>(targetDomains.Length);
        var errors = new List<DomainContainerDefaultsError>();
        RunPerTargetCollection(
            targets: targetDomains,
            collect: domain => {
                var snapshot = DomainContainerDefaultsService.GetSnapshot(domain);
                var anyChanged = snapshot.ComputerContainerChanged || snapshot.UserContainerChanged;
                rows.Add(new DomainContainerDefaultsRow(
                    DomainName: snapshot.DomainName,
                    DefaultComputerContainer: snapshot.DefaultComputerContainer,
                    DefaultUserContainer: snapshot.DefaultUserContainer,
                    ComputerContainerChanged: snapshot.ComputerContainerChanged,
                    UserContainerChanged: snapshot.UserContainerChanged,
                    AnyChanged: anyChanged));
            },
            errorFactory: (domain, ex) => new DomainContainerDefaultsError(domain, ToCollectorErrorMessage(ex)),
            errors: errors,
            cancellationToken: cancellationToken);

        var filtered = rows
            .Where(row => !changedOnly || row.AnyChanged)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<DomainContainerDefaultsRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDomainContainerDefaultsResult(
            DomainName: domainName,
            ForestName: forestName,
            ChangedOnly: changedOnly,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Domains: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "domains_view",
            title: "Active Directory: Domain Container Defaults (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("changed_only", changedOnly);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
            }));
    }
}


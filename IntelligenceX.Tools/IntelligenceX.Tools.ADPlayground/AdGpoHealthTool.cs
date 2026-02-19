using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Evaluates health for one or more GPO IDs using LDAP + SYSVOL probes (read-only, capped).
/// </summary>
public sealed class AdGpoHealthTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_gpo_health",
        "Evaluate LDAP/SYSVOL health for one or more GPO IDs (read-only, capped).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional domain DNS name. Defaults to current domain when available.")),
                ("gpo_ids", ToolSchema.Array(ToolSchema.String("GPO GUID in standard format (with or without braces)."), "GPO GUIDs to evaluate.")),
                ("max_results", ToolSchema.Integer("Maximum GPO IDs to process (capped).")))
            .WithTableViewOptions()
            .Required("gpo_ids")
            .NoAdditionalProperties());

    private sealed record AdGpoHealthResult(
        string DomainName,
        int RequestedCount,
        bool Truncated,
        IReadOnlyList<GpoHealthRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdGpoHealthTool"/> class.
    /// </summary>
    public AdGpoHealthTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ResolveDomainName(arguments);
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error(
                "not_configured",
                "domain_name is required when the current domain cannot be discovered.",
                hints: new[] {
                    "Pass domain_name explicitly (for example: corp.contoso.com).",
                    "Call ad_environment_discover first to verify reachable domain context."
                },
                isTransient: false));
        }

        var rawIds = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("gpo_ids"));
        if (rawIds.Count == 0) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "gpo_ids must contain at least one GUID."));
        }

        var maxResults = ResolveMaxResultsClampToOne(arguments);
        var requestedCount = rawIds.Count;
        var truncated = requestedCount > maxResults;
        if (rawIds.Count > maxResults) {
            rawIds = rawIds.GetRange(0, maxResults);
        }

        var ids = new List<Guid>(rawIds.Count);
        for (var i = 0; i < rawIds.Count; i++) {
            var raw = rawIds[i];
            if (!Guid.TryParse(raw, out var parsed)) {
                return Task.FromResult(ToolResponse.Error("invalid_argument", $"gpo_ids[{i}] is not a valid GUID."));
            }
            ids.Add(parsed);
        }

        var rows = new List<GpoHealthRow>(ids.Count);
        foreach (var id in ids) {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(GpoHealthService.EvaluateSingle(domainName, id));
        }

        var result = new AdGpoHealthResult(
            DomainName: domainName,
            RequestedCount: requestedCount,
            Truncated: truncated,
            Rows: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO health (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: rows.Count,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, maxResults);
                meta.Add("requested_count", requestedCount);
                meta.Add("processed_count", rows.Count);
            }));
    }

    private static string? ResolveDomainName(JsonObject? arguments) {
        var explicitDomainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (!string.IsNullOrWhiteSpace(explicitDomainName)) {
            return explicitDomainName;
        }

        return DomainHelper.TryGetCurrentDomainName(out var currentDomain) && !string.IsNullOrWhiteSpace(currentDomain)
            ? currentDomain
            : null;
    }
}

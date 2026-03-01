using System;
using System.Collections.Generic;
using System.Linq;
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
    private sealed record GpoHealthRequest(
        string? DomainName,
        IReadOnlyList<Guid> GpoIds,
        int RequestedCount,
        bool Truncated,
        int MaxResults);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<GpoHealthRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var rawIds = reader.DistinctStringArray("gpo_ids");
            if (rawIds.Count == 0) {
                return ToolRequestBindingResult<GpoHealthRequest>.Failure("gpo_ids must contain at least one GUID.");
            }

            var maxResults = reader.CappedInt32("max_results", Options.MaxResults, 1, Options.MaxResults);
            var requestedCount = rawIds.Count;
            var truncated = requestedCount > maxResults;
            var selectedRawIds = truncated ? rawIds.Take(maxResults).ToArray() : rawIds.ToArray();

            var ids = new List<Guid>(selectedRawIds.Length);
            for (var i = 0; i < selectedRawIds.Length; i++) {
                var raw = selectedRawIds[i];
                if (!Guid.TryParse(raw, out var parsed)) {
                    return ToolRequestBindingResult<GpoHealthRequest>.Failure($"gpo_ids[{i}] is not a valid GUID.");
                }

                ids.Add(parsed);
            }

            return ToolRequestBindingResult<GpoHealthRequest>.Success(new GpoHealthRequest(
                DomainName: reader.OptionalString("domain_name"),
                GpoIds: ids,
                RequestedCount: requestedCount,
                Truncated: truncated,
                MaxResults: maxResults));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<GpoHealthRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var domainName = ResolveDomainName(request.DomainName);
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResultV2.Error(
                "not_configured",
                "domain_name is required when the current domain cannot be discovered.",
                hints: new[] {
                    "Pass domain_name explicitly (for example: corp.contoso.com).",
                    "Call ad_environment_discover first to verify reachable domain context."
                },
                isTransient: false));
        }

        var rows = new List<GpoHealthRow>(request.GpoIds.Count);
        foreach (var id in request.GpoIds) {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(GpoHealthService.EvaluateSingle(domainName, id));
        }

        var result = new AdGpoHealthResult(
            DomainName: domainName,
            RequestedCount: request.RequestedCount,
            Truncated: request.Truncated,
            Rows: rows);

        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: rows,
            viewRowsPath: "rows_view",
            title: "Active Directory: GPO health (preview)",
            maxTop: MaxViewTop,
            baseTruncated: request.Truncated,
            scanned: rows.Count,
            metaMutate: meta => {
                AddDomainAndMaxResultsMeta(meta, domainName, request.MaxResults);
                meta.Add("requested_count", request.RequestedCount);
                meta.Add("processed_count", rows.Count);
            }));
    }

    private static string? ResolveDomainName(string? explicitDomainName) {
        if (!string.IsNullOrWhiteSpace(explicitDomainName)) {
            return explicitDomainName;
        }

        return DomainHelper.TryGetCurrentDomainName(out var currentDomain) && !string.IsNullOrWhiteSpace(currentDomain)
            ? currentDomain
            : null;
    }
}

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
/// Returns null-session and anonymous-SAM exposure posture across selected domain controllers (read-only).
/// </summary>
public sealed class AdNullSessionPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_null_session_posture",
        "Check DC null-session and anonymous-SAM exposure posture across one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, enumerates DCs in one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional explicit DC list. When set, skips domain/forest discovery.")),
                ("anonymous_sam_only", ToolSchema.Boolean("When true, return only rows where anonymous SAM is allowed.")),
                ("null_session_only", ToolSchema.Boolean("When true, return only rows where null session access is allowed.")),
                ("max_domain_controllers", ToolSchema.Integer("Maximum discovered DCs to evaluate (capped). Default 200.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record NullSessionPostureRow(
        string DomainName,
        string DomainController,
        bool AnonymousSamAllowed,
        bool NullSessionAllowed,
        bool AnyInsecure);

    private sealed record NullSessionPostureError(
        string DomainController,
        string Message);

    private sealed record AdNullSessionPostureResult(
        string? DomainName,
        string? ForestName,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<NullSessionPostureError> Errors,
        IReadOnlyList<NullSessionPostureRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdNullSessionPostureTool"/> class.
    /// </summary>
    public AdNullSessionPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        ReadDomainAndForestScope(arguments, out var domainName, out var forestName);
        var explicitDcs = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("domain_controllers"));
        var anonymousSamOnly = ToolArgs.GetBoolean(arguments, "anonymous_sam_only", defaultValue: false);
        var nullSessionOnly = ToolArgs.GetBoolean(arguments, "null_session_only", defaultValue: false);
        var maxDomainControllers = ToolArgs.GetCappedInt32(arguments, "max_domain_controllers", 200, 1, 2000);
        var maxResults = ResolveBoundedMaxResults(arguments);

        var dcRows = new List<(string DomainName, string DomainController)>();
        if (explicitDcs.Count > 0) {
            foreach (var dc in explicitDcs) {
                dcRows.Add((domainName ?? string.Empty, dc));
            }
        } else {
            var domains = string.IsNullOrWhiteSpace(domainName)
                ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(static x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : new[] { domainName! };

            if (domains.Length == 0) {
                return Task.FromResult(ToolResponse.Error(
                    "query_failed",
                    "No domains resolved for null-session posture query. Provide domain_name/domain_controllers or ensure forest discovery is available."));
            }

            foreach (var domain in domains) {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var dc in DomainHelper.EnumerateDomainControllers(domain, cancellationToken: cancellationToken)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    dcRows.Add((domain, dc));
                    if (dcRows.Count >= maxDomainControllers) {
                        break;
                    }
                }
                if (dcRows.Count >= maxDomainControllers) {
                    break;
                }
            }
        }

        if (dcRows.Count == 0) {
            return Task.FromResult(ToolResponse.Error("query_failed", "No domain controllers resolved for null-session posture query."));
        }

        var checker = new NullSessionChecker();
        var rows = new List<NullSessionPostureRow>(dcRows.Count);
        var errors = new List<NullSessionPostureError>();
        foreach (var (rowDomain, dc) in dcRows) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var anonymousSamAllowed = checker.IsAnonymousSamAllowed(dc);
                var nullSessionAllowed = checker.IsNullSessionAllowed(dc);
                rows.Add(new NullSessionPostureRow(
                    DomainName: rowDomain,
                    DomainController: dc,
                    AnonymousSamAllowed: anonymousSamAllowed,
                    NullSessionAllowed: nullSessionAllowed,
                    AnyInsecure: anonymousSamAllowed || nullSessionAllowed));
            } catch (Exception ex) {
                errors.Add(new NullSessionPostureError(dc, ToCollectorErrorMessage(ex)));
            }
        }

        var filtered = rows
            .Where(row => !anonymousSamOnly || row.AnonymousSamAllowed)
            .Where(row => !nullSessionOnly || row.NullSessionAllowed)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<NullSessionPostureRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        var result = new AdNullSessionPostureResult(
            DomainName: domainName,
            ForestName: forestName,
            Scanned: scanned,
            Truncated: truncated,
            ErrorCount: errors.Count,
            Errors: errors,
            Rows: projectedRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "rows_view",
            title: "Active Directory: Null Session Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("max_domain_controllers", maxDomainControllers);
                meta.Add("anonymous_sam_only", anonymousSamOnly);
                meta.Add("null_session_only", nullSessionOnly);
                meta.Add("error_count", errors.Count);
                AddDomainAndForestMeta(meta, domainName, forestName);
                if (explicitDcs.Count > 0) {
                    meta.Add("explicit_domain_controllers", explicitDcs.Count);
                }
            }));
    }
}


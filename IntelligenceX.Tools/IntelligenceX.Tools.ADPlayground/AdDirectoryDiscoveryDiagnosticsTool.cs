using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Diagnostics;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Produces AD domain controller discovery diagnostics and DNS/topology consistency findings (read-only).
/// </summary>
public sealed class AdDirectoryDiscoveryDiagnosticsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_directory_discovery_diagnostics",
        "Get diagnostics for AD domain controller discovery and DNS/topology consistency (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name.")),
                ("domains", ToolSchema.Array(ToolSchema.String(), "Optional explicit domain list. When omitted, scans forest scope.")),
                ("max_issues", ToolSchema.Integer("Maximum issues collected by diagnostics engine. Default 5000.")),
                ("dns_resolve_timeout_ms", ToolSchema.Integer("DNS host resolution timeout in milliseconds. Default 1500.")),
                ("ldap_timeout_ms", ToolSchema.Integer("LDAP query timeout in milliseconds. Default 3000.")),
                ("include_dns_srv_comparison", ToolSchema.Boolean("When true, compares AD DC list against DNS SRV records.")),
                ("include_host_resolution", ToolSchema.Boolean("When true, validates DNS A/AAAA resolution for DC hosts.")),
                ("include_directory_topology", ToolSchema.Boolean("When true, checks Sites/Servers/NTDS/computer topology consistency.")),
                ("as_issue", ToolSchema.Boolean("When true, output rows are the issue list; otherwise still includes issues with snapshot metadata.")),
                ("max_results", ToolSchema.Integer("Maximum issue rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdDirectoryDiscoveryDiagnosticsResult(
        bool AsIssue,
        int Scanned,
        bool Truncated,
        DirectoryDiscoveryDiagnosticsSnapshot Snapshot,
        IReadOnlyList<DirectoryDiscoveryIssue> Issues);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDirectoryDiscoveryDiagnosticsTool"/> class.
    /// </summary>
    public AdDirectoryDiscoveryDiagnosticsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var domains = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("domains"));
        var asIssue = ToolArgs.GetBoolean(arguments, "as_issue", defaultValue: false);
        var maxResults = ResolveMaxResultsClampToOne(arguments);

        var maxIssues = ToolArgs.GetCappedInt32(arguments, "max_issues", 5000, 1, 100_000);
        var dnsResolveTimeoutMs = ToolArgs.GetCappedInt32(arguments, "dns_resolve_timeout_ms", 1500, 200, 120_000);
        var ldapTimeoutMs = ToolArgs.GetCappedInt32(arguments, "ldap_timeout_ms", 3000, 200, 120_000);
        var includeDnsSrvComparison = ToolArgs.GetBoolean(arguments, "include_dns_srv_comparison", defaultValue: true);
        var includeHostResolution = ToolArgs.GetBoolean(arguments, "include_host_resolution", defaultValue: true);
        var includeDirectoryTopology = ToolArgs.GetBoolean(arguments, "include_directory_topology", defaultValue: true);

        if (!TryExecute(
                action: () => DirectoryDiscoveryDiagnosticsService.GetSnapshot(
                new DirectoryDiscoveryDiagnosticsOptions {
                    ForestName = forestName,
                    Domains = domains.Count == 0 ? null : domains,
                    IncludeDnsSrvComparison = includeDnsSrvComparison,
                    IncludeHostResolution = includeHostResolution,
                    IncludeDirectoryTopology = includeDirectoryTopology,
                    DnsResolveTimeoutMs = dnsResolveTimeoutMs,
                    LdapTimeoutMs = ldapTimeoutMs,
                    MaxIssues = maxIssues
                },
                cancellationToken),
                result: out DirectoryDiscoveryDiagnosticsSnapshot snapshot,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "Directory discovery diagnostics failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var scanned = snapshot.Issues.Count;
        IReadOnlyList<DirectoryDiscoveryIssue> rows = scanned > maxResults
            ? snapshot.Issues.Take(maxResults).ToArray()
            : snapshot.Issues;
        var truncated = scanned > rows.Count;

        var model = new AdDirectoryDiscoveryDiagnosticsResult(
            AsIssue: asIssue,
            Scanned: scanned,
            Truncated: truncated,
            Snapshot: snapshot,
            Issues: rows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: model,
            sourceRows: rows,
            viewRowsPath: asIssue ? "issues_view" : "snapshot_issues_view",
            title: "Active Directory: Directory Discovery Diagnostics (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("mode", asIssue ? "issues" : "snapshot");
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("max_issues", maxIssues);
                meta.Add("include_dns_srv_comparison", includeDnsSrvComparison);
                meta.Add("include_host_resolution", includeHostResolution);
                meta.Add("include_directory_topology", includeDirectoryTopology);
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
                if (domains.Count > 0) {
                    meta.Add("domains", ToolJson.ToJsonArray(domains));
                }
            }));
    }
}

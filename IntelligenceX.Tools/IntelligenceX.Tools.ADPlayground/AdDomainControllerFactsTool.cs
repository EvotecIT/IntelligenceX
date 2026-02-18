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
/// Returns AD-derived domain controller facts (site/GC/RODC/OS) for one domain or forest scope (read-only).
/// </summary>
public sealed class AdDomainControllerFactsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_controller_facts",
        "Get AD-derived domain controller facts (site/GC/RODC/OS and optional attributes) for one domain or forest scope (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. When set, evaluates one domain.")),
                ("forest_name", ToolSchema.String("Optional forest DNS name used when domain_name is omitted.")),
                ("additional_attributes", ToolSchema.Array(ToolSchema.String(), "Optional extra DC computer-object attributes to include in rows.")),
                ("include_attributes", ToolSchema.Boolean("When true, include additional attribute bag in each row.")),
                ("only_global_catalog", ToolSchema.Boolean("When true, return only global catalog DC rows.")),
                ("only_rodc", ToolSchema.Boolean("When true, return only RODC rows.")),
                ("timeout_ms", ToolSchema.Integer("Per-domain LDAP query timeout in milliseconds. Default 3000.")),
                ("max_results", ToolSchema.Integer("Maximum rows to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record DomainControllerFactRow(
        string DomainName,
        string DomainController,
        string? Site,
        bool? IsGlobalCatalog,
        bool? IsReadOnlyDomainController,
        string? OperatingSystem,
        string? OperatingSystemVersion,
        int AdditionalAttributeCount,
        IReadOnlyDictionary<string, string> Attributes);

    private sealed record DomainControllerFactsError(
        string Domain,
        string Message);

    private sealed record AdDomainControllerFactsResult(
        string? DomainName,
        string? ForestName,
        int TimeoutMs,
        bool IncludeAttributes,
        bool OnlyGlobalCatalog,
        bool OnlyRodc,
        int Scanned,
        bool Truncated,
        int ErrorCount,
        IReadOnlyList<DomainControllerFactsError> Errors,
        IReadOnlyList<DomainControllerFactRow> Rows);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainControllerFactsTool"/> class.
    /// </summary>
    public AdDomainControllerFactsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var additionalAttributes = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("additional_attributes"));
        var includeAttributes = ToolArgs.GetBoolean(arguments, "include_attributes", defaultValue: false);
        var onlyGlobalCatalog = ToolArgs.GetBoolean(arguments, "only_global_catalog", defaultValue: false);
        var onlyRodc = ToolArgs.GetBoolean(arguments, "only_rodc", defaultValue: false);
        var timeoutMs = ToolArgs.GetCappedInt32(arguments, "timeout_ms", 3000, 300, 60000);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var targetDomains = string.IsNullOrWhiteSpace(domainName)
            ? DomainHelper.EnumerateForestDomainNames(forestName, cancellationToken)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : new[] { domainName! };

        if (targetDomains.Length == 0) {
            return Task.FromResult(ToolResponse.Error(
                "query_failed",
                "No domains resolved for domain-controller-facts query. Provide domain_name or ensure forest discovery is available."));
        }

        var rows = new List<DomainControllerFactRow>(targetDomains.Length * 8);
        var errors = new List<DomainControllerFactsError>();

        foreach (var domain in targetDomains) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var facts = DomainControllerFactsService.GetFacts(domain, timeoutMs, additionalAttributes);
                foreach (var fact in facts.Values.OrderBy(static x => x.Server, StringComparer.OrdinalIgnoreCase)) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var isGc = fact.IsGC;
                    var isRo = fact.IsRO;
                    if (onlyGlobalCatalog && isGc != true) {
                        continue;
                    }
                    if (onlyRodc && isRo != true) {
                        continue;
                    }

                    var attributes = includeAttributes
                        ? fact.Attributes
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    rows.Add(new DomainControllerFactRow(
                        DomainName: domain,
                        DomainController: fact.Server,
                        Site: fact.Site,
                        IsGlobalCatalog: isGc,
                        IsReadOnlyDomainController: isRo,
                        OperatingSystem: fact.OperatingSystem,
                        OperatingSystemVersion: fact.OperatingSystemVersion,
                        AdditionalAttributeCount: fact.Attributes.Count,
                        Attributes: attributes));
                }
            } catch (Exception ex) {
                errors.Add(new DomainControllerFactsError(domain, ToCollectorErrorMessage(ex)));
            }
        }

        var scanned = rows.Count;
        IReadOnlyList<DomainControllerFactRow> projectedRows = scanned > maxResults
            ? rows.Take(maxResults).ToArray()
            : rows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdDomainControllerFactsResult(
            DomainName: domainName,
            ForestName: forestName,
            TimeoutMs: timeoutMs,
            IncludeAttributes: includeAttributes,
            OnlyGlobalCatalog: onlyGlobalCatalog,
            OnlyRodc: onlyRodc,
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
            title: "Active Directory: Domain Controller Facts (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("timeout_ms", timeoutMs);
                meta.Add("include_attributes", includeAttributes);
                meta.Add("only_global_catalog", onlyGlobalCatalog);
                meta.Add("only_rodc", onlyRodc);
                meta.Add("additional_attributes_count", additionalAttributes.Count);
                meta.Add("max_results", maxResults);
                meta.Add("error_count", errors.Count);
                if (!string.IsNullOrWhiteSpace(domainName)) {
                    meta.Add("domain_name", domainName);
                }
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            }));
    }
}

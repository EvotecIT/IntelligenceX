using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground;
using ADPlayground.Forests;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns forest functional-level posture and recommended target level (read-only).
/// </summary>
public sealed class AdForestFunctionalTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_forest_functional",
        "Get forest functional-level posture, supported ceiling, and recommended target level (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name. When omitted, uses current forest context.")),
                ("include_domain_overview", ToolSchema.Boolean("When true, include per-domain functional-level rows.")),
                ("max_domain_rows", ToolSchema.Integer("Maximum per-domain rows in domain_overview payload. Default 200.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record ForestFunctionalSummaryRow(
        string ForestName,
        string RootDomain,
        string ForestFunctionalLevelLabel,
        string LowestDomainFunctionalLevelLabel,
        int DomainCount,
        int DomainControllerCount,
        int GlobalCatalogCount,
        string MaximumSupportedForestFunctionalLevelLabel,
        string RecommendedFunctionalLevelLabel,
        int FunctionalLevelGap,
        bool IsAtRecommendedLevel);

    private sealed record ForestFunctionalDomainRow(
        string DomainName,
        string DomainFunctionalLevelLabel,
        int DomainControllerCount,
        string MaximumSupportedDomainFunctionalLevelLabel);

    private sealed record AdForestFunctionalResult(
        string? ForestName,
        bool IncludeDomainOverview,
        int MaxDomainRows,
        IReadOnlyList<ForestFunctionalSummaryRow> Forests,
        IReadOnlyList<ForestFunctionalDomainRow> DomainOverview);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdForestFunctionalTool"/> class.
    /// </summary>
    public AdForestFunctionalTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeDomainOverview = ToolArgs.GetBoolean(arguments, "include_domain_overview", defaultValue: false);
        var maxDomainRows = ToolArgs.GetCappedInt32(arguments, "max_domain_rows", 200, 1, 5000);

        ForestFunctionalSnapshot snapshot;
        IReadOnlyList<ForestFunctionalDomainRow> domainRows = Array.Empty<ForestFunctionalDomainRow>();

        try {
            var data = new ActiveDirectoryDataServices();
            data.GetInformation(forestName: forestName);
            var overview = ActiveDirectoryOverviewService.Create(data, cancellationToken);
            snapshot = ForestFunctionalService.GetSnapshot(data, overview);

            if (includeDomainOverview && overview != null) {
                domainRows = overview.Domains
                    .Select(static domain => new ForestFunctionalDomainRow(
                        DomainName: domain.DomainName,
                        DomainFunctionalLevelLabel: domain.DomainFunctionalLevelLabel,
                        DomainControllerCount: domain.DomainControllerCount,
                        MaximumSupportedDomainFunctionalLevelLabel: domain.MaximumSupportedDomainFunctionalLevelLabel))
                    .Take(maxDomainRows)
                    .ToArray();
            }
        } catch (Exception ex) {
            return Task.FromResult(ErrorFromException(
                ex,
                defaultMessage: "Forest functional posture query failed."));
        }

        var summaryRow = new ForestFunctionalSummaryRow(
            ForestName: snapshot.ForestName,
            RootDomain: snapshot.RootDomain,
            ForestFunctionalLevelLabel: snapshot.ForestFunctionalLevelLabel,
            LowestDomainFunctionalLevelLabel: snapshot.LowestDomainFunctionalLevelLabel,
            DomainCount: snapshot.DomainCount,
            DomainControllerCount: snapshot.DomainControllerCount,
            GlobalCatalogCount: snapshot.GlobalCatalogCount,
            MaximumSupportedForestFunctionalLevelLabel: snapshot.MaximumSupportedForestFunctionalLevelLabel,
            RecommendedFunctionalLevelLabel: snapshot.RecommendedFunctionalLevelLabel,
            FunctionalLevelGap: snapshot.FunctionalLevelGap,
            IsAtRecommendedLevel: snapshot.FunctionalLevelGap == 0);

        var result = new AdForestFunctionalResult(
            ForestName: forestName,
            IncludeDomainOverview: includeDomainOverview,
            MaxDomainRows: maxDomainRows,
            Forests: new[] { summaryRow },
            DomainOverview: domainRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: result.Forests,
            viewRowsPath: "forests_view",
            title: "Active Directory: Forest Functional Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            response: out var response,
            scanned: result.Forests.Count,
            metaMutate: meta => {
                meta.Add("include_domain_overview", includeDomainOverview);
                meta.Add("max_domain_rows", maxDomainRows);
                if (!string.IsNullOrWhiteSpace(forestName)) {
                    meta.Add("forest_name", forestName);
                }
            });
        return Task.FromResult(response);
    }
}

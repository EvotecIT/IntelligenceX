using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Lists Active Directory domain controllers.
/// </summary>
public sealed class AdDomainControllersTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_controllers",
        "List Active Directory domain controllers (read-only).",
        ToolSchema.Object(
                ("max_results", ToolSchema.Integer("Optional maximum domain controllers to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainControllersTool"/> class.
    /// </summary>
    public AdDomainControllersTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var max = ResolveMaxResultsClampToOne(arguments);

        var (dc, defaultNc) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);
        if (string.IsNullOrWhiteSpace(defaultNc)) {
            return Task.FromResult(Error(
                errorCode: "not_configured",
                error: "search_base_dn could not be resolved (RootDSE defaultNamingContext missing).",
                hints: new[] {
                    "Call ad_environment_discover first to resolve effective domain_controller and search_base_dn.",
                    "If discovery fails, pass domain_controller and search_base_dn explicitly."
                },
                isTransient: false));
        }

        if (!AdDomainControllersService.TryQuery(
                options: new AdDomainControllersService.DomainControllersQueryOptions {
                    DomainController = dc,
                    SearchBaseDn = defaultNc,
                    MaxResults = max,
                    MaxValuesPerAttribute = 50
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = queryResult!;
        AdDynamicTableView.TryBuildResponseFromOutputRows(
            arguments: arguments,
            model: result,
            rows: result.DomainControllers,
            title: "Active Directory: Domain Controllers (preview)",
            rowsPath: "domain_controllers_view",
            baseTruncated: result.IsTruncated,
            response: out var response);
        return Task.FromResult(response);
    }
}

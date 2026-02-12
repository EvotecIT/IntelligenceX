using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Groups;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Summarizes Domain Admins group membership with resolved details and quick risk flags (read-only).
/// </summary>
public sealed class AdDomainAdminsSummaryTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_domain_admins_summary",
        "Summarize Domain Admins membership with resolved details and quick risk flags (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("Optional DNS domain name. Used to derive group SID and to pick query targets.")),
                ("search_base_dn", ToolSchema.String("Optional base DN hint used to derive domain_name.")),
                ("domain_controller", ToolSchema.String("Optional domain controller hint (engine chooses best DC).")),
                ("include_nested", ToolSchema.Boolean("When true, include nested members. Default false.")),
                ("users_only", ToolSchema.Boolean("When true, include only user-like objects. Default false.")),
                ("computers_only", ToolSchema.Boolean("When true, include only computer objects. Default false.")),
                ("include_members", ToolSchema.Boolean("When true, include resolved member objects. Default true.")),
                ("max_results", ToolSchema.Integer("Maximum members to return (capped). Default tool cap.")))
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDomainAdminsSummaryTool"/> class.
    /// </summary>
    public AdDomainAdminsSummaryTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var includeNested = arguments?.GetBoolean("include_nested") ?? false;
        var usersOnly = arguments?.GetBoolean("users_only") ?? false;
        var computersOnly = arguments?.GetBoolean("computers_only") ?? false;
        if (usersOnly && computersOnly) {
            return Error("users_only and computers_only cannot both be true.");
        }

        var includeMembers = arguments?.GetBoolean("include_members") ?? true;

        var requestedMax = arguments?.GetInt64("max_results");
        var maxResults = requestedMax.HasValue && requestedMax.Value > 0
            ? (int)Math.Min(requestedMax.Value, Options.MaxResults)
            : Options.MaxResults;

        try {
            var summary = await DomainAdminsSummaryService
                .QueryAsync(
                    new DomainAdminsSummaryQueryOptions {
                        DomainControllerHint = arguments?.GetString("domain_controller") ?? Options.DomainController,
                        SearchBaseDnHint = arguments?.GetString("search_base_dn") ?? Options.DefaultSearchBaseDn,
                        DomainName = arguments?.GetString("domain_name"),
                        IncludeNested = includeNested,
                        UsersOnly = usersOnly,
                        ComputersOnly = computersOnly,
                        IncludeMembers = includeMembers,
                        MaxResults = maxResults,
                        Timeout = TimeSpan.FromSeconds(30)
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            return ToolResponse.OkModel(summary);
        } catch (InvalidOperationException ex) {
            return Error(ex.Message);
        } catch (ArgumentException ex) {
            return Error(ex.Message);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using ADPlayground.Kerberos;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Aggregates SPNs by service class/host to help audit Kerberos service exposure (read-only).
/// </summary>
public sealed class AdSpnStatsTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;
    private const int DefaultMaxServiceClasses = 50;
    private const int DefaultMaxHosts = 50;
    private const int MaxServiceClassesCap = 200;
    private const int MaxHostsCap = 500;

    private sealed record SpnStatsRequest(
        string? SpnContains,
        string? SpnExact,
        string? Kind,
        bool EnabledOnly,
        bool IncludeExamples,
        int MaxServiceClasses,
        int MaxHosts);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_spn_stats",
        "Aggregate servicePrincipalName (SPN) values by service class/host (read-only).",
        ToolSchema.Object(
                ("spn_contains", ToolSchema.String("Optional substring match for SPNs (LDAP contains). When omitted, includes all SPNs.")),
                ("spn_exact", ToolSchema.String("Optional exact SPN match (mutually exclusive with spn_contains).")),
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "computer")),
                ("enabled_only", ToolSchema.Boolean("When true, filter out disabled accounts (userAccountControl bit 2). Default false.")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum directory objects to scan (capped).")),
                ("max_service_classes", ToolSchema.Integer("Maximum service classes returned (capped).")),
                ("max_hosts", ToolSchema.Integer("Maximum hosts returned (capped).")),
                ("include_examples", ToolSchema.Boolean("When true, include up to 3 example SPNs and accounts per service class.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdSpnStatsTool"/> class.
    /// </summary>
    public AdSpnStatsTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return await RunPipelineAsync(
                arguments: arguments,
                cancellationToken: cancellationToken,
                binder: BindRequest,
                execute: ExecuteAsync)
            .ConfigureAwait(false);
    }

    private static ToolRequestBindingResult<SpnStatsRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var spnContains = reader.OptionalString("spn_contains");
            var spnExact = reader.OptionalString("spn_exact");
            if (!string.IsNullOrWhiteSpace(spnContains) && !string.IsNullOrWhiteSpace(spnExact)) {
                return ToolRequestBindingResult<SpnStatsRequest>.Failure("spn_contains and spn_exact are mutually exclusive.");
            }

            return ToolRequestBindingResult<SpnStatsRequest>.Success(new SpnStatsRequest(
                SpnContains: spnContains,
                SpnExact: spnExact,
                Kind: reader.OptionalString("kind"),
                EnabledOnly: reader.Boolean("enabled_only"),
                IncludeExamples: reader.Boolean("include_examples"),
                MaxServiceClasses: ResolvePositiveCappedOrDefault(
                    reader.OptionalInt64("max_service_classes"),
                    DefaultMaxServiceClasses,
                    MaxServiceClassesCap),
                MaxHosts: ResolvePositiveCappedOrDefault(
                    reader.OptionalInt64("max_hosts"),
                    DefaultMaxHosts,
                    MaxHostsCap)));
        });
    }

    private async Task<string> ExecuteAsync(ToolPipelineContext<SpnStatsRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var kind = LdapToolKinds.ParseSpnAccountKind(request.Kind);

        var maxObjects = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        var query = new SpnStatsService.SpnStatsQueryOptions {
            DomainController = dc,
            SearchBaseDn = baseDn,
            SpnContains = request.SpnContains,
            SpnExact = request.SpnExact,
            Kind = kind,
            EnabledOnly = request.EnabledOnly,
            PageSize = 250,
            MaxObjects = maxObjects,
            MaxServiceClasses = request.MaxServiceClasses,
            MaxHosts = request.MaxHosts,
            IncludeExamples = request.IncludeExamples,
            MaxExamplesPerBucket = 3
        };

        var stats = await SpnStatsService.QueryAsync(query, cancellationToken).ConfigureAwait(false);

        return BuildAutoTableResponse(
            arguments: context.Arguments,
            model: stats,
            sourceRows: stats.ServiceClasses,
            viewRowsPath: "service_classes_view",
            title: "Active Directory: SPN Stats (preview)",
            maxTop: MaxViewTop,
            baseTruncated: stats.Truncated,
            scanned: stats.ScannedObjects);
    }

    private static int ResolvePositiveCappedOrDefault(long? requestedValue, int defaultValue, int maxInclusive) {
        if (requestedValue is not { } rawValue || rawValue <= 0) {
            return defaultValue;
        }

        return (int)Math.Min(rawValue, maxInclusive);
    }
}

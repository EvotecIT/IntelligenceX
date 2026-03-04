using System;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Audits delegation-related settings for AD user/computer accounts (read-only).
/// </summary>
public sealed class AdDelegationAuditTool : ActiveDirectoryToolBase, ITool {
    private const int DefaultMaxValuesPerAttribute = 50;
    private const int MaxValuesPerAttributeCap = 200;
    private const int MaxViewTop = 5000;

    private sealed record DelegationAuditRequest(
        string Kind,
        bool EnabledOnly,
        bool IncludeSpns,
        bool IncludeAllowedToDelegateTo,
        int MaxValuesPerAttribute);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_delegation_audit",
        "Audit delegation-related settings (unconstrained, protocol transition, constrained) for AD users/computers (read-only).",
        ToolSchema.Object(
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "computer")),
                ("enabled_only", ToolSchema.Boolean("When true, filter out disabled accounts. Default false.")),
                ("include_spns", ToolSchema.Boolean("When true, include (capped) servicePrincipalName values per object. Default false.")),
                ("include_allowed_to_delegate_to", ToolSchema.Boolean("When true, include (capped) msDS-AllowedToDelegateTo values per object. Default false.")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values returned for multi-valued attributes (capped). Default 50.")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("max_results", ToolSchema.Integer("Maximum objects to return (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdDelegationAuditTool"/> class.
    /// </summary>
    public AdDelegationAuditTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<DelegationAuditRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var kind = reader.OptionalString("kind");
            kind = string.IsNullOrWhiteSpace(kind) ? "any" : kind.Trim().ToLowerInvariant();

            return ToolRequestBindingResult<DelegationAuditRequest>.Success(new DelegationAuditRequest(
                Kind: kind,
                EnabledOnly: reader.Boolean("enabled_only"),
                IncludeSpns: reader.Boolean("include_spns"),
                IncludeAllowedToDelegateTo: reader.Boolean("include_allowed_to_delegate_to"),
                MaxValuesPerAttribute: ResolvePositiveCappedOrDefault(
                    reader.OptionalInt64("max_values_per_attribute"),
                    DefaultMaxValuesPerAttribute,
                    MaxValuesPerAttributeCap)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<DelegationAuditRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var maxResults = ResolveMaxResults(context.Arguments, nonPositiveBehavior: MaxResultsNonPositiveBehavior.DefaultToOptionCap);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);
        if (!LdapToolDelegationAuditService.TryExecute(
                request: new LdapToolDelegationAuditQueryRequest {
                    Kind = request.Kind,
                    EnabledOnly = request.EnabledOnly,
                    IncludeSpns = request.IncludeSpns,
                    IncludeAllowedToDelegateTo = request.IncludeAllowedToDelegateTo,
                    MaxValuesPerAttribute = request.MaxValuesPerAttribute,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxResults = maxResults
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = queryResult!;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Results,
            viewRowsPath: "results_view",
            title: "Active Directory: Delegation Audit (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.IsTruncated,
            scanned: result.Results.Count));
    }

    private static int ResolvePositiveCappedOrDefault(long? requestedValue, int defaultValue, int maxInclusive) {
        if (requestedValue is not { } rawValue || rawValue <= 0) {
            return defaultValue;
        }

        return (int)Math.Min(rawValue, maxInclusive);
    }
}

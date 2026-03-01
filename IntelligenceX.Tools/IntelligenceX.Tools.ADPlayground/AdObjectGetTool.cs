using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Gets a single Active Directory object by a common identifier (read-only).
/// </summary>
public sealed class AdObjectGetTool : ActiveDirectoryToolBase, ITool {
    private sealed record ObjectGetRequest(
        string Identity,
        string? Kind,
        int MaxValuesPerAttribute,
        IReadOnlyList<string> Attributes);

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_object_get",
        "Get a single Active Directory object by identity (DN, sAMAccountName, UPN, mail, DNS hostname, cn/name) (read-only).",
        ToolSchema.Object(
                ("identity", ToolSchema.String("Identity to match: distinguishedName, sAMAccountName, userPrincipalName, mail, dNSHostName, cn, or name.")),
                ("kind", ToolSchema.String("Object kind to search.").Enum("any", "user", "group", "computer")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional attributes to include (engine policy enforced).")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values per multi-valued attribute (capped). Default 50.")))
            .Required("identity")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdObjectGetTool"/> class.
    /// </summary>
    public AdObjectGetTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ObjectGetRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("identity", out var identity, out var identityError)) {
                return ToolRequestBindingResult<ObjectGetRequest>.Failure(identityError);
            }

            var requestedMaxValues = reader.OptionalInt64("max_values_per_attribute");
            var maxValuesPerAttribute = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
                ? (int)Math.Min(requestedMaxValues.Value, 200)
                : 50;

            return ToolRequestBindingResult<ObjectGetRequest>.Success(new ObjectGetRequest(
                Identity: identity,
                Kind: reader.OptionalString("kind"),
                MaxValuesPerAttribute: maxValuesPerAttribute,
                Attributes: reader.StringArray("attributes")));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ObjectGetRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var kind = string.IsNullOrWhiteSpace(request.Kind) ? "any" : request.Kind.Trim().ToLowerInvariant();
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        if (!LdapToolObjectGetService.TryExecute(
                request: new LdapToolObjectGetQueryRequest {
                    Identity = request.Identity,
                    Kind = kind,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxValuesPerAttribute = request.MaxValuesPerAttribute,
                    Attributes = request.Attributes
                },
                result: out var queryResult,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        if (!queryResult!.Found) {
            var title = queryResult.Ambiguous
                ? "Active Directory: Object Get (ambiguous)"
                : "Active Directory: Object Get";

            var facts = new[] {
                ("Found", "false"),
                ("Ambiguous", queryResult.Ambiguous ? "true" : "false"),
                ("Identity", queryResult.Identity),
                ("Kind", queryResult.Kind),
                ("Domain controller", queryResult.DomainController)
            };

            return Task.FromResult(ToolResultV2.OkFactsModel(
                model: queryResult,
                title: title,
                facts: facts,
                meta: ToolOutputHints.Meta(count: facts.Length, truncated: false),
                keyHeader: "Field",
                valueHeader: "Value",
                truncated: false,
                render: null));
        }

        LdapToolOutputRow? obj = null;
        if (queryResult.Object is not null) {
            obj = new LdapToolOutputRow {
                Attributes = queryResult.Object.Attributes,
                TruncatedAttributes = queryResult.Object.TruncatedAttributes
            };
        }

        var dnValue = obj is null ? string.Empty : AdQueryResultHelpers.ReadStringValue(obj.Attributes, "distinguishedName");

        var root = new {
            queryResult.Found,
            queryResult.Ambiguous,
            queryResult.Warning,
            queryResult.Identity,
            queryResult.Kind,
            queryResult.DomainController,
            queryResult.SearchBaseDn,
            queryResult.LdapFilter,
            queryResult.MaxValuesPerAttribute,
            queryResult.TruncatedAttributes,
            queryResult.MatchDns,
            Object = obj
        };

        var factsFound = new[] {
            ("Found", "true"),
            ("Identity", queryResult.Identity),
            ("Kind", queryResult.Kind),
            ("DN", dnValue),
            ("Domain controller", queryResult.DomainController)
        };

        return Task.FromResult(ToolResultV2.OkFactsModel(
            model: root,
            title: "Active Directory: Object Get",
            facts: factsFound,
            meta: ToolOutputHints.Meta(count: factsFound.Length, truncated: false),
            keyHeader: "Field",
            valueHeader: "Value",
            truncated: false,
            render: null));
    }
}

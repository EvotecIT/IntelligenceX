using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Resolves a batch of Active Directory objects by DN or SID (read-only).
/// </summary>
public sealed class AdObjectResolveTool : ActiveDirectoryToolBase, ITool {
    private sealed record ObjectResolveRequest(
        IReadOnlyList<string> Identities,
        string? IdentityKind,
        string? Kind,
        int MaxInputs,
        int MaxValuesPerAttribute);

    private const int DefaultMaxInputs = 200;
    private const int MaxMaxInputs = 5000;
    private const int DefaultMaxValuesPerAttribute = 25;
    private const int MaxViewTop = 5000;

    private static readonly string[] DefaultAttributes = {
        "distinguishedName",
        "objectClass",
        "objectSid",
        "cn",
        "name",
        "sAMAccountName",
        "userPrincipalName",
        "dNSHostName",
        "displayName",
        "mail"
    };

    private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase) {
        "distinguishedName",
        "objectClass",
        "objectSid",
        "objectGUID",
        "cn",
        "name",
        "sAMAccountName",
        "userPrincipalName",
        "dNSHostName",
        "displayName",
        "mail",
        "description",
        "whenCreated",
        "whenChanged",
        "userAccountControl",
        "adminCount",
        "memberOf"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_object_resolve",
        "Resolve multiple Active Directory objects by distinguishedName and/or objectSid (read-only). Designed to avoid N+1 ad_object_get loops.",
        ToolSchema.Object(
                ("identities", ToolSchema.Array(ToolSchema.String(), "List of identities to resolve. Supported: distinguishedName (DN) and objectSid (SID string).")),
                ("identity_kind", ToolSchema.String("How to interpret identities. auto=detect DN vs SID (default).").Enum("auto", "dn", "sid")),
                ("kind", ToolSchema.String("Optional object kind filter to reduce results.").Enum("any", "user", "group", "computer")),
                ("search_base_dn", ToolSchema.String("Optional base DN override (defaults to RootDSE defaultNamingContext).")),
                ("domain_controller", ToolSchema.String("Optional domain controller override.")),
                ("attributes", ToolSchema.Array(ToolSchema.String(), "Optional attributes to include (allowlist enforced).")),
                ("max_inputs", ToolSchema.Integer("Maximum identities to process (capped). Default 200.")),
                ("max_values_per_attribute", ToolSchema.Integer("Maximum values per multi-valued attribute (capped). Default 25.")))
            .WithTableViewOptions()
            .Required("identities")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdObjectResolveTool"/> class.
    /// </summary>
    public AdObjectResolveTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<ObjectResolveRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            var identities = reader.StringArray("identities");
            if (identities.Count == 0) {
                return ToolRequestBindingResult<ObjectResolveRequest>.Failure("identities is required.");
            }

            var requestedMaxInputs = reader.OptionalInt64("max_inputs");
            var maxInputs = requestedMaxInputs.HasValue && requestedMaxInputs.Value > 0
                ? (int)Math.Min(requestedMaxInputs.Value, MaxMaxInputs)
                : DefaultMaxInputs;

            var requestedMaxValues = reader.OptionalInt64("max_values_per_attribute");
            var maxValuesPerAttribute = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
                ? (int)Math.Min(requestedMaxValues.Value, 200)
                : DefaultMaxValuesPerAttribute;

            return ToolRequestBindingResult<ObjectResolveRequest>.Success(new ObjectResolveRequest(
                Identities: identities,
                IdentityKind: reader.OptionalString("identity_kind"),
                Kind: reader.OptionalString("kind"),
                MaxInputs: maxInputs,
                MaxValuesPerAttribute: maxValuesPerAttribute));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ObjectResolveRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var identityKind = LdapToolResolveHelper.ParseIdentityKind(request.IdentityKind);
        var kind = LdapToolKinds.ParseObjectKind(request.Kind);
        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(context.Arguments, cancellationToken);

        var attributes = ResolveAttributes(
            arguments: context.Arguments,
            attributesKey: "attributes",
            allowedAttributes: AllowedAttributes,
            defaultAttributes: DefaultAttributes,
            requiredAttributes: new[] { "distinguishedName", "objectSid", "objectClass" });

        var input = request.Identities.Take(request.MaxInputs).ToArray();
        var kindToolString = LdapToolKinds.ToToolString(kind);
        if (!LdapToolObjectResolveService.TryExecute(
                request: new LdapToolObjectResolveQueryRequest {
                    Identities = input,
                    InputsTotal = request.Identities.Count,
                    MaxInputs = request.MaxInputs,
                    InputsTruncated = request.Identities.Count > input.Length,
                    IdentityKind = identityKind,
                    Kind = kindToolString,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxValuesPerAttribute = request.MaxValuesPerAttribute,
                    Attributes = attributes
                },
                result: out var output,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = output!;
        return Task.FromResult(ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: result,
            sourceRows: result.Results,
            viewRowsPath: "results_view",
            title: "Active Directory: Object Resolve (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.InputsTruncated,
            scanned: request.Identities.Count));
    }
}

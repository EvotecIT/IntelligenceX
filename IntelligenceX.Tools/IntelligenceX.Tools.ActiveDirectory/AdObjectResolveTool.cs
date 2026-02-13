using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Helpers;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Resolves a batch of Active Directory objects by DN or SID (read-only).
/// </summary>
public sealed class AdObjectResolveTool : ActiveDirectoryToolBase, ITool {
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
        cancellationToken.ThrowIfCancellationRequested();

        var identitiesArray = arguments?.GetArray("identities");
        if (identitiesArray is null || identitiesArray.Count == 0) {
            return Task.FromResult(Error("invalid_argument", "identities is required."));
        }

        var identityKind = LdapToolResolveHelper.ParseIdentityKind(ToolArgs.GetOptionalTrimmed(arguments, "identity_kind"));
        var kind = LdapToolKinds.ParseObjectKind(ToolArgs.GetOptionalTrimmed(arguments, "kind"));
        var kindToolString = LdapToolKinds.ToToolString(kind);

        var requestedMaxInputs = arguments?.GetInt64("max_inputs");
        var maxInputs = requestedMaxInputs.HasValue && requestedMaxInputs.Value > 0
            ? (int)Math.Min(requestedMaxInputs.Value, MaxMaxInputs)
            : DefaultMaxInputs;

        var requestedMaxValues = arguments?.GetInt64("max_values_per_attribute");
        var maxValuesPerAttribute = requestedMaxValues.HasValue && requestedMaxValues.Value > 0
            ? (int)Math.Min(requestedMaxValues.Value, 200)
            : DefaultMaxValuesPerAttribute;

        var (dc, baseDn) = ResolveDomainControllerAndSearchBase(arguments, cancellationToken);

        var attributes = ResolveAttributes(
            arguments: arguments,
            attributesKey: "attributes",
            allowedAttributes: AllowedAttributes,
            defaultAttributes: DefaultAttributes,
            requiredAttributes: new[] { "distinguishedName", "objectSid", "objectClass" });

        var input = ToolArgs.ReadStringArrayCapped(identitiesArray, maxInputs);
        if (!LdapToolObjectResolveService.TryExecute(
                request: new LdapToolObjectResolveQueryRequest {
                    Identities = input,
                    InputsTotal = identitiesArray.Count,
                    MaxInputs = maxInputs,
                    InputsTruncated = identitiesArray.Count > input.Count,
                    IdentityKind = identityKind,
                    Kind = kindToolString,
                    DomainController = dc,
                    SearchBaseDn = baseDn ?? string.Empty,
                    MaxValuesPerAttribute = maxValuesPerAttribute,
                    Attributes = attributes
                },
                result: out var output,
                failure: out var failure,
                cancellationToken: cancellationToken)) {
            return Task.FromResult(AdQueryResultHelpers.MapQueryFailure(failure));
        }

        var result = output!;
        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: result.Results,
            viewRowsPath: "results_view",
            title: "Active Directory: Object Resolve (preview)",
            maxTop: MaxViewTop,
            baseTruncated: result.InputsTruncated,
            response: out var response,
            scanned: identitiesArray.Count);
        return Task.FromResult(response);
    }
}

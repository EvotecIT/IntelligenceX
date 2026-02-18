using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Gpo;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns client/server authentication posture baseline for Domain Controllers OU (read-only).
/// </summary>
public sealed class AdClientServerAuthPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_client_server_auth_posture",
        "Aggregate Domain Controllers OU authentication posture (LM/NTLM, SMB signing, LDAP signing/channel binding, Netlogon, null-session policy; read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdClientServerAuthPostureResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        bool LdapSigningRequired,
        bool LdapChannelBindingEnabled,
        bool? SmbSigningServerRequired,
        bool? SmbSigningClientRequired,
        int? LmCompatibilityLevel,
        bool? NoLmHash,
        NtlmSmbPostureService.View NtlmSmb,
        RestrictNtlmEvaluator.Summary RestrictNtlmSummary,
        LdapServerSigningEvaluator.View LdapSigning,
        LdapChannelBindingEvaluator.View LdapChannelBinding,
        NetlogonConfiguration Netlogon,
        RestrictAnonymousEvaluator.View RestrictAnonymous,
        NullSessionFallbackEvaluator.View NullSessionFallback,
        NullSessionEvaluator.View NullSession,
        SmbNtlmConfiguration? SmbNtlmDetails,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdClientServerAuthPostureTool"/> class.
    /// </summary>
    public AdClientServerAuthPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var domainName = ToolArgs.GetOptionalTrimmed(arguments, "domain_name");
        if (string.IsNullOrWhiteSpace(domainName)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "domain_name is required."));
        }

        var includeAttribution = ToolArgs.GetBoolean(arguments, "include_attribution", defaultValue: true);
        var configuredAttributionOnly = ToolArgs.GetBoolean(arguments, "configured_attribution_only", defaultValue: false);
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);

        var posture = ClientServerAuthPostureService.EvaluateForDomainControllers(domainName);
        if (!posture.CollectionSucceeded) {
            var message = string.IsNullOrWhiteSpace(posture.CollectionError)
                ? "Client/server authentication posture query failed."
                : posture.CollectionError!;
            return Task.FromResult(ToolResponse.Error("query_failed", message));
        }

        var attributionRows = includeAttribution
            ? posture.Attribution
                .Where(row => !configuredAttributionOnly || !string.IsNullOrWhiteSpace(row.Effective) && !string.Equals(row.Effective, "Not configured", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<PolicyAttribution>();

        var scanned = attributionRows.Length;
        IReadOnlyList<PolicyAttribution> projectedRows = scanned > maxResults
            ? attributionRows.Take(maxResults).ToArray()
            : attributionRows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdClientServerAuthPostureResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            LdapSigningRequired: posture.LdapSigningRequired,
            LdapChannelBindingEnabled: posture.LdapChannelBindingEnabled,
            SmbSigningServerRequired: posture.SmbSigningServerRequired,
            SmbSigningClientRequired: posture.SmbSigningClientRequired,
            LmCompatibilityLevel: posture.LmCompatibilityLevel,
            NoLmHash: posture.NoLmHash,
            NtlmSmb: posture.NtlmSmb,
            RestrictNtlmSummary: posture.RestrictNtlmSummary,
            LdapSigning: posture.LdapSigning,
            LdapChannelBinding: posture.LdapChannelBinding,
            Netlogon: posture.Netlogon,
            RestrictAnonymous: posture.RestrictAnonymous,
            NullSessionFallback: posture.NullSessionFallback,
            NullSession: posture.NullSession,
            SmbNtlmDetails: posture.SmbNtlmDetails,
            Attribution: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Client/Server Auth Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("include_attribution", includeAttribution);
                meta.Add("configured_attribution_only", configuredAttributionOnly);
                meta.Add("ldap_signing_required", posture.LdapSigningRequired);
                meta.Add("ldap_channel_binding_enabled", posture.LdapChannelBindingEnabled);
                meta.Add("max_results", maxResults);
            });
        return Task.FromResult(response);
    }
}

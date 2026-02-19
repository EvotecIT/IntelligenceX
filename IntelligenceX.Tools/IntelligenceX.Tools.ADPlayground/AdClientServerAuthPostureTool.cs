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
        return ExecutePolicyAttributionTool(
            arguments: arguments,
            cancellationToken: cancellationToken,
            title: "Active Directory: Client/Server Auth Posture (preview)",
            defaultErrorMessage: "Client/server authentication posture query failed.",
            maxTop: MaxViewTop,
            query: domainName => {
                var view = ClientServerAuthPostureService.EvaluateForDomainControllers(domainName);
                ThrowIfCollectionFailed(
                    view.CollectionSucceeded,
                    view.CollectionError,
                    "Client/server authentication posture query failed.");
                return view;
            },
            attributionSelector: static view => view.Attribution,
            resultFactory: static (request, view, scanned, truncated, rows) => new AdClientServerAuthPostureResult(
                DomainName: request.DomainName,
                IncludeAttribution: request.IncludeAttribution,
                ConfiguredAttributionOnly: request.ConfiguredAttributionOnly,
                Scanned: scanned,
                Truncated: truncated,
                LdapSigningRequired: view.LdapSigningRequired,
                LdapChannelBindingEnabled: view.LdapChannelBindingEnabled,
                SmbSigningServerRequired: view.SmbSigningServerRequired,
                SmbSigningClientRequired: view.SmbSigningClientRequired,
                LmCompatibilityLevel: view.LmCompatibilityLevel,
                NoLmHash: view.NoLmHash,
                NtlmSmb: view.NtlmSmb,
                RestrictNtlmSummary: view.RestrictNtlmSummary,
                LdapSigning: view.LdapSigning,
                LdapChannelBinding: view.LdapChannelBinding,
                Netlogon: view.Netlogon,
                RestrictAnonymous: view.RestrictAnonymous,
                NullSessionFallback: view.NullSessionFallback,
                NullSession: view.NullSession,
                SmbNtlmDetails: view.SmbNtlmDetails,
                Attribution: rows),
            additionalMetaMutate: static (meta, _, view, _) => {
                meta.Add("ldap_signing_required", view.LdapSigningRequired);
                meta.Add("ldap_channel_binding_enabled", view.LdapChannelBindingEnabled);
            }
            );
    }
}


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
/// Returns consolidated legacy CVE exposure posture for Domain Controllers in one domain (read-only).
/// </summary>
public sealed class AdLegacyCveExposureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_legacy_cve_exposure",
        "Assess legacy CVE-related exposure signals (SMBv1/NTLM/Netlogon/Kerberos posture) for Domain Controllers (read-only).",
        ToolSchema.Object(
                ("domain_name", ToolSchema.String("DNS domain name to evaluate.")),
                ("include_attribution", ToolSchema.Boolean("When true, include policy-attribution rows.")),
                ("configured_attribution_only", ToolSchema.Boolean("When true, omit attribution rows that are not configured.")),
                ("max_results", ToolSchema.Integer("Maximum attribution rows to return (capped).")))
            .WithTableViewOptions()
            .Required("domain_name")
            .NoAdditionalProperties());

    private sealed record AdLegacyCveExposureResult(
        string DomainName,
        bool IncludeAttribution,
        bool ConfiguredAttributionOnly,
        int Scanned,
        bool Truncated,
        bool SmbSigningWeak,
        bool Smb1Enabled,
        bool NetlogonMissingKeys,
        bool NetlogonRefuseComputerPasswordChange,
        LegacyCveExposureService.SmbSignals Smb,
        LegacyCveExposureService.NetlogonSignals Netlogon,
        LegacyCveExposureService.KerberosSignals Kerberos,
        IReadOnlyList<PolicyAttribution> Attribution);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdLegacyCveExposureTool"/> class.
    /// </summary>
    public AdLegacyCveExposureTool(ActiveDirectoryToolOptions options) : base(options) { }

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

        var view = LegacyCveExposureService.Get(domainName);
        var attributionRows = includeAttribution
            ? view.Attribution
                .Where(row => !configuredAttributionOnly || !string.IsNullOrWhiteSpace(row.Effective) && !string.Equals(row.Effective, "Not configured", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<PolicyAttribution>();

        var scanned = attributionRows.Length;
        IReadOnlyList<PolicyAttribution> projectedRows = scanned > maxResults
            ? attributionRows.Take(maxResults).ToArray()
            : attributionRows;
        var truncated = scanned > projectedRows.Count;

        var result = new AdLegacyCveExposureResult(
            DomainName: domainName,
            IncludeAttribution: includeAttribution,
            ConfiguredAttributionOnly: configuredAttributionOnly,
            Scanned: scanned,
            Truncated: truncated,
            SmbSigningWeak: view.Smb.ServerSigningRequired != true || view.Smb.ClientSigningRequired != true,
            Smb1Enabled: view.Smb.Smb1Disabled == false,
            NetlogonMissingKeys: view.Netlogon.MissingKeys > 0,
            NetlogonRefuseComputerPasswordChange: view.Netlogon.RefuseComputerPasswordChange,
            Smb: view.Smb,
            Netlogon: view.Netlogon,
            Kerberos: view.Kerberos,
            Attribution: projectedRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "attribution_view",
            title: "Active Directory: Legacy CVE Exposure Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("domain_name", domainName);
                meta.Add("include_attribution", includeAttribution);
                meta.Add("configured_attribution_only", configuredAttributionOnly);
                meta.Add("max_results", maxResults);
            });
        return Task.FromResult(response);
    }
}

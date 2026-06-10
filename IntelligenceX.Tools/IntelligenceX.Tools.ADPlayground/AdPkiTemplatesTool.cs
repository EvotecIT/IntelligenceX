using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CertNoob.Dashboard;
using CertNoob.Pki;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns PKI certificate template posture and takeover risk signals (read-only).
/// </summary>
public sealed class AdPkiTemplatesTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_pki_templates",
        "Get PKI certificate template posture with takeover and risky-configuration signals (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name. When omitted, uses current forest.")),
                ("weak_key_only", ToolSchema.Boolean("When true, keep only templates with weak minimal key size.")),
                ("takeover_risk_only", ToolSchema.Boolean("When true, keep only templates with takeover risk findings.")),
                ("code_signing_risk_only", ToolSchema.Boolean("When true, keep only templates with code-signing risk indicators.")),
                ("client_auth_risk_only", ToolSchema.Boolean("When true, keep only templates with client-auth risk indicators.")),
                ("include_takeover_rows", ToolSchema.Boolean("When true, include takeover detail rows in output payload. Default true.")),
                ("max_results", ToolSchema.Integer("Maximum template rows to return (capped).")),
                ("max_takeover_rows", ToolSchema.Integer("Maximum takeover detail rows to include (capped).")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record AdPkiTemplatesResult(
        string ForestName,
        int Scanned,
        bool Truncated,
        int TotalTemplates,
        int TakeoverCount,
        IReadOnlyList<AdPkiTemplateRow> Templates,
        IReadOnlyList<AdPkiToolSupport.PkiFindingRow> TakeoverRows);

    private sealed record AdPkiTemplateRow(
        string Name,
        string? DisplayName,
        string DistinguishedName,
        int? MinimalKeySize,
        int PublishedOnCount,
        IReadOnlyList<string> PublishedOn,
        bool WeakKey,
        bool TakeoverRisk,
        bool CodeSigningRisk,
        bool ClientAuthRisk,
        bool ExportableKey,
        bool AutoEnrollmentEnabled,
        bool EnrolleeSuppliesSubject,
        bool EnrolleeSuppliesSubjectAltName,
        bool PendAllRequests,
        bool RequireKeyArchival,
        string? MaxSeverity,
        int VulnerabilityCount,
        IReadOnlyList<string> VulnerabilityTypes,
        IReadOnlyList<string> ExtendedKeyUsages,
        int EnrollPrincipalCount,
        int AutoEnrollPrincipalCount,
        int FullControlPrincipalCount);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPkiTemplatesTool"/> class.
    /// </summary>
    public AdPkiTemplatesTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var weakKeyOnly = ToolArgs.GetBoolean(arguments, "weak_key_only", defaultValue: false);
        var takeoverRiskOnly = ToolArgs.GetBoolean(arguments, "takeover_risk_only", defaultValue: false);
        var codeSigningRiskOnly = ToolArgs.GetBoolean(arguments, "code_signing_risk_only", defaultValue: false);
        var clientAuthRiskOnly = ToolArgs.GetBoolean(arguments, "client_auth_risk_only", defaultValue: false);
        var includeTakeoverRows = ToolArgs.GetBoolean(arguments, "include_takeover_rows", defaultValue: true);
        var maxResults = ResolveMaxResults(arguments);
        var maxTakeoverRows = ResolveMaxResults(arguments, "max_takeover_rows");

        if (!TryExecute(
                action: () => AdPkiToolSupport.BuildAssessment(
                    forestName,
                    includeEnrollmentPolicyEntries: false),
                result: out PkiAssessmentSnapshot snapshot,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "PKI template query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var templates = snapshot.Dashboard?.Templates ?? new List<TemplateRiskView>();
        var findings = AdPkiToolSupport.BuildFindings(snapshot);
        var takeoverFindings = findings
            .Where(IsTemplateTakeoverFinding)
            .ToArray();

        var filtered = templates
            .Select(ToTemplateRow)
            .Where(item => !weakKeyOnly || item.WeakKey)
            .Where(item => !takeoverRiskOnly || item.TakeoverRisk)
            .Where(item => !codeSigningRiskOnly || item.CodeSigningRisk)
            .Where(item => !clientAuthRiskOnly || item.ClientAuthRisk)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<AdPkiTemplateRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        IReadOnlyList<AdPkiToolSupport.PkiFindingRow> projectedTakeoverRows = includeTakeoverRows
            ? AdPkiToolSupport.ToFindingRows(takeoverFindings, maxTakeoverRows)
            : Array.Empty<AdPkiToolSupport.PkiFindingRow>();

        var forest = AdPkiToolSupport.ResolveScopeName(snapshot, forestName);

        var result = new AdPkiTemplatesResult(
            ForestName: forest,
            Scanned: scanned,
            Truncated: truncated,
            TotalTemplates: templates.Count,
            TakeoverCount: takeoverFindings.Length,
            Templates: projectedRows,
            TakeoverRows: projectedTakeoverRows);

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "templates_view",
            title: "Active Directory: PKI Templates (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("forest_name", forest);
                AddMaxResultsMeta(meta, maxResults);
                meta.Add("max_takeover_rows", maxTakeoverRows);
                meta.Add("weak_key_only", weakKeyOnly);
                meta.Add("takeover_risk_only", takeoverRiskOnly);
                meta.Add("code_signing_risk_only", codeSigningRiskOnly);
                meta.Add("client_auth_risk_only", clientAuthRiskOnly);
                meta.Add("include_takeover_rows", includeTakeoverRows);
            }));
    }

    private static AdPkiTemplateRow ToTemplateRow(TemplateRiskView template) {
        var weakKey = AdPkiToolSupport.IsWeakKeyTemplate(template);
        var takeoverRisk = AdPkiToolSupport.IsTakeoverRiskTemplate(template);
        var codeSigningRisk = AdPkiToolSupport.IsCodeSigningTemplate(template);
        var clientAuthRisk = AdPkiToolSupport.IsAuthenticationCapableTemplate(template);

        return new AdPkiTemplateRow(
            Name: template.Name,
            DisplayName: template.DisplayName,
            DistinguishedName: template.DistinguishedName,
            MinimalKeySize: template.MinimalKeySize,
            PublishedOnCount: template.PublishedOnCount,
            PublishedOn: template.PublishedOn.ToArray(),
            WeakKey: weakKey,
            TakeoverRisk: takeoverRisk,
            CodeSigningRisk: codeSigningRisk,
            ClientAuthRisk: clientAuthRisk,
            ExportableKey: template.ExportableKey,
            AutoEnrollmentEnabled: template.AutoEnrollmentEnabled,
            EnrolleeSuppliesSubject: template.EnrolleeSuppliesSubject,
            EnrolleeSuppliesSubjectAltName: template.EnrolleeSuppliesSubjectAltName,
            PendAllRequests: template.PendAllRequests,
            RequireKeyArchival: template.RequireKeyArchival,
            MaxSeverity: template.MaxSeverity?.ToString(),
            VulnerabilityCount: template.VulnerabilityCount,
            VulnerabilityTypes: template.VulnerabilityTypes.Select(t => t.ToString()).ToArray(),
            ExtendedKeyUsages: template.ExtendedKeyUsages.ToArray(),
            EnrollPrincipalCount: template.EnrollPrincipalCount,
            AutoEnrollPrincipalCount: template.AutoEnrollPrincipalCount,
            FullControlPrincipalCount: template.FullControlPrincipalCount);
    }

    private static bool IsTemplateTakeoverFinding(PkiFinding finding) =>
        finding.Tags.Any(tag => string.Equals(tag, "Template", StringComparison.OrdinalIgnoreCase)) &&
        (finding.Severity >= PkiFindingSeverity.Medium ||
         string.Equals(finding.Id, PkiFindingIds.TemplateBroadEnrollmentRights, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(finding.Id, PkiFindingIds.TemplateModifiableByNonPrivileged, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(finding.Id, PkiFindingIds.TemplateOwnedByNonPrivileged, StringComparison.OrdinalIgnoreCase));
}

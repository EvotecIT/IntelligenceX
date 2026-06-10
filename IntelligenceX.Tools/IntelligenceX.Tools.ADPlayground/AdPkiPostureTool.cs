using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CertNoob.Pki;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns consolidated PKI posture findings (ROCA availability, weak crypto, enrollment endpoint HTTPS) for a forest (read-only).
/// </summary>
public sealed class AdPkiPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_pki_posture",
        "Get consolidated PKI posture findings (ROCA availability, weak crypto, insecure enrollment endpoints) for a forest (read-only).",
        ToolSchema.Object(
                ("forest_name", ToolSchema.String("Optional forest DNS name. When omitted, uses current forest.")),
                ("include_details", ToolSchema.Boolean("When true, include detailed finding rows in output payload. Default true.")),
                ("insecure_endpoints_only", ToolSchema.Boolean("When true, include only insecure (HTTP) enrollment endpoint details.")),
                ("max_details_per_category", ToolSchema.Integer("Maximum detail rows per finding category (capped). Default 200.")))
            .WithTableViewOptions()
            .NoAdditionalProperties());

    private sealed record PkiPostureSummaryRow(
        string Category,
        int Count,
        string Severity,
        string Notes);

    private sealed record AdPkiPostureResult(
        string ForestName,
        bool IncludeDetails,
        bool InsecureEndpointsOnly,
        IReadOnlyList<PkiPostureSummaryRow> Summary,
        IReadOnlyList<AdPkiToolSupport.PkiFindingRow> RocaConfirmed,
        IReadOnlyList<AdPkiToolSupport.PkiFindingRow> RocaSuspected,
        IReadOnlyList<AdPkiToolSupport.PkiFindingRow> WeakRsaComponents,
        IReadOnlyList<AdPkiToolSupport.PkiEndpointRow> InsecureEnrollmentEndpoints,
        IReadOnlyList<AdPkiToolSupport.PkiEndpointRow> EnrollmentEndpoints);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPkiPostureTool"/> class.
    /// </summary>
    public AdPkiPostureTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var forestName = ToolArgs.GetOptionalTrimmed(arguments, "forest_name");
        var includeDetails = ToolArgs.GetBoolean(arguments, "include_details", defaultValue: true);
        var insecureEndpointsOnly = ToolArgs.GetBoolean(arguments, "insecure_endpoints_only", defaultValue: false);
        var maxDetailsPerCategory = ToolArgs.GetCappedInt32(arguments, "max_details_per_category", 200, 1, Options.MaxResults);

        if (!TryExecute(
                action: () => AdPkiToolSupport.BuildAssessment(
                    forestName,
                    includeEnrollmentPolicyEntries: true,
                    includeIssuedRequestSample: true),
                result: out PkiAssessmentSnapshot snapshot,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "PKI posture query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var forest = AdPkiToolSupport.ResolveScopeName(snapshot, forestName);
        var findings = AdPkiToolSupport.BuildFindings(snapshot);
        var weakCryptoFindings = findings
            .Where(AdPkiToolSupport.IsWeakRsaOrCryptoFinding)
            .ToArray();
        var endpointRows = AdPkiToolSupport.EnumerateEndpointRows(snapshot);
        var insecureEndpointRows = endpointRows
            .Where(endpoint => endpoint.Insecure)
            .ToArray();
        var httpEndpointFindings = findings
            .Where(finding => string.Equals(finding.Id, PkiFindingIds.EnrollmentPolicyEndpointUsesHttp, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var summaryRows = new List<PkiPostureSummaryRow> {
            new(
                Category: "roca_confirmed",
                Count: 0,
                Severity: "info",
                Notes: RocaDetector.IsAvailable
                    ? "ROCA dataset is available, but CertNoob now exposes PKI assessment findings rather than legacy confirmed-item rows."
                    : "ROCA dataset unavailable; set TESTIMOX_ROCA_DATASET, CERTNOOB_ROCA_DATASET, or ADP_ROCA_DATASET to enable checks where supported."),
            new(
                Category: "roca_suspected",
                Count: 0,
                Severity: "info",
                Notes: "Legacy suspected-item rows are no longer produced by the CertNoob assessment surface."),
            new(
                Category: "weak_rsa_components",
                Count: weakCryptoFindings.Sum(finding => Math.Max(finding.AffectedCount, 1)),
                Severity: ResolveHighestSeverity(weakCryptoFindings),
                Notes: "Weak RSA and related weak-crypto indicators from CertNoob findings."),
            new(
                Category: "enrollment_endpoints_insecure",
                Count: insecureEndpointRows.Length,
                Severity: httpEndpointFindings.Length > 0 ? ResolveHighestSeverity(httpEndpointFindings) : "info",
                Notes: "HTTP enrollment endpoints should be remediated to HTTPS.")
        };

        var insecureEndpointDetails = insecureEndpointRows
            .Take(maxDetailsPerCategory)
            .ToArray();
        var endpointDetails = insecureEndpointsOnly
            ? insecureEndpointDetails
            : endpointRows.Take(maxDetailsPerCategory).ToArray();

        var result = new AdPkiPostureResult(
            ForestName: forest,
            IncludeDetails: includeDetails,
            InsecureEndpointsOnly: insecureEndpointsOnly,
            Summary: summaryRows,
            RocaConfirmed: includeDetails
                ? Array.Empty<AdPkiToolSupport.PkiFindingRow>()
                : Array.Empty<AdPkiToolSupport.PkiFindingRow>(),
            RocaSuspected: includeDetails
                ? Array.Empty<AdPkiToolSupport.PkiFindingRow>()
                : Array.Empty<AdPkiToolSupport.PkiFindingRow>(),
            WeakRsaComponents: includeDetails
                ? AdPkiToolSupport.ToFindingRows(weakCryptoFindings, maxDetailsPerCategory)
                : Array.Empty<AdPkiToolSupport.PkiFindingRow>(),
            InsecureEnrollmentEndpoints: includeDetails
                ? insecureEndpointDetails
                : Array.Empty<AdPkiToolSupport.PkiEndpointRow>(),
            EnrollmentEndpoints: includeDetails
                ? endpointDetails
                : Array.Empty<AdPkiToolSupport.PkiEndpointRow>());

        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: result,
            sourceRows: summaryRows,
            viewRowsPath: "summary_view",
            title: "Active Directory: PKI Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            scanned: summaryRows.Count,
            metaMutate: meta => {
                meta.Add("forest_name", forest);
                meta.Add("include_details", includeDetails);
                meta.Add("insecure_endpoints_only", insecureEndpointsOnly);
                meta.Add("max_details_per_category", maxDetailsPerCategory);
            }));
    }

    private static string ResolveHighestSeverity(IReadOnlyList<PkiFinding> findings) {
        if (findings.Count == 0) {
            return "info";
        }

        return findings
            .OrderByDescending(finding => finding.Severity)
            .First()
            .Severity
            .ToString()
            .ToLowerInvariant();
    }
}


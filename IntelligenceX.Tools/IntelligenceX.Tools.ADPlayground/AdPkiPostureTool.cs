using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Pki;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns consolidated PKI posture findings (ROCA/weak RSA/enrollment endpoint HTTPS) for a forest (read-only).
/// </summary>
public sealed class AdPkiPostureTool : ActiveDirectoryToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_pki_posture",
        "Get consolidated PKI posture findings (ROCA confirmed/suspected, weak RSA, insecure enrollment endpoints) for a forest (read-only).",
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
        IReadOnlyList<RocaConfirmedEvaluator.Item> RocaConfirmed,
        IReadOnlyList<RocaSuspectedEvaluator.Item> RocaSuspected,
        IReadOnlyList<WeakRsaComponentEvaluator.Item> WeakRsaComponents,
        IReadOnlyList<EnrollmentHttpsRequiredEvaluator.Endpoint> InsecureEnrollmentEndpoints,
        IReadOnlyList<EnrollmentHttpsRequiredEvaluator.Endpoint> EnrollmentEndpoints);

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

        PkiConfiguration posture;
        try {
            posture = PkiApi.GetPosture(forestName);
        } catch (Exception ex) {
            return Task.FromResult(ToolResponse.Error("query_failed", $"PKI posture query failed: {ex.Message}"));
        }

        var forest = posture.RocaConfirmed.ForestName;
        if (string.IsNullOrWhiteSpace(forest)) {
            forest = posture.RocaSuspected.ForestName;
        }
        if (string.IsNullOrWhiteSpace(forest)) {
            forest = posture.WeakRsaComponents.ForestName;
        }
        if (string.IsNullOrWhiteSpace(forest)) {
            forest = posture.EnrollmentEndpoints.ForestName;
        }
        forest ??= forestName ?? string.Empty;

        var summaryRows = new List<PkiPostureSummaryRow> {
            new(
                Category: "roca_confirmed",
                Count: posture.RocaConfirmed.Confirmed.Count,
                Severity: posture.RocaConfirmed.Confirmed.Count > 0 ? "critical" : "info",
                Notes: posture.RocaConfirmed.DatasetAvailable
                    ? "Dataset-based ROCA confirmation."
                    : "ROCA dataset unavailable; confirmed list may be empty."),
            new(
                Category: "roca_suspected",
                Count: posture.RocaSuspected.Items.Count,
                Severity: posture.RocaSuspected.Items.Count > 0 ? "high" : "info",
                Notes: "Heuristic ROCA indicators."),
            new(
                Category: "weak_rsa_components",
                Count: posture.WeakRsaComponents.Items.Count,
                Severity: posture.WeakRsaComponents.Items.Count > 0 ? "medium" : "info",
                Notes: "Weak RSA exponent/parameter indicators."),
            new(
                Category: "enrollment_endpoints_insecure",
                Count: posture.EnrollmentEndpoints.Insecure.Count,
                Severity: posture.EnrollmentEndpoints.Insecure.Count > 0 ? "high" : "info",
                Notes: "HTTP enrollment endpoints should be remediated to HTTPS.")
        };

        var insecureEndpointDetails = posture.EnrollmentEndpoints.Insecure
            .Take(maxDetailsPerCategory)
            .ToArray();
        var endpointDetails = insecureEndpointsOnly
            ? insecureEndpointDetails
            : posture.EnrollmentEndpoints.Endpoints.Take(maxDetailsPerCategory).ToArray();

        var result = new AdPkiPostureResult(
            ForestName: forest,
            IncludeDetails: includeDetails,
            InsecureEndpointsOnly: insecureEndpointsOnly,
            Summary: summaryRows,
            RocaConfirmed: includeDetails
                ? posture.RocaConfirmed.Confirmed.Take(maxDetailsPerCategory).ToArray()
                : Array.Empty<RocaConfirmedEvaluator.Item>(),
            RocaSuspected: includeDetails
                ? posture.RocaSuspected.Items.Take(maxDetailsPerCategory).ToArray()
                : Array.Empty<RocaSuspectedEvaluator.Item>(),
            WeakRsaComponents: includeDetails
                ? posture.WeakRsaComponents.Items.Take(maxDetailsPerCategory).ToArray()
                : Array.Empty<WeakRsaComponentEvaluator.Item>(),
            InsecureEnrollmentEndpoints: includeDetails
                ? insecureEndpointDetails
                : Array.Empty<EnrollmentHttpsRequiredEvaluator.Endpoint>(),
            EnrollmentEndpoints: includeDetails
                ? endpointDetails
                : Array.Empty<EnrollmentHttpsRequiredEvaluator.Endpoint>());

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: summaryRows,
            viewRowsPath: "summary_view",
            title: "Active Directory: PKI Posture (preview)",
            maxTop: MaxViewTop,
            baseTruncated: false,
            response: out var response,
            scanned: summaryRows.Count,
            metaMutate: meta => {
                meta.Add("forest_name", forest);
                meta.Add("include_details", includeDetails);
                meta.Add("insecure_endpoints_only", insecureEndpointsOnly);
                meta.Add("max_details_per_category", maxDetailsPerCategory);
            });
        return Task.FromResult(response);
    }
}

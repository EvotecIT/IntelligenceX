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
        IReadOnlyList<PkiTemplatesEvaluator.TemplateRow> Templates,
        IReadOnlyList<PkiTemplatesEvaluator.TakeoverRow> TakeoverRows);

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
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", Options.MaxResults, 1, Options.MaxResults);
        var maxTakeoverRows = ToolArgs.GetCappedInt32(arguments, "max_takeover_rows", Options.MaxResults, 1, Options.MaxResults);

        if (!TryExecute(
                action: () => PkiApi.GetTemplates(forestName),
                result: out PkiTemplatesEvaluator.View view,
                errorResponse: out var errorResponse,
                defaultErrorMessage: "PKI template query failed.",
                invalidOperationErrorCode: "query_failed")) {
            return Task.FromResult(errorResponse!);
        }

        var filtered = view.Items
            .Where(item => !weakKeyOnly || item.WeakKey)
            .Where(item => !takeoverRiskOnly || item.TakeoverRisk)
            .Where(item => !codeSigningRiskOnly || item.CodeSigningRisk)
            .Where(item => !clientAuthRiskOnly || item.ClientAuthRisk)
            .ToArray();

        var scanned = filtered.Length;
        IReadOnlyList<PkiTemplatesEvaluator.TemplateRow> projectedRows = scanned > maxResults
            ? filtered.Take(maxResults).ToArray()
            : filtered;
        var truncated = scanned > projectedRows.Count;

        IReadOnlyList<PkiTemplatesEvaluator.TakeoverRow> projectedTakeoverRows = includeTakeoverRows
            ? view.Takeover.Take(maxTakeoverRows).ToArray()
            : Array.Empty<PkiTemplatesEvaluator.TakeoverRow>();

        var result = new AdPkiTemplatesResult(
            ForestName: view.ForestName,
            Scanned: scanned,
            Truncated: truncated,
            TotalTemplates: view.Items.Count,
            TakeoverCount: view.Takeover.Count,
            Templates: projectedRows,
            TakeoverRows: projectedTakeoverRows);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: result,
            sourceRows: projectedRows,
            viewRowsPath: "templates_view",
            title: "Active Directory: PKI Templates (preview)",
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scanned,
            metaMutate: meta => {
                meta.Add("forest_name", view.ForestName);
                meta.Add("max_results", maxResults);
                meta.Add("max_takeover_rows", maxTakeoverRows);
                meta.Add("weak_key_only", weakKeyOnly);
                meta.Add("takeover_risk_only", takeoverRiskOnly);
                meta.Add("code_signing_risk_only", codeSigningRiskOnly);
                meta.Add("client_auth_risk_only", clientAuthRiskOnly);
                meta.Add("include_takeover_rows", includeTakeoverRows);
            });
        return Task.FromResult(response);
    }
}



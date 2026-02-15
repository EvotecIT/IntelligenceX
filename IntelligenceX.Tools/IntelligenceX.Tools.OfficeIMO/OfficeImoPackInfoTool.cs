using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Returns OfficeIMO pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class OfficeImoPackInfoTool : OfficeImoToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "officeimo_pack_info",
        "Return OfficeIMO ingestion pack capabilities, allowed-root behavior, output contract, and recommended usage patterns.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="OfficeImoPackInfoTool"/> class.
    /// </summary>
    public OfficeImoPackInfoTool(OfficeImoToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "officeimo",
            engine: "OfficeIMO.Reader",
            tools: ToolRegistryOfficeImoExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use officeimo_pack_info to confirm caps and supported formats.",
                "Use officeimo_read on a file or folder to extract normalized chunks for reasoning.",
                "Reason from raw payload fields (chunks/text/tables). Use summary markdown only as a preview."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Confirm limits and scope",
                    suggestedTools: new[] { "officeimo_pack_info" },
                    notes: "All reads are restricted to AllowedRoots."),
                ToolPackGuidance.FlowStep(
                    goal: "Extract evidence from Office documents",
                    suggestedTools: new[] { "officeimo_read" },
                    notes: "Prefer passing a folder to ingest a document set; use max_files/max_total_bytes to bound work.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "office_ingestion",
                    summary: "Extract text/markdown/tables from Word/Excel/PowerPoint/Markdown files into AI-friendly chunks.",
                    primaryTools: new[] { "officeimo_read" }),
                ToolPackGuidance.Capability(
                    id: "bounded_access",
                    summary: "Constrain all reads to configured allowed roots and size caps.",
                    primaryTools: new[] { "officeimo_pack_info", "officeimo_read" })
            },
            toolCatalog: ToolRegistryOfficeImoExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Chunks are returned as structured raw payload for full model reasoning (text/markdown/location/tables).",
            viewProjectionPolicy: "Projection arguments are optional and view-only. This pack does not currently expose projection arguments; reason from raw payload fields.",
            safety: new {
                AllowedRootsCount = Options.AllowedRoots.Count,
                Note = "All path operations are constrained to AllowedRoots."
            },
            limits: new {
                Options.MaxFiles,
                Options.MaxTotalBytes,
                Options.MaxInputBytes
            });

        var summary = ToolMarkdown.SummaryText(
            title: "OfficeIMO Pack",
            "Use raw payload fields for reasoning. Use `summary_markdown` only as a preview.");

        return Task.FromResult(ToolResponse.OkModel(root, summaryMarkdown: summary));
    }
}

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
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "officeimo_pack_info",
        description: "Return OfficeIMO ingestion pack capabilities, allowed-root behavior, output contract, and recommended usage patterns.",
        packId: "officeimo");

    /// <summary>
    /// Initializes a new instance of the <see cref="OfficeImoPackInfoTool"/> class.
    /// </summary>
    public OfficeImoPackInfoTool(OfficeImoToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<PackInfoRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<PackInfoRequest>.Success(new PackInfoRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<PackInfoRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "officeimo",
            engine: "OfficeIMO.Reader",
            tools: ToolRegistryOfficeImoExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use officeimo_pack_info to confirm caps and supported formats.",
                "Use officeimo_read on a file or folder to extract normalized chunks/documents.",
                "For indexing pipelines, set output_mode='documents' (or 'both') and upsert by source_id/source_hash + chunk_hash.",
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
                    notes: "Prefer passing a folder to ingest a document set; use max_files/max_total_bytes to bound work."),
                ToolPackGuidance.FlowStep(
                    goal: "Build incremental document index",
                    suggestedTools: new[] { "officeimo_read" },
                    notes: "Use output_mode='documents' and include_document_chunks=true to get per-source hashes/chunks for direct DB upserts.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "office_ingestion",
                    summary: "Extract text/markdown/tables from Word/Excel/PowerPoint/Markdown/PDF files into AI-friendly chunks and source-level documents.",
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

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }
}

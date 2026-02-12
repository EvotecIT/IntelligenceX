using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Returns filesystem pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class FileSystemPackInfoTool : FileSystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "fs_pack_info",
        "Return filesystem pack capabilities, allowed-root behavior, output contract, and recommended usage patterns.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="FileSystemPackInfoTool"/> class.
    /// </summary>
    public FileSystemPackInfoTool(FileSystemToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "filesystem",
            engine: "ComputerX.FileSystem",
            tools: ToolRegistryFileSystemExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use fs_list to discover candidate files/folders.",
                "Use fs_search for broad pattern discovery across files.",
                "Use fs_read for final evidence extraction from selected files."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover candidate paths",
                    suggestedTools: new[] { "fs_list", "fs_search" }),
                ToolPackGuidance.FlowStep(
                    goal: "Extract evidence content",
                    suggestedTools: new[] { "fs_read" }),
                ToolPackGuidance.FlowStep(
                    goal: "Apply display projection when needed",
                    suggestedTools: new[] { "fs_list", "fs_search" },
                    notes: "Use columns/sort/top only for presentation; reason from raw payload fields.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "path_discovery",
                    summary: "Enumerate files/directories and search file contents within allowed roots.",
                    primaryTools: new[] { "fs_list", "fs_search" }),
                ToolPackGuidance.Capability(
                    id: "content_extraction",
                    summary: "Read text files with configured caps for safe evidence extraction.",
                    primaryTools: new[] { "fs_read" }),
                ToolPackGuidance.Capability(
                    id: "bounded_access",
                    summary: "Constrain all filesystem operations to configured allowed roots.",
                    primaryTools: new[] { "fs_pack_info", "fs_list", "fs_search", "fs_read" })
            },
            toolCatalog: ToolRegistryFileSystemExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Raw arrays (entries/matches) are preserved for full model reasoning.",
            viewProjectionPolicy: "Projection arguments are view-only and intended for display shaping (columns/sort_by/sort_direction/top).",
            safety: new {
                AllowedRootsCount = Options.AllowedRoots.Count,
                Note = "All path operations are constrained to AllowedRoots."
            },
            limits: new {
                MaxResults = Options.MaxResults,
                MaxReadBytes = Options.MaxReadBytes,
                MaxSearchFileBytes = Options.MaxSearchFileBytes
            });

        var summary = ToolMarkdown.SummaryText(
            title: "FileSystem Pack",
            "Use raw payload fields for reasoning. Use `*_view` only for presentation.");

        return Task.FromResult(ToolResponse.OkModel(root, summaryMarkdown: summary));
    }
}

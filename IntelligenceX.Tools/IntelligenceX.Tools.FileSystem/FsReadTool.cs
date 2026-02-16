using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Engines.FileSystem;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Reads a text file (safe-by-default; requires AllowedRoots).
/// </summary>
public sealed class FsReadTool : FileSystemToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "fs_read",
        "Read a UTF-8 text file from disk (restricted to allowed roots).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Path to the file (absolute or relative).")),
                ("max_bytes", ToolSchema.Integer("Optional maximum bytes to read.")))
            .Required("path")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="FsReadTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public FsReadTool(FileSystemToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON string result.</returns>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var path = arguments?.GetString("path") ?? string.Empty;
        if (!TryResolveExistingFile(path, out var fullPath, out var pathError)) {
            return Task.FromResult(pathError);
        }

        var maxBytes = ToolArgs.GetCappedInt64(arguments, "max_bytes", Options.MaxReadBytes, 1, Options.MaxReadBytes);
        var root = FileSystemQuery.ReadText(
            request: new FileTextReadRequest {
                Path = fullPath,
                MaxBytes = maxBytes
            },
            cancellationToken: cancellationToken);

        var preview = root.Text;
        const int previewMax = 2000;
        if (preview.Length > previewMax) {
            preview = preview.Substring(0, previewMax) + "...";
        }

        var meta = ToolOutputHints.Meta(count: 1, truncated: root.Truncated)
            .Add("bytes_read", root.BytesRead)
            .Add("max_bytes", maxBytes);

        var summaryMarkdown = ToolMarkdown.SummaryFacts(
            title: "File read (preview)",
            facts: new (string Key, string Value)[] {
                ("Path", fullPath),
                ("Bytes read", root.BytesRead.ToString()),
                ("Truncated", root.Truncated ? "yes" : "no")
            },
            codeLanguage: "text",
            codeContent: preview);

        return Task.FromResult(ToolResponse.OkModel(
            model: root,
            meta: meta,
            summaryMarkdown: summaryMarkdown,
            render: ToolOutputHints.RenderCode(language: "text", contentPath: "text")));
    }
}

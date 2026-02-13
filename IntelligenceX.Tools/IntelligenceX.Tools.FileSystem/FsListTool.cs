using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ComputerX.FileSystem;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Lists directory entries (safe-by-default; requires AllowedRoots).
/// </summary>
public sealed class FsListTool : FileSystemToolBase, ITool {
    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "fs_list",
        "List directory entries (restricted to allowed roots).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Directory path (absolute or relative).")),
                ("recursive", ToolSchema.Boolean("Whether to list recursively.")),
                ("include_files", ToolSchema.Boolean("Include files (default true).")),
                ("include_dirs", ToolSchema.Boolean("Include directories (default true).")))
            .WithTableViewOptions()
            .Required("path")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="FsListTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public FsListTool(FileSystemToolOptions options) : base(options) { }

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
        if (!TryResolveExistingDirectory(path, out var fullPath, out var pathError)) {
            return Task.FromResult(pathError);
        }

        var recursive = arguments?.GetBoolean("recursive") ?? false;
        var includeFiles = arguments?.GetBoolean("include_files") ?? true;
        var includeDirs = arguments?.GetBoolean("include_dirs") ?? true;

        var root = FileSystemQuery.List(
            request: new FileSystemListRequest {
                Path = fullPath,
                Recursive = recursive,
                IncludeFiles = includeFiles,
                IncludeDirectories = includeDirs,
                MaxResults = Options.MaxResults
            },
            canDescendOrIncludePath: candidate => TryResolvePath(candidate, out _, out _),
            cancellationToken: cancellationToken);

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: root,
            sourceRows: root.Entries,
            viewRowsPath: "entries_view",
            title: "Directory entries (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root.Truncated,
            response: out var response);
        return Task.FromResult(response);
    }
}


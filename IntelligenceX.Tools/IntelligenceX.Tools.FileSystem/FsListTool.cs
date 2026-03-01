using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Engines.FileSystem;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Lists directory entries (safe-by-default; requires AllowedRoots).
/// </summary>
public sealed class FsListTool : FileSystemToolBase, ITool {
    private sealed record ListRequest(
        string Path,
        bool Recursive,
        bool IncludeFiles,
        bool IncludeDirs);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<ListRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("path", out var path, out var pathError)) {
                return ToolRequestBindingResult<ListRequest>.Failure(pathError);
            }

            return ToolRequestBindingResult<ListRequest>.Success(new ListRequest(
                Path: path,
                Recursive: reader.Boolean("recursive", defaultValue: false),
                IncludeFiles: reader.Boolean("include_files", defaultValue: true),
                IncludeDirs: reader.Boolean("include_dirs", defaultValue: true)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<ListRequest> context, CancellationToken cancellationToken) {
        if (!TryResolveExistingDirectory(context.Request.Path, out var fullPath, out var pathError)) {
            return Task.FromResult(pathError);
        }

        var root = FileSystemQuery.List(
            request: new FileSystemListRequest {
                Path = fullPath,
                Recursive = context.Request.Recursive,
                IncludeFiles = context.Request.IncludeFiles,
                IncludeDirectories = context.Request.IncludeDirs,
                MaxResults = Options.MaxResults
            },
            canDescendOrIncludePath: candidate => TryResolvePath(candidate, out _, out _),
            cancellationToken: cancellationToken);

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: root,
            sourceRows: root.Entries,
            viewRowsPath: "entries_view",
            title: "Directory entries (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root.Truncated);
        return Task.FromResult(response);
    }
}

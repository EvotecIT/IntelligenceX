using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Engines.FileSystem;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Searches text files under a directory for a regex pattern (safe-by-default; requires AllowedRoots).
/// </summary>
public sealed class FsSearchTool : FileSystemToolBase, ITool {
    private sealed record SearchRequest(
        string Path,
        string Pattern,
        bool CaseSensitive,
        int MaxMatches);

    private const int MaxViewTop = 5000;

    private static readonly ToolDefinition DefinitionValue = new(
        "fs_search",
        "Search text files under a directory for a regex pattern (restricted to allowed roots).",
        ToolSchema.Object(
                ("path", ToolSchema.String("Directory path (absolute or relative).")),
                ("pattern", ToolSchema.String("Regex pattern to search for.")),
                ("case_sensitive", ToolSchema.Boolean("Case sensitive search (default false).")),
                ("max_matches", ToolSchema.Integer("Optional maximum matches to return.")))
            .WithTableViewOptions()
            .Required("path", "pattern")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="FsSearchTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public FsSearchTool(FileSystemToolOptions options) : base(options) { }

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

    private ToolRequestBindingResult<SearchRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("path", out var path, out var pathError)) {
                return ToolRequestBindingResult<SearchRequest>.Failure(pathError);
            }

            if (!reader.TryReadRequiredString("pattern", out var pattern, out var patternError)) {
                return ToolRequestBindingResult<SearchRequest>.Failure(patternError);
            }

            return ToolRequestBindingResult<SearchRequest>.Success(new SearchRequest(
                Path: path,
                Pattern: pattern,
                CaseSensitive: reader.Boolean("case_sensitive", defaultValue: false),
                MaxMatches: reader.CappedInt32(
                    key: "max_matches",
                    defaultValue: Options.MaxResults,
                    minInclusive: 1,
                    maxInclusive: Options.MaxResults)));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<SearchRequest> context, CancellationToken cancellationToken) {
        if (!TryResolveExistingDirectory(context.Request.Path, out var fullPath, out var pathError)) {
            return Task.FromResult(pathError);
        }

        FileTextSearchResult root;
        try {
            root = FileSystemQuery.SearchText(
                request: new FileTextSearchRequest {
                    Path = fullPath,
                    Pattern = context.Request.Pattern,
                    CaseSensitive = context.Request.CaseSensitive,
                    MaxMatches = context.Request.MaxMatches,
                    MaxFileBytes = Options.MaxSearchFileBytes
                },
                canDescendOrIncludePath: candidate => TryResolvePath(candidate, out _, out _),
                cancellationToken: cancellationToken);
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResultV2.Error("invalid_argument", $"Invalid regex: {ex.Message}"));
        }

        var response = ToolResultV2.OkAutoTableResponse(
            arguments: context.Arguments,
            model: root,
            sourceRows: root.Matches,
            viewRowsPath: "matches_view",
            title: "Search matches (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root.Truncated);
        return Task.FromResult(response);
    }
}

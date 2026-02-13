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
/// Searches text files under a directory for a regex pattern (safe-by-default; requires AllowedRoots).
/// </summary>
public sealed class FsSearchTool : FileSystemToolBase, ITool {
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
        var path = arguments?.GetString("path") ?? string.Empty;
        var pattern = arguments?.GetString("pattern") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pattern)) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", "Pattern is required."));
        }

        if (!TryResolveExistingDirectory(path, out var fullPath, out var pathError)) {
            return Task.FromResult(pathError);
        }

        var caseSensitive = arguments?.GetBoolean("case_sensitive") ?? false;
        var maxMatches = ToolArgs.GetCappedInt32(arguments, "max_matches", Options.MaxResults, 1, Options.MaxResults);

        FileTextSearchResult root;
        try {
            root = FileSystemQuery.SearchText(
                request: new FileTextSearchRequest {
                    Path = fullPath,
                    Pattern = pattern,
                    CaseSensitive = caseSensitive,
                    MaxMatches = maxMatches,
                    MaxFileBytes = Options.MaxSearchFileBytes
                },
                canDescendOrIncludePath: candidate => TryResolvePath(candidate, out _, out _),
                cancellationToken: cancellationToken);
        } catch (ArgumentException ex) {
            return Task.FromResult(ToolResponse.Error("invalid_argument", $"Invalid regex: {ex.Message}"));
        }

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: root,
            sourceRows: root.Matches,
            viewRowsPath: "matches_view",
            title: "Search matches (preview)",
            maxTop: MaxViewTop,
            baseTruncated: root.Truncated,
            response: out var response);
        return Task.FromResult(response);
    }
}


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Finds local EVTX files under allowed roots (read-only, bounded).
/// Useful when the user asks for event log data but only has (or may have) exported EVTX files available.
/// </summary>
public sealed class EventLogEvtxFindTool : EventLogToolBase, ITool {
    private const int MaxDepth = 6;
    private const int MaxDirsScanned = 5000;
    private const int MaxFilesScanned = 8000;
    private const int MaxDefaultResults = 20;
    private const int MaxMaxResults = 80;

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_evtx_find",
        "Find local .evtx files under allowed roots (read-only, bounded scan). Use this when you have exported EVTX logs but don't know the exact path.",
        ToolSchema.Object(
                ("query", ToolSchema.String("Optional case-insensitive filename/path filter. If it contains whitespace, all tokens must match.")),
                ("log_name", ToolSchema.String("Optional log hint (for example: System, Security, Application).")),
                ("max_results", ToolSchema.Integer("Optional maximum files to return (capped).")))
            .NoAdditionalProperties());

    private sealed record EvtxFindFile(
        string Path,
        string FileName,
        long SizeBytes,
        DateTime LastWriteTimeUtc);

    private sealed record EvtxFindResult(
        IReadOnlyList<EvtxFindFile> Files,
        int ScannedDirectories,
        int ScannedFiles,
        bool Truncated);

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogEvtxFindTool"/> class.
    /// </summary>
    public EventLogEvtxFindTool(EventLogToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        // Directory enumeration is synchronous; offload to avoid blocking the tool runner thread.
        return Task.Run(() => InvokeCore(arguments, cancellationToken), cancellationToken);
    }

    private string InvokeCore(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (Options.AllowedRoots.Count == 0) {
            return ToolResponse.Error(
                "access_denied",
                "EVTX file scanning is disabled (AllowedRoots is empty).",
                hints: new[] { "Enable EVTX access by configuring AllowedRoots / --allow-root." },
                isTransient: false);
        }

        var query = (arguments?.GetString("query") ?? string.Empty).Trim();
        var logHint = (arguments?.GetString("log_name") ?? string.Empty).Trim();
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", MaxDefaultResults, 1, MaxMaxResults);

        var queryTokens = SplitTokens(query);
        var logToken = string.IsNullOrWhiteSpace(logHint) ? null : logHint;

        // Collect one extra match so we can report `truncated` accurately without scanning the entire tree.
        var maxMatchesToCollect = maxResults + 1;
        var matches = new List<EvtxFindFile>(Math.Min(64, maxResults));
        var scannedDirs = 0;
        var scannedFiles = 0;
        var hitScanBudget = false;

        foreach (var root in Options.AllowedRoots.Where(static x => !string.IsNullOrWhiteSpace(x))) {
            cancellationToken.ThrowIfCancellationRequested();

            var rootPath = root.Trim();
            if (!Directory.Exists(rootPath)) {
                continue;
            }

            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((rootPath, 0));

            while (queue.Count > 0) {
                cancellationToken.ThrowIfCancellationRequested();

                if (matches.Count >= maxMatchesToCollect) {
                    break;
                }

                if (scannedDirs >= MaxDirsScanned || scannedFiles >= MaxFilesScanned) {
                    hitScanBudget = true;
                    break;
                }

                var (dir, depth) = queue.Dequeue();
                scannedDirs++;

                IEnumerable<string> files;
                try {
                    files = Directory.EnumerateFiles(dir, "*.evtx", SearchOption.TopDirectoryOnly);
                } catch (Exception ex) when (
                    ex is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException or IOException) {
                    continue;
                }

                foreach (var file in files) {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (matches.Count >= maxMatchesToCollect) {
                        break;
                    }

                    if (scannedFiles >= MaxFilesScanned) {
                        hitScanBudget = true;
                        break;
                    }

                    scannedFiles++;

                    if (!IsMatch(file, queryTokens, logToken)) {
                        continue;
                    }

                    try {
                        var info = new FileInfo(file);
                        matches.Add(new EvtxFindFile(
                            Path: info.FullName,
                            FileName: info.Name,
                            SizeBytes: info.Length,
                            LastWriteTimeUtc: info.LastWriteTimeUtc));
                    } catch (Exception ex) when (
                        ex is UnauthorizedAccessException or FileNotFoundException or PathTooLongException or IOException) {
                        // Ignore file races/access issues.
                    }
                }

                if (matches.Count >= maxMatchesToCollect || hitScanBudget) {
                    break;
                }

                if (depth >= MaxDepth) {
                    continue;
                }

                IEnumerable<string> subDirs;
                try {
                    subDirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
                } catch (Exception ex) when (
                    ex is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException or IOException) {
                    continue;
                }

                foreach (var subDir in subDirs) {
                    // Avoid queue blowups on wide trees: cap *pending* dirs based on remaining scan budget.
                    if (scannedDirs + queue.Count >= MaxDirsScanned) {
                        hitScanBudget = true;
                        break;
                    }
                    queue.Enqueue((subDir, depth + 1));
                }
            }

            if (matches.Count >= maxMatchesToCollect || hitScanBudget) {
                break;
            }
        }

        var ordered = matches
            .OrderByDescending(static x => x.LastWriteTimeUtc)
            .ThenBy(static x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // We may stop early either because we hit max_results or because we hit scan budgets.
        var truncated = hitScanBudget || ordered.Length > maxResults;
        var selected = truncated ? ordered.Take(maxResults).ToArray() : ordered;

        var result = new EvtxFindResult(
            Files: selected,
            ScannedDirectories: scannedDirs,
            ScannedFiles: scannedFiles,
            Truncated: truncated);

        var preview = ToolPreview.Table(maxRows: 20, maxCellChars: 120);
        foreach (var row in selected) {
            preview.TryAdd(
                row.FileName,
                row.LastWriteTimeUtc.ToString("u"),
                row.SizeBytes.ToString(),
                row.Path);
        }

        return ToolResponse.OkTablePreviewModel(
            model: result,
            title: "EVTX files (preview)",
            rowsPath: "files",
            headers: new[] { "File", "LastWriteUtc", "Size", "Path" },
            previewRows: preview.Rows,
            count: selected.Length,
            truncated: truncated,
            scanned: scannedFiles,
            metaMutate: meta => {
                meta["scanned_directories"] = JsonValue.From(scannedDirs);
            },
            columns: new[] {
                new ToolColumn("file_name", "File", "string"),
                new ToolColumn("last_write_time_utc", "LastWriteUtc", "datetime"),
                new ToolColumn("size_bytes", "Size", "number"),
                new ToolColumn("path", "Path", "string")
            });
    }

    private static bool IsMatch(string path, IReadOnlyList<string> queryTokens, string? logHint) {
        var haystack = path ?? string.Empty;
        if (queryTokens.Count > 0) {
            for (var i = 0; i < queryTokens.Count; i++) {
                if (haystack.IndexOf(queryTokens[i], StringComparison.OrdinalIgnoreCase) < 0) {
                    return false;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(logHint)) {
            if (haystack.IndexOf(logHint!, StringComparison.OrdinalIgnoreCase) < 0) {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> SplitTokens(string query) {
        if (string.IsNullOrWhiteSpace(query)) {
            return Array.Empty<string>();
        }

        var tokens = query
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return tokens.Length == 0 ? Array.Empty<string>() : tokens;
    }
}

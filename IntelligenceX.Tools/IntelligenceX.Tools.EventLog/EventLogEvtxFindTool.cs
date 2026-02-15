using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
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
    private const int MaxDefaultResults = 20;
    private const int MaxMaxResults = 80;

    // EVTX discovery can touch a lot of the filesystem; cap concurrent scans to avoid threadpool/disk contention.
    private static readonly SemaphoreSlim ScanConcurrency = new(initialCount: 2, maxCount: 2);

    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_evtx_find",
        "Find local .evtx files under allowed roots (read-only, bounded scan). Use this when you have exported EVTX logs but don't know the exact path.",
        ToolSchema.Object(
                ("query", ToolSchema.String("Optional case-insensitive filename/path filter. If it contains whitespace, all tokens must match.")),
                ("log_name", ToolSchema.String("Optional log hint (for example: System, Security, Application).")),
                ("max_results", ToolSchema.Integer("Optional maximum files to return (capped).")))
            .NoAdditionalProperties());

    private sealed record EvtxFindFile(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("file_name")] string FileName,
        [property: JsonPropertyName("size_bytes")] long SizeBytes,
        [property: JsonPropertyName("last_write_time_utc")] DateTime LastWriteTimeUtc);

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
    protected override async Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        await ScanConcurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            cancellationToken.ThrowIfCancellationRequested();
            // Filesystem enumeration is synchronous; keep it bounded (budgets) and concurrency-limited (semaphore)
            // rather than consuming extra threadpool threads via Task.Run.
            return InvokeCore(arguments, cancellationToken);
        } finally {
            ScanConcurrency.Release();
        }
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

        try {
            Options.Validate();
        } catch (ArgumentOutOfRangeException ex) {
            return ToolResponse.Error(
                "invalid_configuration",
                $"Event log tool options are invalid: {ex.Message}",
                hints: new[] { "Fix configuration values for EventLogToolOptions and restart." },
                isTransient: false);
        }

        var query = (arguments?.GetString("query") ?? string.Empty).Trim();
        var logHint = (arguments?.GetString("log_name") ?? string.Empty).Trim();
        var maxResults = ToolArgs.GetCappedInt32(arguments, "max_results", MaxDefaultResults, 1, MaxMaxResults);
        var maxDepth = Options.EvtxFindMaxDepth;
        var maxDirsScanned = Options.EvtxFindMaxDirsScanned;
        var maxFilesScanned = Options.EvtxFindMaxFilesScanned;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var tokenComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        var queryTokens = SplitTokens(query, tokenComparer);
        var logTokens = SplitTokens(logHint, tokenComparer);

        var best = new List<EvtxFindFile>(Math.Min(64, maxResults));
        // Counts all files that match filters, even if we don't keep them in `best` due to max_results.
        var totalMatches = 0;
        var scannedDirs = 0;
        var scannedFiles = 0;
        var hitScanBudget = false;

        foreach (var root in Options.AllowedRoots.Where(static x => !string.IsNullOrWhiteSpace(x))) {
            cancellationToken.ThrowIfCancellationRequested();

            var rootPath = root.Trim();
            string rootFull;
            try {
                rootFull = Path.GetFullPath(rootPath);
                rootFull = Path.TrimEndingDirectorySeparator(rootFull);
            } catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException) {
                continue;
            }

            if (!Directory.Exists(rootFull)) {
                continue;
            }

            // Do not traverse reparse points (symlinks/junctions) to prevent escaping AllowedRoots.
            if (HasReparsePoint(rootFull)) {
                continue;
            }

            // Normalize once for containment comparisons (works across \\?\, \\?\UNC\, and separator representations).
            var rootCmp = NormalizePathForComparison(rootFull);

            var queue = new Queue<(string Dir, int Depth)>();
            queue.Enqueue((rootFull, 0));

            while (queue.Count > 0) {
                cancellationToken.ThrowIfCancellationRequested();

                if (scannedDirs >= maxDirsScanned || scannedFiles >= maxFilesScanned) {
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

                    if (scannedFiles >= maxFilesScanned) {
                        hitScanBudget = true;
                        break;
                    }

                    scannedFiles++;

                    if (!IsMatch(file, queryTokens, logTokens, comparison)) {
                        continue;
                    }

                    try {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) {
                            continue;
                        }

                        var fileFull = NormalizePathForComparison(Path.GetFullPath(info.FullName));
                        if (!IsUnderRoot(fileFull, rootCmp, comparison)) {
                            continue;
                        }

                        var candidate = new EvtxFindFile(
                            Path: fileFull,
                            FileName: info.Name,
                            SizeBytes: info.Length,
                            LastWriteTimeUtc: info.LastWriteTimeUtc);
                        totalMatches++;
                        ConsiderCandidate(best, candidate, maxResults);
                    } catch (Exception ex) when (
                        ex is UnauthorizedAccessException or FileNotFoundException or PathTooLongException or IOException) {
                        // Ignore file races/access issues.
                    }
                }

                if (hitScanBudget) {
                    break;
                }

                if (depth >= maxDepth) {
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
                    if (scannedDirs + queue.Count >= maxDirsScanned) {
                        hitScanBudget = true;
                        break;
                    }

                    string subFull;
                    try {
                        subFull = Path.GetFullPath(subDir);
                        subFull = Path.TrimEndingDirectorySeparator(subFull);
                    } catch (Exception ex) when (
                        ex is ArgumentException or NotSupportedException or PathTooLongException) {
                        continue;
                    }

                    if (!IsUnderRoot(subFull, rootCmp, comparison)) {
                        continue;
                    }

                    // Do not traverse reparse points (symlinks/junctions) to prevent escaping AllowedRoots.
                    if (HasReparsePoint(subFull)) {
                        continue;
                    }

                    queue.Enqueue((subFull, depth + 1));
                }
            }

            if (hitScanBudget) {
                break;
            }
        }

        var truncated = hitScanBudget || totalMatches > maxResults;
        var selected = best;

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
            count: selected.Count,
            truncated: truncated,
            scanned: scannedFiles,
            metaMutate: meta => {
                meta["scanned_directories"] = JsonValue.From(scannedDirs);
                meta["scan_budget_hit"] = JsonValue.From(hitScanBudget);
                meta["total_matches"] = JsonValue.From(totalMatches);
            },
            columns: new[] {
                new ToolColumn("file_name", "File", "string"),
                new ToolColumn("last_write_time_utc", "LastWriteUtc", "datetime"),
                new ToolColumn("size_bytes", "Size", "number"),
                new ToolColumn("path", "Path", "string")
            });
    }

    private static readonly StringComparison BestStringComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static readonly IComparer<EvtxFindFile> BestComparer = Comparer<EvtxFindFile>.Create(CompareBest);

    // Negative means "a is better than b" (earlier in output ordering).
    private static int CompareBest(EvtxFindFile a, EvtxFindFile b) {
        var cmp = b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);
        if (cmp != 0) {
            return cmp;
        }

        cmp = string.Compare(a.FileName, b.FileName, BestStringComparison);
        if (cmp != 0) {
            return cmp;
        }

        return string.Compare(a.Path, b.Path, BestStringComparison);
    }

    private static void ConsiderCandidate(List<EvtxFindFile> best, EvtxFindFile candidate, int maxResults) {
        if (maxResults <= 0) {
            return;
        }

        if (best.Count < maxResults) {
            InsertSorted(best, candidate);
            return;
        }

        // best is kept in output order (best -> worst).
        var worst = best[^1];
        if (BestComparer.Compare(candidate, worst) >= 0) {
            return;
        }

        InsertSorted(best, candidate);
        if (best.Count > maxResults) {
            best.RemoveAt(best.Count - 1);
        }
    }

    private static void InsertSorted(List<EvtxFindFile> best, EvtxFindFile candidate) {
        var idx = best.BinarySearch(candidate, BestComparer);
        if (idx < 0) {
            idx = ~idx;
        }
        best.Insert(idx, candidate);
    }

    private static bool HasReparsePoint(string path) {
        try {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        } catch (Exception ex) when (
            ex is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException or IOException) {
            // Treat as unsafe: skip traversal if we can't reliably read attributes.
            return true;
        }
    }

    // Internal so we can unit-test root boundary logic without relying on junction/symlink creation.
    internal static bool IsUnderRoot(string candidateFullPath, string rootFullPath, StringComparison comparison) {
        if (string.IsNullOrWhiteSpace(candidateFullPath) || string.IsNullOrWhiteSpace(rootFullPath)) {
            return false;
        }

        candidateFullPath = NormalizePathForComparison(Path.TrimEndingDirectorySeparator(candidateFullPath));
        rootFullPath = NormalizePathForComparison(Path.TrimEndingDirectorySeparator(rootFullPath));

        // Prefer relative-path based boundary checks over raw prefix checks.
        // This avoids "root vs root2" prefix bugs and handles separator differences more robustly.
        try {
            var rel = Path.GetRelativePath(rootFullPath, candidateFullPath);
            if (string.Equals(rel, ".", comparison)) {
                return true;
            }

            // If the relative path escapes the root, it will start with ".." or resolve to an absolute path.
            if (string.Equals(rel, "..", comparison)) {
                return false;
            }

            var parentPrefix1 = ".." + Path.DirectorySeparatorChar;
            var parentPrefix2 = ".." + Path.AltDirectorySeparatorChar;
            if (rel.StartsWith(parentPrefix1, comparison) || rel.StartsWith(parentPrefix2, comparison)) {
                return false;
            }

            if (Path.IsPathRooted(rel)) {
                return false;
            }

            return true;
        } catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException) {
            return false;
        }
    }

    private static string NormalizePathForComparison(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        // Normalize Windows extended-length prefixes so containment checks don't vary by representation.
        if (OperatingSystem.IsWindows()) {
            // Normalize separators so containment checks don't vary by representation ("C:/x" vs "C:\\x").
            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) {
                return @"\\" + path.Substring(@"\\?\UNC\".Length);
            }
            if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) {
                return path.Substring(@"\\?\".Length);
            }
        }

        return path;
    }

    private static bool IsMatch(string path, IReadOnlyList<string> queryTokens, IReadOnlyList<string> logTokens, StringComparison comparison) {
        var haystack = path;
        if (queryTokens.Count > 0) {
            for (var i = 0; i < queryTokens.Count; i++) {
                if (haystack.IndexOf(queryTokens[i], comparison) < 0) {
                    return false;
                }
            }
        }

        if (logTokens.Count > 0) {
            for (var i = 0; i < logTokens.Count; i++) {
                if (haystack.IndexOf(logTokens[i], comparison) < 0) {
                    return false;
                }
            }
        }

        return true;
    }

    private static IReadOnlyList<string> SplitTokens(string query, StringComparer comparer) {
        if (string.IsNullOrWhiteSpace(query)) {
            return Array.Empty<string>();
        }

        var tokens = query
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static x => x.Length > 0)
            .Distinct(comparer)
            .Take(8)
            .ToArray();

        return tokens.Length == 0 ? Array.Empty<string>() : tokens;
    }
}

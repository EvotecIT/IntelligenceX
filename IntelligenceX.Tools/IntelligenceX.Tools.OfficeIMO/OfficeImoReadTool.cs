using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

#if OFFICEIMO_ENABLED
using OfficeIMO.Reader;
#endif

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Reads a supported Office document (or a folder of documents) and emits AI-friendly chunks (safe-by-default; requires AllowedRoots).
/// </summary>
public sealed class OfficeImoReadTool : OfficeImoToolBase, ITool {
    private static readonly string[] DefaultOfficeExtensions = {
        ".docx", ".docm",
        ".xlsx", ".xlsm",
        ".pptx", ".pptm",
        ".md", ".markdown",
        ".pdf"
    };

    private static readonly ToolDefinition DefinitionValue = new(
        "officeimo_read",
        "Read a Word/Excel/PowerPoint/Markdown/PDF file (or a folder containing those) and return normalized chunks for reasoning.",
        ToolSchema.Object(
                ("path", ToolSchema.String("Path to a file or folder (absolute or relative).")),
                ("recurse", ToolSchema.Boolean("If path is a folder, recurse into subfolders (default: false).")),
                ("extensions", ToolSchema.Array(ToolSchema.String(), "Optional allowlist of extensions to ingest (default: Office formats only, e.g. ['.docx','.xlsx','.pptx','.md','.pdf']).")),
                ("max_files", ToolSchema.Integer("Max files to ingest when a folder is provided (capped by pack options).")),
                ("max_total_bytes", ToolSchema.Integer("Max total bytes across all ingested files when a folder is provided (capped by pack options).")),
                ("max_input_bytes", ToolSchema.Integer("Max bytes per single file (capped by pack options).")),
                ("max_chunks", ToolSchema.Integer("Max chunks returned overall (caps output payload size).")),
                ("max_chars", ToolSchema.Integer("Max characters per chunk (caps output size).")),
                ("max_table_rows", ToolSchema.Integer("Max rows per table (Excel).")),
                ("excel_sheet_name", ToolSchema.String("Optional Excel sheet name to read.")),
                ("excel_a1_range", ToolSchema.String("Optional Excel A1 range (e.g. A1:D200).")),
                ("excel_headers_in_first_row", ToolSchema.Boolean("Whether to treat the first row as headers (default: true).")),
                ("include_word_footnotes", ToolSchema.Boolean("Include Word footnotes (default: true).")),
                ("include_ppt_notes", ToolSchema.Boolean("Include PowerPoint speaker notes (default: true).")),
                ("markdown_chunk_by_headings", ToolSchema.Boolean("Chunk Markdown by headings when possible (default: true).")))
            .Required("path")
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="OfficeImoReadTool"/> class.
    /// </summary>
    public OfficeImoReadTool(OfficeImoToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var inputPath = arguments?.GetString("path") ?? string.Empty;

        // Resolve path + enforce AllowedRoots (without assuming file vs directory).
        if (!PathResolver.TryResolvePath(inputPath, Options.AllowedRoots, out var fullPath, out var resolveError)) {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "access_denied",
                error: resolveError,
                hints: new[] { "Adjust AllowedRoots to include the requested path.", "Use an absolute path inside an allowed root." },
                isTransient: false));
        }

        var recurse = ToolArgs.GetBoolean(arguments, "recurse", defaultValue: false);
        var maxFiles = ToolArgs.GetCappedInt32(arguments, "max_files", Options.MaxFiles, 1, Options.MaxFiles);
        var maxTotalBytes = ToolArgs.GetCappedInt64(arguments, "max_total_bytes", Options.MaxTotalBytes, 1, Options.MaxTotalBytes);
        var maxInputBytes = ToolArgs.GetCappedInt64(arguments, "max_input_bytes", Options.MaxInputBytes, 1, Options.MaxInputBytes);
        var maxChunks = ToolArgs.GetCappedInt32(arguments, "max_chunks", defaultValue: 10_000, minInclusive: 1, maxInclusive: 100_000);

        // Output shaping caps (avoid accidental multi-megabyte chunks in tool payloads).
        var maxChars = ToolArgs.GetCappedInt32(arguments, "max_chars", defaultValue: 8000, minInclusive: 256, maxInclusive: 250_000);
        var maxTableRows = ToolArgs.GetCappedInt32(arguments, "max_table_rows", defaultValue: 200, minInclusive: 1, maxInclusive: 10_000);

        var excelSheetName = ToolArgs.GetOptionalTrimmed(arguments, "excel_sheet_name");
        var excelA1Range = ToolArgs.GetOptionalTrimmed(arguments, "excel_a1_range");
        var excelHeadersInFirstRow = ToolArgs.GetBoolean(arguments, "excel_headers_in_first_row", defaultValue: true);
        var includeWordFootnotes = ToolArgs.GetBoolean(arguments, "include_word_footnotes", defaultValue: true);
        var includePptNotes = ToolArgs.GetBoolean(arguments, "include_ppt_notes", defaultValue: true);
        var markdownChunkByHeadings = ToolArgs.GetBoolean(arguments, "markdown_chunk_by_headings", defaultValue: true);

        var extensions = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("extensions"));
        var normalizedExt = NormalizeExtensions(extensions);

        var result = new OfficeImoReadResult();

        if (Directory.Exists(fullPath)) {
            var folder = fullPath;
            var files = EnumerateFolderFilesSafe(
                folderPath: folder,
                recurse: recurse,
                allowedExt: normalizedExt,
                maxFiles: maxFiles,
                maxTotalBytes: maxTotalBytes,
                cancellationToken: cancellationToken,
                warnings: result.Warnings,
                truncated: out var folderTruncated);

            result.Truncated |= folderTruncated;
            foreach (var f in files) {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Files.Count >= maxFiles) {
                    result.Truncated = true;
                    break;
                }

                // Enforce per-file cap early.
                try {
                    var len = new FileInfo(f).Length;
                    if (len > maxInputBytes) {
                        result.Warnings.Add($"Skipped (too large): {f}");
                        continue;
                    }
                } catch {
                    result.Warnings.Add($"Skipped (cannot stat): {f}");
                    continue;
                }

                result.Files.Add(f);
                var chunks = ReadChunks(f, maxInputBytes, maxChars, maxTableRows, excelSheetName, excelA1Range, excelHeadersInFirstRow, includeWordFootnotes, includePptNotes, markdownChunkByHeadings, cancellationToken, out var readWarning);
                if (!string.IsNullOrWhiteSpace(readWarning)) {
                    result.Warnings.Add(readWarning!);
                }
                if (AddChunksWithCap(result.Chunks, chunks, maxChunks)) {
                    result.Truncated = true;
                    result.Warnings.Add($"Stopped after reaching max_chunks={maxChunks}.");
                    break;
                }
            }
        } else if (File.Exists(fullPath)) {
            // Single file.
            result.Files.Add(fullPath);
            var chunks = ReadChunks(fullPath, maxInputBytes, maxChars, maxTableRows, excelSheetName, excelA1Range, excelHeadersInFirstRow, includeWordFootnotes, includePptNotes, markdownChunkByHeadings, cancellationToken, out var readWarning);
            if (!string.IsNullOrWhiteSpace(readWarning)) {
                result.Warnings.Add(readWarning!);
            }
            if (AddChunksWithCap(result.Chunks, chunks, maxChunks)) {
                result.Truncated = true;
                result.Warnings.Add($"Stopped after reaching max_chunks={maxChunks}.");
            }
        } else {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "not_found",
                error: "File or directory not found.",
                hints: new[] { "Verify the path exists and is inside AllowedRoots." },
                isTransient: false));
        }

        var preview = BuildPreviewMarkdown(result.Chunks, maxChunks: 6, maxCharsPerChunk: 1800);
        var meta = ToolOutputHints.Meta(count: result.Chunks.Count, truncated: result.Truncated)
            .Add("files", result.Files.Count)
            .Add("max_files", maxFiles)
            .Add("max_total_bytes", maxTotalBytes)
            .Add("max_input_bytes", maxInputBytes)
            .Add("max_chunks", maxChunks)
            .Add("max_chars", maxChars)
            .Add("max_table_rows", maxTableRows);

        var summary = ToolMarkdown.JoinBlocks(
            ToolMarkdown.SummaryFacts(
                title: "OfficeIMO read",
                facts: new (string Key, string Value)[] {
                    ("Files", result.Files.Count.ToString()),
                    ("Chunks", result.Chunks.Count.ToString()),
                    ("Truncated", result.Truncated ? "yes" : "no")
                }),
            preview);

        // Keep render hints minimal; consumer usually reasons from `chunks`.
        return Task.FromResult(ToolResponse.OkModel(model: result, meta: meta, summaryMarkdown: summary));
    }

    private static string BuildPreviewMarkdown(IReadOnlyList<OfficeImoChunk> chunks, int maxChunks, int maxCharsPerChunk) {
        if (chunks is null || chunks.Count == 0) {
            return ToolMarkdown.SummaryText("Preview", "No chunks returned.");
        }

        var take = Math.Clamp(maxChunks, 1, 25);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(ToolMarkdown.Heading(3, "Preview"));
        sb.AppendLine();

        var n = Math.Min(take, chunks.Count);
        for (var i = 0; i < n; i++) {
            var c = chunks[i];
            var title = $"{i + 1}. {c.Kind} {c.Id}";
            sb.AppendLine(ToolMarkdown.Heading(4, title));
            sb.AppendLine();

            var text = c.Markdown ?? c.Text ?? string.Empty;
            if (text.Length > maxCharsPerChunk) {
                text = text.Substring(0, maxCharsPerChunk) + "\n\n<!-- truncated -->";
            }
            sb.AppendLine(ToolMarkdown.CodeBlock(language: "text", content: text));
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static HashSet<string> NormalizeExtensions(List<string> extensions) {
        var set = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        IEnumerable<string> source = (extensions is null || extensions.Count == 0) ? DefaultOfficeExtensions : extensions;
        foreach (var e in source) {
            if (string.IsNullOrWhiteSpace(e)) continue;
            var v = e.Trim();
            if (!v.StartsWith(".", StringComparison.Ordinal)) v = "." + v;
            set.Add(v);
        }
        return set;
    }

    private static IEnumerable<string> EnumerateFolderFilesSafe(
        string folderPath,
        bool recurse,
        HashSet<string> allowedExt,
        int maxFiles,
        long maxTotalBytes,
        CancellationToken cancellationToken,
        List<string> warnings,
        out bool truncated) {

        truncated = false;
        var files = new List<string>(capacity: Math.Min(maxFiles, 512));

        // Stable traversal: per-directory sort; also skip reparse points (symlinks/junctions) to avoid escaping AllowedRoots.
        var dirs = new Stack<string>();
        dirs.Push(folderPath);

        long totalBytes = 0;

        while (dirs.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = dirs.Pop();

            IEnumerable<string> entries;
            try {
                entries = Directory.EnumerateFileSystemEntries(dir);
            } catch {
                warnings.Add($"Skipped (cannot enumerate): {dir}");
                continue;
            }

            var ordered = entries.OrderBy(static x => x, StringComparer.Ordinal).ToArray();
            foreach (var entry in ordered) {
                cancellationToken.ThrowIfCancellationRequested();
                if (files.Count >= maxFiles) {
                    truncated = true;
                    return files;
                }

                try {
                    if (Directory.Exists(entry)) {
                        if (!recurse) {
                            continue;
                        }

                        // Skip reparse points (junctions/symlinks) for safety.
                        try {
                            var attrs = File.GetAttributes(entry);
                            if ((attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) {
                                warnings.Add($"Skipped (reparse point): {entry}");
                                continue;
                            }
                        } catch {
                            warnings.Add($"Skipped (cannot stat dir): {entry}");
                            continue;
                        }

                        dirs.Push(entry);
                        continue;
                    }

                    if (!File.Exists(entry)) {
                        continue;
                    }

                    var ext = Path.GetExtension(entry) ?? string.Empty;
                    if (!allowedExt.Contains(ext)) {
                        continue;
                    }

                    long len;
                    try {
                        len = new FileInfo(entry).Length;
                    } catch {
                        warnings.Add($"Skipped (cannot stat file): {entry}");
                        continue;
                    }

                    if ((totalBytes + len) > maxTotalBytes) {
                        truncated = true;
                        return files;
                    }

                    totalBytes += len;
                    files.Add(entry);
                } catch {
                    warnings.Add($"Skipped (unexpected error): {entry}");
                }
            }
        }

        return files;
    }

    private static bool AddChunksWithCap(List<OfficeImoChunk> destination, List<OfficeImoChunk> source, int maxChunks) {
        if (destination.Count >= maxChunks) {
            return true;
        }

        var remaining = maxChunks - destination.Count;
        if (source.Count <= remaining) {
            destination.AddRange(source);
            return false;
        }

        destination.AddRange(source.GetRange(0, remaining));
        return true;
    }

    private static List<OfficeImoChunk> ReadChunks(
        string path,
        long maxInputBytes,
        int maxChars,
        int maxTableRows,
        string? excelSheetName,
        string? excelA1Range,
        bool excelHeadersInFirstRow,
        bool includeWordFootnotes,
        bool includePptNotes,
        bool markdownChunkByHeadings,
        CancellationToken cancellationToken,
        out string? warning) {

        warning = null;

#if !OFFICEIMO_ENABLED
        warning = "OfficeIMO.Reader is not available in this build (missing reference).";
        return new List<OfficeImoChunk>();
#else
        ReaderOptions opt = new() {
            MaxInputBytes = maxInputBytes,
            MaxChars = maxChars,
            MaxTableRows = maxTableRows,
            ExcelSheetName = excelSheetName,
            ExcelA1Range = excelA1Range,
            ExcelHeadersInFirstRow = excelHeadersInFirstRow,
            IncludeWordFootnotes = includeWordFootnotes,
            IncludePowerPointNotes = includePptNotes,
            MarkdownChunkByHeadings = markdownChunkByHeadings
            // Keep OpenXmlMaxCharactersInPart default from OfficeIMO.Reader for safety.
        };

        IEnumerable<ReaderChunk> chunks;
        try {
            chunks = DocumentReader.Read(path, options: opt, cancellationToken: cancellationToken);
        } catch (NotSupportedException ex) {
            warning = $"Skipped (unsupported): {path} ({ex.Message})";
            return new List<OfficeImoChunk>();
        } catch (IOException ex) {
            warning = $"Skipped (I/O): {path} ({ex.Message})";
            return new List<OfficeImoChunk>();
        } catch (Exception ex) {
            warning = $"Skipped (error): {path} ({ex.Message})";
            return new List<OfficeImoChunk>();
        }

        var list = new List<OfficeImoChunk>();
        foreach (var c in chunks) {
            cancellationToken.ThrowIfCancellationRequested();
            if (c == null) continue;

            list.Add(new OfficeImoChunk {
                Id = c.Id ?? string.Empty,
                Kind = c.Kind.ToString().ToLowerInvariant(),
                Text = c.Text ?? string.Empty,
                Markdown = c.Markdown,
                Location = c.Location,
                Tables = c.Tables,
                Warnings = c.Warnings
            });
        }
        return list;
#endif
    }
}

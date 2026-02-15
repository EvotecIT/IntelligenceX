using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
/// Reads a supported Office document (or a folder of documents) and emits AI-friendly chunks/documents.
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
        "Read a Word/Excel/PowerPoint/Markdown/PDF file (or a folder containing those) and return normalized chunks/documents for reasoning and indexing.",
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
                ("markdown_chunk_by_headings", ToolSchema.Boolean("Chunk Markdown by headings when possible (default: true).")),
                ("output_mode", ToolSchema.String("Output shape: 'chunks' (default), 'documents', or 'both'.")),
                ("include_document_chunks", ToolSchema.Boolean("When output_mode includes documents, include per-document chunk arrays (default: true).")))
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

        var maxChars = ToolArgs.GetCappedInt32(arguments, "max_chars", defaultValue: 8000, minInclusive: 256, maxInclusive: 250_000);
        var maxTableRows = ToolArgs.GetCappedInt32(arguments, "max_table_rows", defaultValue: 200, minInclusive: 1, maxInclusive: 10_000);

        var excelSheetName = ToolArgs.GetOptionalTrimmed(arguments, "excel_sheet_name");
        var excelA1Range = ToolArgs.GetOptionalTrimmed(arguments, "excel_a1_range");
        var excelHeadersInFirstRow = ToolArgs.GetBoolean(arguments, "excel_headers_in_first_row", defaultValue: true);
        var includeWordFootnotes = ToolArgs.GetBoolean(arguments, "include_word_footnotes", defaultValue: true);
        var includePptNotes = ToolArgs.GetBoolean(arguments, "include_ppt_notes", defaultValue: true);
        var markdownChunkByHeadings = ToolArgs.GetBoolean(arguments, "markdown_chunk_by_headings", defaultValue: true);
        var includeDocumentChunks = ToolArgs.GetBoolean(arguments, "include_document_chunks", defaultValue: true);

        if (!TryParseOutputMode(ToolArgs.GetOptionalTrimmed(arguments, "output_mode"), out var outputMode)) {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "invalid_argument",
                error: "Invalid output_mode. Allowed values: chunks, documents, both.",
                hints: new[] { "Use output_mode='chunks' (default), 'documents', or 'both'." },
                isTransient: false));
        }

        var extensions = ToolArgs.ReadDistinctStringArray(arguments?.GetArray("extensions"));
        var normalizedExt = NormalizeExtensions(extensions);

        var result = new OfficeImoReadResult {
            OutputMode = ToOutputModeString(outputMode)
        };

#if !OFFICEIMO_ENABLED
        if (Directory.Exists(fullPath)) {
            var files = EnumerateFolderFilesSafe(
                folderPath: fullPath,
                recurse: recurse,
                allowedExt: normalizedExt,
                maxFiles: maxFiles,
                maxTotalBytes: maxTotalBytes,
                cancellationToken: cancellationToken,
                warnings: result.Warnings,
                truncated: out var folderTruncated);

            result.Truncated |= folderTruncated;
            result.Files.AddRange(files);
            result.FilesScanned = result.Files.Count;
        } else if (File.Exists(fullPath)) {
            result.Files.Add(fullPath);
            result.FilesScanned = 1;
        } else {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "not_found",
                error: "File or directory not found.",
                hints: new[] { "Verify the path exists and is inside AllowedRoots." },
                isTransient: false));
        }

        result.Warnings.Add("OfficeIMO.Reader is not available in this build (missing reference).");
        var disabledSummary = ToolMarkdown.SummaryText(
            title: "OfficeIMO read",
            "Reader dependency is unavailable in this build.",
            $"Files listed: {result.Files.Count}");

        var disabledMeta = ToolOutputHints.Meta(count: 0, truncated: result.Truncated)
            .Add("files", result.Files.Count)
            .Add("output_mode", result.OutputMode)
            .Add("officeimo_enabled", false);

        return Task.FromResult(ToolResponse.OkModel(model: result, meta: disabledMeta, summaryMarkdown: disabledSummary));
#else
        var readerOptions = CreateReaderOptions(
            maxInputBytes: maxInputBytes,
            maxChars: maxChars,
            maxTableRows: maxTableRows,
            excelSheetName: excelSheetName,
            excelA1Range: excelA1Range,
            excelHeadersInFirstRow: excelHeadersInFirstRow,
            includeWordFootnotes: includeWordFootnotes,
            includePptNotes: includePptNotes,
            markdownChunkByHeadings: markdownChunkByHeadings);

        var includeFlatChunks = outputMode is OfficeImoOutputMode.Chunks or OfficeImoOutputMode.Both;
        var includeDocuments = outputMode is OfficeImoOutputMode.Documents or OfficeImoOutputMode.Both;
        var includeDocChunksInPayload = includeDocuments && includeDocumentChunks;
        var enforceChunkBudget = includeFlatChunks || includeDocChunksInPayload;
        var remainingChunkBudget = enforceChunkBudget ? maxChunks : int.MaxValue;

        if (Directory.Exists(fullPath)) {
            var progress = new FolderProgressState();
            var folderOptions = new ReaderFolderOptions {
                Recurse = recurse,
                MaxFiles = maxFiles,
                MaxTotalBytes = maxTotalBytes,
                Extensions = normalizedExt.OrderBy(static x => x, StringComparer.Ordinal).ToArray(),
                SkipReparsePoints = true,
                DeterministicOrder = true
            };

            foreach (var source in DocumentReader.ReadFolderDocuments(
                         folderPath: fullPath,
                         folderOptions: folderOptions,
                         options: readerOptions,
                         onProgress: p => UpdateProgress(progress, p),
                         cancellationToken: cancellationToken)) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(source.Path)) {
                    result.Files.Add(source.Path);
                }
                ProcessSourceDocument(
                    source,
                    includeFlatChunks,
                    includeDocuments,
                    includeDocChunksInPayload,
                    ref remainingChunkBudget,
                    result);

                if (enforceChunkBudget && remainingChunkBudget <= 0) {
                    result.Truncated = true;
                    AddWarning(result.Warnings, $"Stopped after reaching max_chunks={maxChunks}.");
                    break;
                }
            }

            result.FilesScanned = progress.FilesScanned;
            result.FilesParsed = progress.FilesParsed;
            result.FilesSkipped = progress.FilesSkipped;
            result.BytesRead = progress.BytesRead;
            result.ChunksProduced = progress.ChunksProduced;
        } else if (File.Exists(fullPath)) {
            ProcessSingleFile(
                fullPath,
                readerOptions,
                includeFlatChunks,
                includeDocuments,
                includeDocChunksInPayload,
                ref remainingChunkBudget,
                maxChunks,
                result,
                cancellationToken);
        } else {
            return Task.FromResult(ToolResponse.Error(
                errorCode: "not_found",
                error: "File or directory not found.",
                hints: new[] { "Verify the path exists and is inside AllowedRoots." },
                isTransient: false));
        }

        var preview = BuildPreviewMarkdown(result.Chunks, maxChunks: 6, maxCharsPerChunk: 1800);
        var meta = ToolOutputHints.Meta(
                count: includeFlatChunks ? result.Chunks.Count : result.Documents.Count,
                truncated: result.Truncated)
            .Add("files", result.Files.Count)
            .Add("documents", result.Documents.Count)
            .Add("files_scanned", result.FilesScanned)
            .Add("files_parsed", result.FilesParsed)
            .Add("files_skipped", result.FilesSkipped)
            .Add("bytes_read", result.BytesRead)
            .Add("chunks_produced", result.ChunksProduced)
            .Add("chunks_returned", result.ChunksReturned)
            .Add("token_estimate_returned", result.TokenEstimateReturned)
            .Add("output_mode", result.OutputMode)
            .Add("max_files", maxFiles)
            .Add("max_total_bytes", maxTotalBytes)
            .Add("max_input_bytes", maxInputBytes)
            .Add("max_chunks", maxChunks)
            .Add("max_chars", maxChars)
            .Add("max_table_rows", maxTableRows)
            .Add("officeimo_enabled", true);

        var summary = ToolMarkdown.JoinBlocks(
            ToolMarkdown.SummaryFacts(
                title: "OfficeIMO read",
                facts: new (string Key, string Value)[] {
                    ("Output mode", result.OutputMode),
                    ("Files", result.Files.Count.ToString()),
                    ("Documents", result.Documents.Count.ToString()),
                    ("Chunks returned", result.ChunksReturned.ToString()),
                    ("Chunks produced", result.ChunksProduced.ToString()),
                    ("Truncated", result.Truncated ? "yes" : "no")
                }),
            preview);

        return Task.FromResult(ToolResponse.OkModel(model: result, meta: meta, summaryMarkdown: summary));
#endif
    }

    private static string BuildPreviewMarkdown(IReadOnlyList<OfficeImoChunk> chunks, int maxChunks, int maxCharsPerChunk) {
        if (chunks is null || chunks.Count == 0) {
            return ToolMarkdown.SummaryText("Preview", "No chunks returned.");
        }

        var take = Math.Clamp(maxChunks, 1, 25);
        var sb = new StringBuilder();
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

    private static bool TryParseOutputMode(string? raw, out OfficeImoOutputMode outputMode) {
        var normalized = string.IsNullOrWhiteSpace(raw) ? "chunks" : raw.Trim().ToLowerInvariant();
        switch (normalized) {
            case "chunks":
                outputMode = OfficeImoOutputMode.Chunks;
                return true;
            case "documents":
                outputMode = OfficeImoOutputMode.Documents;
                return true;
            case "both":
                outputMode = OfficeImoOutputMode.Both;
                return true;
            default:
                outputMode = OfficeImoOutputMode.Chunks;
                return false;
        }
    }

    private static string ToOutputModeString(OfficeImoOutputMode outputMode) {
        return outputMode switch {
            OfficeImoOutputMode.Documents => "documents",
            OfficeImoOutputMode.Both => "both",
            _ => "chunks"
        };
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

#if OFFICEIMO_ENABLED
    private static ReaderOptions CreateReaderOptions(
        long maxInputBytes,
        int maxChars,
        int maxTableRows,
        string? excelSheetName,
        string? excelA1Range,
        bool excelHeadersInFirstRow,
        bool includeWordFootnotes,
        bool includePptNotes,
        bool markdownChunkByHeadings) {
        return new ReaderOptions {
            MaxInputBytes = maxInputBytes,
            MaxChars = maxChars,
            MaxTableRows = maxTableRows,
            ExcelSheetName = excelSheetName,
            ExcelA1Range = excelA1Range,
            ExcelHeadersInFirstRow = excelHeadersInFirstRow,
            IncludeWordFootnotes = includeWordFootnotes,
            IncludePowerPointNotes = includePptNotes,
            MarkdownChunkByHeadings = markdownChunkByHeadings,
            ComputeHashes = true
            // Keep OpenXmlMaxCharactersInPart default from OfficeIMO.Reader for safety.
        };
    }

    private static void UpdateProgress(FolderProgressState state, ReaderProgress progress) {
        if (state == null || progress == null) return;
        state.FilesScanned = progress.FilesScanned;
        state.FilesParsed = progress.FilesParsed;
        state.FilesSkipped = progress.FilesSkipped;
        state.BytesRead = progress.BytesRead;
        state.ChunksProduced = progress.ChunksProduced;
    }

    private static void ProcessSingleFile(
        string path,
        ReaderOptions readerOptions,
        bool includeFlatChunks,
        bool includeDocuments,
        bool includeDocChunksInPayload,
        ref int remainingChunkBudget,
        int maxChunks,
        OfficeImoReadResult result,
        CancellationToken cancellationToken) {
        result.Files.Add(path);
        result.FilesScanned = 1;

        List<ReaderChunk>? sourceChunks = null;
        string? warning = null;
        try {
            sourceChunks = DocumentReader.Read(path, options: readerOptions, cancellationToken: cancellationToken).ToList();
        } catch (NotSupportedException ex) {
            warning = $"Skipped (unsupported): {path} ({ex.Message})";
        } catch (IOException ex) {
            warning = $"Skipped (I/O): {path} ({ex.Message})";
        } catch (Exception ex) {
            warning = $"Skipped (error): {path} ({ex.Message})";
        }

        if (sourceChunks == null) {
            result.FilesSkipped = 1;
            AddWarning(result.Warnings, warning ?? $"Skipped (error): {path}");
            if (includeDocuments) {
                var failedDoc = new OfficeImoDocument {
                    Path = path,
                    Parsed = false
                };
                if (!string.IsNullOrWhiteSpace(warning)) {
                    failedDoc.Warnings.Add(warning!);
                }
                result.Documents.Add(failedDoc);
            }
            return;
        }

        result.FilesParsed = 1;
        result.ChunksProduced = sourceChunks.Count;

        var sourceDoc = BuildSourceDocumentFromChunks(path, sourceChunks);
        ProcessSourceDocument(
            sourceDoc,
            includeFlatChunks,
            includeDocuments,
            includeDocChunksInPayload,
            ref remainingChunkBudget,
            result);

        if ((includeFlatChunks || includeDocChunksInPayload) && remainingChunkBudget <= 0) {
            result.Truncated = true;
            AddWarning(result.Warnings, $"Stopped after reaching max_chunks={maxChunks}.");
        }
    }

    private static ReaderSourceDocument BuildSourceDocumentFromChunks(string path, IReadOnlyList<ReaderChunk> chunks) {
        if (chunks is null || chunks.Count == 0) {
            return new ReaderSourceDocument {
                Path = path,
                Parsed = true,
                ChunksProduced = 0,
                TokenEstimateTotal = 0,
                Chunks = Array.Empty<ReaderChunk>()
            };
        }

        var first = chunks[0];
        var tokenEstimateTotal = 0;
        var warnings = new List<string>();
        for (var i = 0; i < chunks.Count; i++) {
            var c = chunks[i];
            tokenEstimateTotal += c.TokenEstimate ?? 0;
            if (c.Warnings is null) continue;
            for (var j = 0; j < c.Warnings.Count; j++) {
                var w = c.Warnings[j];
                if (!string.IsNullOrWhiteSpace(w)) warnings.Add(w!);
            }
        }

        return new ReaderSourceDocument {
            Path = first.Location?.Path ?? path,
            SourceId = first.SourceId,
            SourceHash = first.SourceHash,
            SourceLastWriteUtc = first.SourceLastWriteUtc,
            SourceLengthBytes = first.SourceLengthBytes,
            Parsed = true,
            ChunksProduced = chunks.Count,
            TokenEstimateTotal = tokenEstimateTotal,
            Warnings = warnings.Count > 0 ? warnings : null,
            Chunks = chunks
        };
    }

    private static void ProcessSourceDocument(
        ReaderSourceDocument source,
        bool includeFlatChunks,
        bool includeDocuments,
        bool includeDocChunksInPayload,
        ref int remainingChunkBudget,
        OfficeImoReadResult result) {
        if (source == null) return;

        var officeDocument = new OfficeImoDocument {
            Path = source.Path ?? string.Empty,
            SourceId = source.SourceId,
            SourceHash = source.SourceHash,
            SourceLastWriteUtc = source.SourceLastWriteUtc,
            SourceLengthBytes = source.SourceLengthBytes,
            Parsed = source.Parsed,
            ChunksProduced = source.ChunksProduced,
            TokenEstimateTotal = source.TokenEstimateTotal
        };

        if (source.Warnings != null) {
            for (var i = 0; i < source.Warnings.Count; i++) {
                var warning = source.Warnings[i];
                if (string.IsNullOrWhiteSpace(warning)) continue;
                officeDocument.Warnings.Add(warning!);
                AddWarning(result.Warnings, warning!);
            }
        }

        var returnedChunks = 0;
        var returnedTokens = 0;
        if (source.Chunks != null && (includeFlatChunks || includeDocChunksInPayload)) {
            for (var i = 0; i < source.Chunks.Count; i++) {
                if (remainingChunkBudget <= 0) break;

                var mapped = ToOfficeChunk(source.Chunks[i]);
                if (includeFlatChunks) {
                    result.Chunks.Add(mapped);
                }
                if (includeDocChunksInPayload) {
                    officeDocument.Chunks.Add(mapped);
                }

                returnedChunks++;
                returnedTokens += mapped.TokenEstimate ?? 0;
                remainingChunkBudget--;
            }
        }

        officeDocument.ChunksReturned = returnedChunks;
        officeDocument.TokenEstimateReturned = returnedTokens;
        result.ChunksReturned += returnedChunks;
        result.TokenEstimateReturned += returnedTokens;

        if (includeDocuments) {
            result.Documents.Add(officeDocument);
        }

        if (source.Parsed && source.SourceLengthBytes.HasValue) {
            result.BytesRead += source.SourceLengthBytes.Value;
        }
    }

    private static OfficeImoChunk ToOfficeChunk(ReaderChunk chunk) {
        if (chunk == null) return new OfficeImoChunk();

        return new OfficeImoChunk {
            Id = chunk.Id ?? string.Empty,
            Kind = chunk.Kind.ToString().ToLowerInvariant(),
            Text = chunk.Text ?? string.Empty,
            Markdown = chunk.Markdown,
            Location = chunk.Location,
            Tables = chunk.Tables,
            Warnings = chunk.Warnings,
            SourceId = chunk.SourceId,
            SourceHash = chunk.SourceHash,
            ChunkHash = chunk.ChunkHash,
            SourceLastWriteUtc = chunk.SourceLastWriteUtc,
            SourceLengthBytes = chunk.SourceLengthBytes,
            TokenEstimate = chunk.TokenEstimate
        };
    }

    private sealed class FolderProgressState {
        public int FilesScanned { get; set; }
        public int FilesParsed { get; set; }
        public int FilesSkipped { get; set; }
        public long BytesRead { get; set; }
        public int ChunksProduced { get; set; }
    }
#endif

    private static void AddWarning(List<string> warnings, string warning) {
        if (warnings is null || string.IsNullOrWhiteSpace(warning)) return;
        if (warnings.Any(x => string.Equals(x, warning, StringComparison.OrdinalIgnoreCase))) return;
        warnings.Add(warning);
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

    private enum OfficeImoOutputMode {
        Chunks = 0,
        Documents = 1,
        Both = 2
    }
}

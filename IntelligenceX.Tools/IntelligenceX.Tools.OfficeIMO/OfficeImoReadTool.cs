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
public sealed partial class OfficeImoReadTool : OfficeImoToolBase, ITool {
    private sealed record OfficeImoReadRequest(
        string FullPath,
        bool Recurse,
        HashSet<string> NormalizedExtensions,
        int MaxFiles,
        long MaxTotalBytes,
        long MaxInputBytes,
        int MaxChunks,
        int MaxChars,
        int MaxTableRows,
        string? ExcelSheetName,
        string? ExcelA1Range,
        bool ExcelHeadersInFirstRow,
        bool IncludeWordFootnotes,
        bool IncludePptNotes,
        bool MarkdownChunkByHeadings,
        bool IncludeDocumentChunks,
        OfficeImoOutputMode OutputMode);

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private ToolRequestBindingResult<OfficeImoReadRequest> BindRequest(JsonObject? arguments) {
        return ToolRequestBinder.Bind(arguments, reader => {
            if (!reader.TryReadRequiredString("path", out var inputPath, out var pathError)) {
                return ToolRequestBindingResult<OfficeImoReadRequest>.Failure(pathError);
            }

            if (!PathResolver.TryResolvePath(inputPath, Options.AllowedRoots, out var fullPath, out var resolveError)) {
                return ToolRequestBindingResult<OfficeImoReadRequest>.Failure(
                    error: resolveError,
                    errorCode: "access_denied",
                    hints: new[] { "Adjust AllowedRoots to include the requested path.", "Use an absolute path inside an allowed root." },
                    isTransient: false);
            }

            var recurse = reader.Boolean("recurse", defaultValue: false);
            var maxFiles = reader.CappedInt32("max_files", Options.MaxFiles, 1, Options.MaxFiles);
            var maxTotalBytes = reader.CappedInt64("max_total_bytes", Options.MaxTotalBytes, 1, Options.MaxTotalBytes);
            var maxInputBytes = reader.CappedInt64("max_input_bytes", Options.MaxInputBytes, 1, Options.MaxInputBytes);
            var maxChunks = reader.CappedInt32("max_chunks", defaultValue: 10_000, minInclusive: 1, maxInclusive: 100_000);
            var maxChars = reader.CappedInt32("max_chars", defaultValue: 8000, minInclusive: 256, maxInclusive: 250_000);
            var maxTableRows = reader.CappedInt32("max_table_rows", defaultValue: 200, minInclusive: 1, maxInclusive: 10_000);
            var excelSheetName = reader.OptionalString("excel_sheet_name");
            var excelA1Range = reader.OptionalString("excel_a1_range");
            var excelHeadersInFirstRow = reader.Boolean("excel_headers_in_first_row", defaultValue: true);
            var includeWordFootnotes = reader.Boolean("include_word_footnotes", defaultValue: true);
            var includePptNotes = reader.Boolean("include_ppt_notes", defaultValue: true);
            var markdownChunkByHeadings = reader.Boolean("markdown_chunk_by_headings", defaultValue: true);
            var includeDocumentChunks = reader.Boolean("include_document_chunks", defaultValue: true);

            if (!TryParseOutputMode(reader.OptionalString("output_mode"), out var outputMode)) {
                return ToolRequestBindingResult<OfficeImoReadRequest>.Failure(
                    error: "Invalid output_mode. Allowed values: chunks, documents, both.",
                    errorCode: "invalid_argument",
                    hints: new[] { "Use output_mode='chunks' (default), 'documents', or 'both'." });
            }

            var normalizedExt = NormalizeExtensions(reader.DistinctStringArray("extensions"));

            return ToolRequestBindingResult<OfficeImoReadRequest>.Success(new OfficeImoReadRequest(
                FullPath: fullPath,
                Recurse: recurse,
                NormalizedExtensions: normalizedExt,
                MaxFiles: maxFiles,
                MaxTotalBytes: maxTotalBytes,
                MaxInputBytes: maxInputBytes,
                MaxChunks: maxChunks,
                MaxChars: maxChars,
                MaxTableRows: maxTableRows,
                ExcelSheetName: excelSheetName,
                ExcelA1Range: excelA1Range,
                ExcelHeadersInFirstRow: excelHeadersInFirstRow,
                IncludeWordFootnotes: includeWordFootnotes,
                IncludePptNotes: includePptNotes,
                MarkdownChunkByHeadings: markdownChunkByHeadings,
                IncludeDocumentChunks: includeDocumentChunks,
                OutputMode: outputMode));
        });
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<OfficeImoReadRequest> context, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath = context.Request.FullPath;
        var recurse = context.Request.Recurse;
        var normalizedExt = context.Request.NormalizedExtensions;
        var maxFiles = context.Request.MaxFiles;
        var maxTotalBytes = context.Request.MaxTotalBytes;
        var maxInputBytes = context.Request.MaxInputBytes;
        var maxChunks = context.Request.MaxChunks;
        var maxChars = context.Request.MaxChars;
        var maxTableRows = context.Request.MaxTableRows;
        var excelSheetName = context.Request.ExcelSheetName;
        var excelA1Range = context.Request.ExcelA1Range;
        var excelHeadersInFirstRow = context.Request.ExcelHeadersInFirstRow;
        var includeWordFootnotes = context.Request.IncludeWordFootnotes;
        var includePptNotes = context.Request.IncludePptNotes;
        var markdownChunkByHeadings = context.Request.MarkdownChunkByHeadings;
        var includeDocumentChunks = context.Request.IncludeDocumentChunks;
        var outputMode = context.Request.OutputMode;

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
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "not_found",
                error: "File or directory not found.",
                hints: new[] { "Verify the path exists and is inside AllowedRoots." },
                isTransient: false));
        }

        result.Warnings.Add("OfficeIMO.Reader is not available in this build (missing reference).");
        result.FilesParsed = 0;
        result.FilesSkipped = result.FilesScanned;
        result.BytesRead = 0;
        result.ChunksProduced = 0;
        result.ChunksReturned = 0;
        result.TokenEstimateReturned = 0;
        result.Chunks.Clear();
        result.Documents.Clear();
        ApplyChainContract(result, fullPath, outputMode, includeDocumentChunks);
        var disabledSummary = ToolMarkdown.SummaryText(
            title: "OfficeIMO read",
            "Reader dependency is unavailable in this build.",
            $"Files listed: {result.Files.Count}");

        var disabledMeta = ToolOutputHints.Meta(count: 0, truncated: result.Truncated)
            .Add("files", result.Files.Count)
            .Add("output_mode", result.OutputMode)
            .Add("officeimo_enabled", false);

        return Task.FromResult(ToolResultV2.OkModel(model: result, meta: disabledMeta, summaryMarkdown: disabledSummary));
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
        if (!Directory.Exists(fullPath) && !File.Exists(fullPath)) {
            return Task.FromResult(ToolResultV2.Error(
                errorCode: "not_found",
                error: "File or directory not found.",
                hints: new[] { "Verify the path exists and is inside AllowedRoots." },
                isTransient: false));
        }

        var folderOptions = CreateFolderOptions(
            recurse: recurse,
            maxFiles: maxFiles,
            maxTotalBytes: maxTotalBytes,
            normalizedExtensions: normalizedExt);

        var projected = DocumentReader.ReadPathDocumentsDetailed(
            path: fullPath,
            folderOptions: folderOptions,
            options: readerOptions,
            includeDocumentChunks: includeFlatChunks || includeDocChunksInPayload,
            maxReturnedChunks: (includeFlatChunks || includeDocChunksInPayload) ? maxChunks : null,
            cancellationToken: cancellationToken);

        result.Files.AddRange(projected.Files);
        result.FilesScanned = projected.FilesScanned;
        result.FilesParsed = projected.FilesParsed;
        result.FilesSkipped = projected.FilesSkipped;
        result.BytesRead = projected.BytesRead;
        result.ChunksProduced = projected.ChunksProduced;
        result.Truncated = projected.Truncated;

        if (projected.Warnings != null) {
            for (var i = 0; i < projected.Warnings.Count; i++) {
                AddWarning(result.Warnings, projected.Warnings[i]);
            }
        }

        ProjectDocuments(
            sources: projected.Documents,
            includeFlatChunks: includeFlatChunks,
            includeDocuments: includeDocuments,
            includeDocumentChunks: includeDocChunksInPayload,
            result: result);

        ApplyChainContract(result, fullPath, outputMode, includeDocumentChunks);
        var preview = BuildPreviewMarkdown(
            chunks: result.Chunks,
            documents: result.Documents,
            includeDocumentChunks: includeDocChunksInPayload,
            maxChunks: 6,
            maxCharsPerChunk: 1800);
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

        return Task.FromResult(ToolResultV2.OkModel(model: result, meta: meta, summaryMarkdown: summary));
#endif
    }

    private static void ApplyChainContract(
        OfficeImoReadResult result,
        string fullPath,
        OfficeImoOutputMode outputMode,
        bool includeDocumentChunks) {
        if (result is null) {
            throw new ArgumentNullException(nameof(result));
        }

        var handoff = ToolChainingHints.Map(
            ("contract", "officeimo_read_handoff"),
            ("version", 1),
            ("path", fullPath ?? string.Empty),
            ("output_mode", result.OutputMode),
            ("files_preview", string.Join(";", result.Files.Take(10))),
            ("documents_count", result.Documents.Count),
            ("chunks_count", result.ChunksReturned),
            ("warnings_count", result.Warnings.Count),
            ("truncated", result.Truncated));

        var nextActions = new List<ToolNextActionModel>();
        if (outputMode is not OfficeImoOutputMode.Both) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "officeimo_read",
                reason: "Switch output_mode='both' to keep flat chunks and source-level documents in one pass.",
                suggestedArguments: ToolChainingHints.Map(
                    ("path", fullPath ?? string.Empty),
                    ("output_mode", "both"),
                    ("include_document_chunks", includeDocumentChunks))));
        }

        if (result.Documents.Count > 0 && outputMode is not OfficeImoOutputMode.Documents) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "officeimo_read",
                reason: "Use documents mode when you need source-level indexing payloads with minimal token load.",
                suggestedArguments: ToolChainingHints.Map(
                    ("path", fullPath ?? string.Empty),
                    ("output_mode", "documents"),
                    ("include_document_chunks", false))));
        }

        if (result.Truncated) {
            nextActions.Add(ToolChainingHints.NextAction(
                tool: "officeimo_read",
                reason: "Result was truncated; rerun with narrower scope (extensions/path) or higher max limits.",
                suggestedArguments: ToolChainingHints.Map(
                    ("path", fullPath ?? string.Empty),
                    ("output_mode", result.OutputMode))));
        }

        var confidence = 0.9d;
        if (result.Truncated) {
            confidence -= 0.25d;
        }
        if (result.FilesParsed == 0 && result.FilesScanned > 0) {
            confidence -= 0.30d;
        }
        if (result.Warnings.Count > 0) {
            confidence -= Math.Min(0.20d, result.Warnings.Count * 0.03d);
        }

        var chain = ToolChainingHints.Create(
            nextActions: nextActions,
            cursor: ToolChainingHints.BuildToken(
                "officeimo_read",
                ("files", result.Files.Count.ToString()),
                ("documents", result.Documents.Count.ToString()),
                ("chunks", result.ChunksReturned.ToString())),
            resumeToken: ToolChainingHints.BuildToken(
                "officeimo_read.resume",
                ("path", fullPath ?? string.Empty),
                ("mode", result.OutputMode),
                ("truncated", result.Truncated ? "1" : "0")),
            handoff: handoff,
            confidence: confidence,
            flowId: ToolChainingHints.BuildToken(
                "officeimo_read.flow",
                ("path", fullPath ?? string.Empty),
                ("mode", result.OutputMode)),
            stepId: "read_result",
            checkpoint: ToolChainingHints.Map(
                ("files", result.Files.Count),
                ("documents", result.Documents.Count),
                ("chunks", result.ChunksReturned),
                ("warnings", result.Warnings.Count),
                ("truncated", result.Truncated)));

        result.NextActions = chain.NextActions;
        result.Cursor = chain.Cursor;
        result.ResumeToken = chain.ResumeToken;
        result.FlowId = chain.FlowId;
        result.StepId = chain.StepId;
        result.Checkpoint = chain.Checkpoint;
        result.Handoff = chain.Handoff;
        result.Confidence = chain.Confidence;
    }

}

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

public sealed partial class OfficeImoReadTool : OfficeImoToolBase, ITool {
    private static string BuildPreviewMarkdown(
        IReadOnlyList<OfficeImoChunk> chunks,
        IReadOnlyList<OfficeImoDocument> documents,
        bool includeDocumentChunks,
        int maxChunks,
        int maxCharsPerChunk) {
        if (chunks is null || chunks.Count == 0) {
            if (includeDocumentChunks && documents != null) {
                var projected = new List<OfficeImoChunk>();
                for (var i = 0; i < documents.Count; i++) {
                    var doc = documents[i];
                    if (doc?.Chunks is null || doc.Chunks.Count == 0) continue;
                    projected.AddRange(doc.Chunks);
                    if (projected.Count >= maxChunks) break;
                }
                if (projected.Count > 0) {
                    chunks = projected;
                }
            }

            if (chunks is null || chunks.Count == 0) {
                if (documents != null && documents.Count > 0) {
                    return ToolMarkdown.SummaryText(
                        "Preview",
                        $"Returned {documents.Count} document record(s).",
                        includeDocumentChunks ? "No chunk text available in preview." : "Per-document chunks were excluded (include_document_chunks=false).");
                }
                return ToolMarkdown.SummaryText("Preview", "No chunks returned.");
            }
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

    private static HashSet<string> NormalizeExtensions(IReadOnlyList<string> extensions) {
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

    private static ReaderFolderOptions CreateFolderOptions(
        bool recurse,
        int maxFiles,
        long maxTotalBytes,
        HashSet<string> normalizedExtensions) {
        return new ReaderFolderOptions {
            Recurse = recurse,
            MaxFiles = maxFiles,
            MaxTotalBytes = maxTotalBytes,
            Extensions = normalizedExtensions.OrderBy(static x => x, StringComparer.Ordinal).ToArray(),
            SkipReparsePoints = true,
            DeterministicOrder = true
        };
    }

    private static void ProjectDocuments(
        ReaderSourceDocument source,
        bool includeFlatChunks,
        bool includeDocuments,
        bool includeDocumentChunks,
        OfficeImoReadResult result) {
        ProjectDocuments(
            sources: new[] { source },
            includeFlatChunks: includeFlatChunks,
            includeDocuments: includeDocuments,
            includeDocumentChunks: includeDocumentChunks,
            result: result);
    }

    private static void ProjectDocuments(
        IReadOnlyList<ReaderSourceDocument> sources,
        bool includeFlatChunks,
        bool includeDocuments,
        bool includeDocumentChunks,
        OfficeImoReadResult result) {
        if (sources is null || result is null) {
            return;
        }

        var flatTokenTotal = 0;
        var documentTokenTotal = 0;
        for (var i = 0; i < sources.Count; i++) {
            var source = sources[i];
            if (source == null) {
                continue;
            }

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
                for (var warningIndex = 0; warningIndex < source.Warnings.Count; warningIndex++) {
                    var warning = source.Warnings[warningIndex];
                    if (string.IsNullOrWhiteSpace(warning)) {
                        continue;
                    }

                    officeDocument.Warnings.Add(warning!);
                }
            }

            var returnedTokens = 0;
            if (source.Chunks != null && source.Chunks.Count > 0) {
                for (var chunkIndex = 0; chunkIndex < source.Chunks.Count; chunkIndex++) {
                    var mapped = ToOfficeChunk(source.Chunks[chunkIndex]);
                    if (includeFlatChunks) {
                        result.Chunks.Add(mapped);
                        flatTokenTotal += mapped.TokenEstimate ?? 0;
                    }

                    if (includeDocumentChunks) {
                        officeDocument.Chunks.Add(mapped);
                        returnedTokens += mapped.TokenEstimate ?? 0;
                    }
                }
            }

            officeDocument.ChunksReturned = officeDocument.Chunks.Count;
            officeDocument.TokenEstimateReturned = returnedTokens;

            if (includeDocumentChunks) {
                documentTokenTotal += returnedTokens;
            }

            if (includeDocuments) {
                result.Documents.Add(officeDocument);
            }
        }

        var documentChunkCount = 0;
        if (includeDocuments && includeDocumentChunks) {
            for (var i = 0; i < result.Documents.Count; i++) {
                documentChunkCount += result.Documents[i].ChunksReturned;
            }
        }

        result.ChunksReturned = result.Chunks.Count + documentChunkCount;
        result.TokenEstimateReturned = flatTokenTotal + documentTokenTotal;
    }

    private static OfficeImoChunk ToOfficeChunk(ReaderChunk chunk) {
        if (chunk == null) return new OfficeImoChunk();

        return new OfficeImoChunk {
            Id = chunk.Id ?? string.Empty,
            Kind = chunk.Kind.ToString().ToLowerInvariant(),
            Text = chunk.Text ?? string.Empty,
            Markdown = chunk.Markdown,
            Location = MapLocation(chunk.Location),
            Tables = MapTables(chunk.Tables),
            Warnings = chunk.Warnings,
            SourceId = chunk.SourceId,
            SourceHash = chunk.SourceHash,
            ChunkHash = chunk.ChunkHash,
            SourceLastWriteUtc = chunk.SourceLastWriteUtc,
            SourceLengthBytes = chunk.SourceLengthBytes,
            TokenEstimate = chunk.TokenEstimate
        };
    }

    private static OfficeImoChunkLocation? MapLocation(ReaderLocation? location) {
        if (location is null) {
            return null;
        }

        return new OfficeImoChunkLocation {
            Path = location.Path,
            BlockIndex = location.BlockIndex,
            SourceBlockIndex = location.SourceBlockIndex,
            StartLine = location.StartLine,
            HeadingPath = location.HeadingPath,
            Sheet = location.Sheet,
            A1Range = location.A1Range,
            Slide = location.Slide,
            Page = location.Page
        };
    }

    private static IReadOnlyList<OfficeImoChunkTable>? MapTables(IReadOnlyList<ReaderTable>? tables) {
        if (tables is null || tables.Count == 0) {
            return null;
        }

        var mapped = new List<OfficeImoChunkTable>(tables.Count);
        for (var i = 0; i < tables.Count; i++) {
            var table = tables[i];
            if (table is null) {
                continue;
            }

            var columns = table.Columns is null
                ? Array.Empty<string>()
                : table.Columns.Select(static value => value ?? string.Empty).ToArray();

            var rows = new List<IReadOnlyList<string>>();
            if (table.Rows is not null) {
                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++) {
                    var row = table.Rows[rowIndex];
                    if (row is null) {
                        rows.Add(Array.Empty<string>());
                        continue;
                    }

                    rows.Add(row.Select(static value => value ?? string.Empty).ToArray());
                }
            }

            mapped.Add(new OfficeImoChunkTable {
                Title = table.Title,
                Columns = columns,
                Rows = rows,
                TotalRowCount = table.TotalRowCount,
                Truncated = table.Truncated
            });
        }

        return mapped.Count == 0 ? null : mapped;
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

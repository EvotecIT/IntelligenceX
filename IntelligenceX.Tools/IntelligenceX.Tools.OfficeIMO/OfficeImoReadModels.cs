using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Result model for <c>officeimo_read</c>.
/// </summary>
public sealed class OfficeImoReadResult {
    private IReadOnlyList<ToolNextActionModel> _nextActions = Array.Empty<ToolNextActionModel>();
    private IReadOnlyDictionary<string, string> _handoff = ToolChainingHints.EmptyMap;

    /// <summary>
    /// Files ingested (resolved full paths).
    /// </summary>
    public List<string> Files { get; set; } = new();

    /// <summary>
    /// Normalized extraction chunks.
    /// </summary>
    public List<OfficeImoChunk> Chunks { get; set; } = new();

    /// <summary>
    /// Source-level document payloads optimized for indexing/database upserts.
    /// </summary>
    public List<OfficeImoDocument> Documents { get; set; } = new();

    /// <summary>
    /// Warnings (for example: truncation or skipped files).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Whether ingestion was truncated by caps.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>
    /// Output shape selected by caller: <c>chunks</c>, <c>documents</c>, or <c>both</c>.
    /// </summary>
    public string OutputMode { get; set; } = "chunks";

    /// <summary>
    /// Files considered for ingestion (allowed extension scope).
    /// </summary>
    public int FilesScanned { get; set; }

    /// <summary>
    /// Files parsed successfully.
    /// </summary>
    public int FilesParsed { get; set; }

    /// <summary>
    /// Files skipped during ingestion.
    /// </summary>
    public int FilesSkipped { get; set; }

    /// <summary>
    /// Total bytes accepted for parsed files.
    /// </summary>
    public long BytesRead { get; set; }

    /// <summary>
    /// Total chunks produced by extraction before output shaping caps.
    /// </summary>
    public int ChunksProduced { get; set; }

    /// <summary>
    /// Total chunk objects returned in this tool payload after output shaping caps.
    /// In <c>both</c> mode this includes flat chunks plus per-document chunks.
    /// </summary>
    public int ChunksReturned { get; set; }

    /// <summary>
    /// Aggregated token estimate across returned chunk objects (best-effort).
    /// </summary>
    public int TokenEstimateReturned { get; set; }

    /// <summary>
    /// Advisory next actions for model/tool chaining.
    /// </summary>
    public IReadOnlyList<ToolNextActionModel> NextActions {
        get => _nextActions;
        set => _nextActions = NormalizeNextActions(value);
    }

    /// <summary>
    /// Opaque continuation cursor.
    /// </summary>
    public string Cursor { get; set; } = string.Empty;

    /// <summary>
    /// Opaque resume token for orchestration loops.
    /// </summary>
    public string ResumeToken { get; set; } = string.Empty;

    /// <summary>
    /// Structured handoff payload for downstream tools.
    /// Keys are normalized by trimming surrounding whitespace; normalized-key collisions use last-write-wins semantics.
    /// </summary>
    public IReadOnlyDictionary<string, string> Handoff {
        get => _handoff;
        set => _handoff = NormalizeHandoff(value);
    }

    /// <summary>
    /// Best-effort confidence score (0..1) for this extraction context.
    /// </summary>
    public double Confidence { get; set; } = 0.5d;

    private static IReadOnlyList<ToolNextActionModel> NormalizeNextActions(IReadOnlyList<ToolNextActionModel>? value) {
        if (value is null || value.Count == 0) {
            return Array.Empty<ToolNextActionModel>();
        }

        var normalized = value
            .Where(static action => action is not null)
            .ToList();
        if (normalized.Count == 0) {
            return Array.Empty<ToolNextActionModel>();
        }

        return new ReadOnlyCollection<ToolNextActionModel>(normalized);
    }

    // Keys are trimmed; collisions after normalization are resolved as last-write-wins.
    private static IReadOnlyDictionary<string, string> NormalizeHandoff(IReadOnlyDictionary<string, string>? value) {
        if (value is null || value.Count == 0) {
            return ToolChainingHints.EmptyMap;
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in value) {
            if (!string.IsNullOrWhiteSpace(entry.Key)) {
                normalized[entry.Key.Trim()] = entry.Value ?? string.Empty;
            }
        }

        return normalized.Count == 0
            ? ToolChainingHints.EmptyMap
            : new ReadOnlyDictionary<string, string>(normalized);
    }
}

/// <summary>
/// Source-level ingestion payload.
/// </summary>
public sealed class OfficeImoDocument {
    /// <summary>
    /// Source file path.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Stable source identifier when available.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// Source content hash when available.
    /// </summary>
    public string? SourceHash { get; set; }

    /// <summary>
    /// Source last write timestamp (UTC) when available.
    /// </summary>
    public DateTime? SourceLastWriteUtc { get; set; }

    /// <summary>
    /// Source length in bytes when available.
    /// </summary>
    public long? SourceLengthBytes { get; set; }

    /// <summary>
    /// True when parsing succeeded for the source.
    /// </summary>
    public bool Parsed { get; set; }

    /// <summary>
    /// Chunks produced by extraction for this source.
    /// </summary>
    public int ChunksProduced { get; set; }

    /// <summary>
    /// Chunks returned for this source after output shaping caps.
    /// </summary>
    public int ChunksReturned { get; set; }

    /// <summary>
    /// Token estimate produced by extraction for this source.
    /// </summary>
    public int TokenEstimateTotal { get; set; }

    /// <summary>
    /// Token estimate returned for this source after output shaping caps.
    /// </summary>
    public int TokenEstimateReturned { get; set; }

    /// <summary>
    /// Source-level warnings.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Source-level returned chunks (optional based on output mode and flags).
    /// </summary>
    public List<OfficeImoChunk> Chunks { get; set; } = new();
}

/// <summary>
/// Normalized source location metadata for a chunk.
/// </summary>
public sealed class OfficeImoChunkLocation {
    /// <summary>
    /// Source path used for citations.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Optional emitted chunk index (0-based).
    /// </summary>
    public int? BlockIndex { get; set; }

    /// <summary>
    /// Optional source block index within the input document.
    /// </summary>
    public int? SourceBlockIndex { get; set; }

    /// <summary>
    /// Optional 1-based start line number (Markdown/text inputs).
    /// </summary>
    public int? StartLine { get; set; }

    /// <summary>
    /// Optional heading path label.
    /// </summary>
    public string? HeadingPath { get; set; }

    /// <summary>
    /// Optional sheet name (Excel).
    /// </summary>
    public string? Sheet { get; set; }

    /// <summary>
    /// Optional A1 range descriptor (Excel).
    /// </summary>
    public string? A1Range { get; set; }

    /// <summary>
    /// Optional 1-based slide number (PowerPoint).
    /// </summary>
    public int? Slide { get; set; }

    /// <summary>
    /// Optional 1-based page number (PDF).
    /// </summary>
    public int? Page { get; set; }
}

/// <summary>
/// Normalized table payload for chunk outputs.
/// </summary>
public sealed class OfficeImoChunkTable {
    /// <summary>
    /// Optional title/label.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Column headers.
    /// </summary>
    public IReadOnlyList<string> Columns { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Rows aligned with <see cref="Columns"/>.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = Array.Empty<IReadOnlyList<string>>();

    /// <summary>
    /// Total row count before truncation.
    /// </summary>
    public int TotalRowCount { get; set; }

    /// <summary>
    /// True when <see cref="Rows"/> was truncated.
    /// </summary>
    public bool Truncated { get; set; }
}

/// <summary>
/// Minimal chunk shape intended for stable tool contracts.
/// </summary>
public sealed class OfficeImoChunk {
    /// <summary>
    /// Stable chunk identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Chunk kind (word/excel/powerpoint/markdown/text/unknown).
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Primary text representation intended for model reasoning.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional Markdown representation (when available).
    /// </summary>
    public string? Markdown { get; set; }

    /// <summary>
    /// Optional location metadata.
    /// </summary>
    public OfficeImoChunkLocation? Location { get; set; }

    /// <summary>
    /// Optional table data (Excel or extracted tables).
    /// </summary>
    public IReadOnlyList<OfficeImoChunkTable>? Tables { get; set; }

    /// <summary>
    /// Optional per-chunk warnings.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; set; }

    /// <summary>
    /// Stable source identifier when available.
    /// </summary>
    public string? SourceId { get; set; }

    /// <summary>
    /// Source content hash when available.
    /// </summary>
    public string? SourceHash { get; set; }

    /// <summary>
    /// Chunk content hash when available.
    /// </summary>
    public string? ChunkHash { get; set; }

    /// <summary>
    /// Source last write timestamp (UTC) when available.
    /// </summary>
    public DateTime? SourceLastWriteUtc { get; set; }

    /// <summary>
    /// Source length in bytes when available.
    /// </summary>
    public long? SourceLengthBytes { get; set; }

    /// <summary>
    /// Best-effort token estimate for prompt budgeting.
    /// </summary>
    public int? TokenEstimate { get; set; }
}

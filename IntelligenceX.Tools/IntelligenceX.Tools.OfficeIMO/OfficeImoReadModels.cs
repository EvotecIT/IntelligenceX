using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Result model for <c>officeimo_read</c>.
/// </summary>
public sealed class OfficeImoReadResult {
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
    /// Total chunks returned in this tool payload after output shaping caps.
    /// </summary>
    public int ChunksReturned { get; set; }

    /// <summary>
    /// Aggregated token estimate across returned chunks (best-effort).
    /// </summary>
    public int TokenEstimateReturned { get; set; }
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
    public object? Location { get; set; }

    /// <summary>
    /// Optional table data (Excel or extracted tables).
    /// </summary>
    public object? Tables { get; set; }

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

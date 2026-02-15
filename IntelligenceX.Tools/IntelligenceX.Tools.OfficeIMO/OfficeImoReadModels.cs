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
    /// Warnings (for example: truncation or skipped files).
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Whether ingestion was truncated by caps.
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
    public object? Location { get; set; }

    /// <summary>
    /// Optional table data (Excel or extracted tables).
    /// </summary>
    public object? Tables { get; set; }

    /// <summary>
    /// Optional per-chunk warnings.
    /// </summary>
    public IReadOnlyList<string>? Warnings { get; set; }
}


using System;
using System.Collections.Generic;
using ChartForgeX.Markup;
using ChartForgeX.VisualArtifacts;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Identifies native transcript content projected from an upstream Markdown AST.
/// </summary>
internal enum NativeTranscriptContentKind {
    Paragraph,
    Code,
    Table,
    Visual,
    Diagnostic
}

/// <summary>
/// Native transcript content item. Markdown parsing is owned by OfficeIMO; this type is only the app-side projection.
/// </summary>
internal sealed class NativeTranscriptContent {
    public NativeTranscriptContent(
        NativeTranscriptContentKind kind,
        string text,
        string? language = null,
        string? caption = null,
        NativeTranscriptTable? table = null,
        NativeTranscriptVisual? visual = null,
        int? sourceLine = null) {
        Kind = kind;
        Text = text ?? string.Empty;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        Table = table;
        Visual = visual;
        SourceLine = sourceLine;
    }

    public NativeTranscriptContentKind Kind { get; }

    public string Text { get; }

    public string? Language { get; }

    public string? Caption { get; }

    public NativeTranscriptTable? Table { get; }

    public NativeTranscriptVisual? Visual { get; }

    public int? SourceLine { get; }
}

/// <summary>
/// Structured Markdown table projection for native table controls.
/// </summary>
internal sealed class NativeTranscriptTable {
    public NativeTranscriptTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) {
        Headers = headers ?? Array.Empty<string>();
        Rows = rows ?? Array.Empty<IReadOnlyList<string>>();
    }

    public IReadOnlyList<string> Headers { get; }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }
}

/// <summary>
/// Product-neutral visual projection returned by ChartForgeX or queued for ChartForgeX rendering.
/// </summary>
internal sealed class NativeTranscriptVisual {
    public NativeTranscriptVisual(
        VisualMarkupKind kind,
        string fenceName,
        string fenceInfo,
        string payload,
        IReadOnlyDictionary<string, string> attributes,
        VisualArtifact? artifact = null,
        NativeVisualPreview? preview = null) {
        Kind = kind;
        FenceName = fenceName ?? string.Empty;
        FenceInfo = fenceInfo ?? string.Empty;
        Payload = payload ?? string.Empty;
        Attributes = attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Artifact = artifact;
        Preview = preview;
    }

    public VisualMarkupKind Kind { get; }

    public string FenceName { get; }

    public string FenceInfo { get; }

    public string Payload { get; }

    public IReadOnlyDictionary<string, string> Attributes { get; }

    public VisualArtifact? Artifact { get; }

    public NativeVisualPreview? Preview { get; }
}

/// <summary>
/// Static native preview bytes for a visual artifact.
/// </summary>
internal sealed class NativeVisualPreview {
    public NativeVisualPreview(string? svg, byte[]? png) {
        Svg = string.IsNullOrWhiteSpace(svg) ? null : svg;
        Png = png == null || png.Length == 0 ? null : png;
    }

    public string? Svg { get; }

    public byte[]? Png { get; }

    public bool HasPng => Png is { Length: > 0 };
}

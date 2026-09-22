using System;
using System.Collections.Generic;
using ChartForgeX.Markup;
using ChartForgeX.VisualArtifacts;
using OfficeIMO.Markdown;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Identifies native transcript content projected from an upstream Markdown AST.
/// </summary>
internal enum NativeTranscriptContentKind {
    Heading,
    Paragraph,
    List,
    Quote,
    Callout,
    Details,
    Code,
    Divider,
    Table,
    Image,
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
        int? sourceLine = null,
        IReadOnlyList<NativeTranscriptInline>? inlines = null,
        int? headingLevel = null,
        NativeTranscriptList? list = null,
        NativeTranscriptContainer? container = null,
        NativeTranscriptImage? image = null) {
        Kind = kind;
        Text = text ?? string.Empty;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        Caption = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        Table = table;
        Visual = visual;
        SourceLine = sourceLine;
        Inlines = inlines ?? Array.Empty<NativeTranscriptInline>();
        HeadingLevel = headingLevel;
        List = list;
        Container = container;
        Image = image;
    }

    public NativeTranscriptContentKind Kind { get; }

    public string Text { get; }

    public string? Language { get; }

    public string? Caption { get; }

    public NativeTranscriptTable? Table { get; }

    public NativeTranscriptVisual? Visual { get; }

    public int? SourceLine { get; }

    public IReadOnlyList<NativeTranscriptInline> Inlines { get; }

    public int? HeadingLevel { get; }

    public NativeTranscriptList? List { get; }

    public NativeTranscriptContainer? Container { get; }

    public NativeTranscriptImage? Image { get; }
}

/// <summary>
/// Native image projection retaining the OfficeIMO source, display metadata, and optional link target.
/// </summary>
internal sealed class NativeTranscriptImage {
    public NativeTranscriptImage(
        string source,
        string? alternateText,
        string? title,
        string? linkUrl,
        double? width,
        double? height) {
        Source = (source ?? string.Empty).Trim();
        AlternateText = string.IsNullOrWhiteSpace(alternateText) ? "Image" : alternateText.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        LinkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim();
        Width = width is > 0 ? width : null;
        Height = height is > 0 ? height : null;
    }

    public string Source { get; }

    public string AlternateText { get; }

    public string? Title { get; }

    public string? LinkUrl { get; }

    public double? Width { get; }

    public double? Height { get; }
}

/// <summary>
/// Inline formatting projection retained from the OfficeIMO native AST.
/// </summary>
internal sealed class NativeTranscriptInline {
    public NativeTranscriptInline(
        MarkdownNativeInlineKind kind,
        string text,
        string? target = null,
        IReadOnlyList<NativeTranscriptInline>? children = null) {
        Kind = kind;
        Text = text ?? string.Empty;
        Target = string.IsNullOrWhiteSpace(target) ? null : target.Trim();
        Children = children ?? Array.Empty<NativeTranscriptInline>();
    }

    public MarkdownNativeInlineKind Kind { get; }

    public string Text { get; }

    public string? Target { get; }

    public IReadOnlyList<NativeTranscriptInline> Children { get; }
}

/// <summary>
/// Ordered, unordered, or task-list projection.
/// </summary>
internal sealed class NativeTranscriptList {
    public NativeTranscriptList(bool isOrdered, int start, IReadOnlyList<NativeTranscriptListItem> items) {
        IsOrdered = isOrdered;
        Start = start;
        Items = items ?? Array.Empty<NativeTranscriptListItem>();
    }

    public bool IsOrdered { get; }

    public int Start { get; }

    public IReadOnlyList<NativeTranscriptListItem> Items { get; }
}

/// <summary>
/// One native list item including its lead inline content and nested blocks.
/// </summary>
internal sealed class NativeTranscriptListItem {
    public NativeTranscriptListItem(
        string text,
        IReadOnlyList<NativeTranscriptInline>? inlines,
        bool isTask,
        bool isChecked,
        IReadOnlyList<NativeTranscriptContent>? children) {
        Text = text ?? string.Empty;
        Inlines = inlines ?? Array.Empty<NativeTranscriptInline>();
        IsTask = isTask;
        IsChecked = isChecked;
        Children = children ?? Array.Empty<NativeTranscriptContent>();
    }

    public string Text { get; }

    public IReadOnlyList<NativeTranscriptInline> Inlines { get; }

    public bool IsTask { get; }

    public bool IsChecked { get; }

    public IReadOnlyList<NativeTranscriptContent> Children { get; }
}

/// <summary>
/// Nested native block container used for quotes and semantic callouts.
/// </summary>
internal sealed class NativeTranscriptContainer {
    public NativeTranscriptContainer(
        string kind,
        string? title,
        string? badge,
        IReadOnlyList<NativeTranscriptContent>? children,
        bool? isExpanded = null) {
        Kind = string.IsNullOrWhiteSpace(kind) ? "note" : kind.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Badge = string.IsNullOrWhiteSpace(badge) ? null : badge.Trim();
        Children = children ?? Array.Empty<NativeTranscriptContent>();
        IsExpanded = isExpanded;
    }

    public string Kind { get; }

    public string? Title { get; }

    public string? Badge { get; }

    public IReadOnlyList<NativeTranscriptContent> Children { get; }

    public bool? IsExpanded { get; }
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

    public NativeTranscriptVisual WithPreview(NativeVisualPreview preview) {
        ArgumentNullException.ThrowIfNull(preview);
        return new NativeTranscriptVisual(
            Kind,
            FenceName,
            FenceInfo,
            Payload,
            Attributes,
            Artifact,
            preview);
    }
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

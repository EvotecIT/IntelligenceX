using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Markdown contract builder for tool summaries.
/// </summary>
/// <remarks>
/// Generates CommonMark/GFM-friendly blocks designed for OfficeIMO.Markdown and OfficeIMO.MarkdownRenderer.
/// Keep output predictable: headings, tables, fenced code blocks, Mermaid, and chart fences.
/// </remarks>
public sealed class ToolMarkdownDocument {
    private readonly List<string> _blocks = new();

    /// <summary>
    /// Adds a heading block.
    /// </summary>
    public ToolMarkdownDocument AddHeading(int level, string text) {
        _blocks.Add(ToolMarkdown.Heading(level, text));
        return this;
    }

    /// <summary>
    /// Adds a paragraph block.
    /// </summary>
    public ToolMarkdownDocument AddParagraph(string? text) {
        if (!string.IsNullOrWhiteSpace(text)) {
            _blocks.Add(text.Trim());
        }
        return this;
    }

    /// <summary>
    /// Adds a bullet list block.
    /// </summary>
    public ToolMarkdownDocument AddBullets(IEnumerable<string> items) {
        var block = ToolMarkdown.Bullets(items);
        if (!string.IsNullOrWhiteSpace(block)) {
            _blocks.Add(block);
        }
        return this;
    }

    /// <summary>
    /// Adds a key/value bullet list block with values rendered as inline code.
    /// </summary>
    public ToolMarkdownDocument AddBulletsCode(IEnumerable<(string Key, string Value)> items) {
        var block = ToolMarkdown.BulletsCode(items);
        if (!string.IsNullOrWhiteSpace(block)) {
            _blocks.Add(block);
        }
        return this;
    }

    /// <summary>
    /// Adds a markdown table block.
    /// </summary>
    public ToolMarkdownDocument AddTable(
        string title,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows,
        int totalCount,
        bool truncated) {
        _blocks.Add(MarkdownTable.Table(title, headers, rows, totalCount, truncated));
        return this;
    }

    /// <summary>
    /// Adds a fenced code block.
    /// </summary>
    public ToolMarkdownDocument AddCode(string? language, string? content) {
        _blocks.Add(ToolMarkdown.CodeBlock(language, content));
        return this;
    }

    /// <summary>
    /// Adds a Mermaid diagram fenced block.
    /// </summary>
    /// <param name="source">Mermaid source body.</param>
    /// <param name="title">Optional heading rendered above the diagram.</param>
    /// <param name="headingLevel">Heading level for <paramref name="title"/>.</param>
    public ToolMarkdownDocument AddMermaid(string? source, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.Mermaid(source));
        return this;
    }

    /// <summary>
    /// Adds a chart fence block (<c>chart</c>) intended for hosts that enable chart rendering.
    /// </summary>
    /// <param name="chartJson">Chart JSON payload.</param>
    /// <param name="title">Optional heading rendered above the chart.</param>
    /// <param name="headingLevel">Heading level for <paramref name="title"/>.</param>
    public ToolMarkdownDocument AddChart(string? chartJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.CodeBlock("chart", chartJson));
        return this;
    }

    /// <summary>
    /// Adds a pre-built markdown block as-is.
    /// </summary>
    public ToolMarkdownDocument AddBlock(string? markdown) {
        if (!string.IsNullOrWhiteSpace(markdown)) {
            _blocks.Add(markdown.TrimEnd());
        }
        return this;
    }

    /// <summary>
    /// Builds the final markdown document.
    /// </summary>
    public string Build() {
        return ToolMarkdown.JoinBlocks(_blocks.ToArray());
    }
}

/// <summary>
/// Factory helpers for markdown contract documents.
/// </summary>
public static class ToolMarkdownContract {
    /// <summary>
    /// Creates an empty markdown contract document builder.
    /// </summary>
    public static ToolMarkdownDocument Create() => new();
}

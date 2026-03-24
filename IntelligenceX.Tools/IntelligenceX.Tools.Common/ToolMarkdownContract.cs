using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Markdown contract builder for tool summaries.
/// </summary>
/// <remarks>
/// Generates CommonMark/GFM-friendly blocks designed for OfficeIMO.Markdown and OfficeIMO.MarkdownRenderer.
/// Keep output predictable: headings, tables, fenced code blocks, Mermaid, and generic visual fences with opt-in IntelligenceX aliases.
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
    /// Adds a generic chart fence block (<c>chart</c>).
    /// </summary>
    /// <param name="chartJson">Chart JSON payload.</param>
    /// <param name="title">Optional heading rendered above the chart.</param>
    /// <param name="headingLevel">Heading level for <paramref name="title"/>.</param>
    public ToolMarkdownDocument AddChart(string? chartJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.Chart(chartJson));
        return this;
    }

    /// <summary>
    /// Adds a generic network fence block (<c>network</c>).
    /// </summary>
    /// <param name="networkJson">Network JSON payload.</param>
    /// <param name="title">Optional heading rendered above the network.</param>
    /// <param name="headingLevel">Heading level for <paramref name="title"/>.</param>
    public ToolMarkdownDocument AddNetwork(string? networkJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.Network(networkJson));
        return this;
    }

    /// <summary>
    /// Adds a generic dataview fence block (<c>dataview</c>).
    /// </summary>
    /// <param name="dataViewJson">Dataview JSON payload.</param>
    /// <param name="title">Optional heading rendered above the dataview.</param>
    /// <param name="headingLevel">Heading level for <paramref name="title"/>.</param>
    public ToolMarkdownDocument AddDataView(string? dataViewJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.DataView(dataViewJson));
        return this;
    }

    /// <summary>
    /// Adds an IntelligenceX chart fence block (<c>ix-chart</c>) for compatibility with existing hosts.
    /// Prefer <see cref="AddChart"/> for new tool output.
    /// </summary>
    public ToolMarkdownDocument AddIxChart(string? chartJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.IxChart(chartJson));
        return this;
    }

    /// <summary>
    /// Adds an IntelligenceX network fence block (<c>ix-network</c>) for compatibility with existing hosts.
    /// Prefer <see cref="AddNetwork"/> for new tool output.
    /// </summary>
    public ToolMarkdownDocument AddIxNetwork(string? networkJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.IxNetwork(networkJson));
        return this;
    }

    /// <summary>
    /// Adds an IntelligenceX dataview fence block (<c>ix-dataview</c>) for compatibility with existing hosts.
    /// Prefer <see cref="AddDataView"/> for new tool output.
    /// </summary>
    public ToolMarkdownDocument AddIxDataView(string? dataViewJson, string? title = null, int headingLevel = 4) {
        if (!string.IsNullOrWhiteSpace(title)) {
            _blocks.Add(ToolMarkdown.Heading(headingLevel, title));
        }
        _blocks.Add(ToolMarkdown.IxDataView(dataViewJson));
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

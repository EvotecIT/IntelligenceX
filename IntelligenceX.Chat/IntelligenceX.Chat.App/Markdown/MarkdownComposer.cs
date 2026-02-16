using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Typed markdown composition helper used by chat/prompt formatting paths.
/// </summary>
internal sealed class MarkdownComposer {
    private readonly List<MarkdownNode> _nodes = new();

    /// <summary>
    /// Adds a markdown heading.
    /// </summary>
    /// <param name="text">Heading text.</param>
    /// <param name="level">Heading level (1-6).</param>
    /// <returns>Current composer.</returns>
    public MarkdownComposer Heading(string text, int level = 2) {
        _nodes.Add(new HeadingNode(text, level));
        return this;
    }

    /// <summary>
    /// Adds a paragraph block.
    /// </summary>
    /// <param name="text">Paragraph text.</param>
    /// <returns>Current composer.</returns>
    public MarkdownComposer Paragraph(string text) {
        _nodes.Add(new ParagraphNode(text));
        return this;
    }

    /// <summary>
    /// Adds an unordered list item.
    /// </summary>
    /// <param name="text">Item text.</param>
    /// <returns>Current composer.</returns>
    public MarkdownComposer Bullet(string text) {
        _nodes.Add(new BulletNode(text));
        return this;
    }

    /// <summary>
    /// Adds a block quote.
    /// </summary>
    /// <param name="text">Quote text.</param>
    /// <returns>Current composer.</returns>
    public MarkdownComposer Quote(string text) {
        _nodes.Add(new QuoteNode(text));
        return this;
    }

    /// <summary>
    /// Adds a fenced code block.
    /// </summary>
    /// <param name="language">Fence language identifier.</param>
    /// <param name="content">Code content.</param>
    /// <returns>Current composer.</returns>
    public MarkdownComposer CodeFence(string language, string content) {
        _nodes.Add(new CodeFenceNode(language, content));
        return this;
    }

    /// <summary>
    /// Adds preformatted markdown as-is.
    /// </summary>
    /// <param name="markdown">Markdown fragment.</param>
    /// <returns>Current composer.</returns>
    public MarkdownComposer Raw(string markdown) {
        _nodes.Add(new RawNode(markdown));
        return this;
    }

    /// <summary>
    /// Adds a blank line separator.
    /// </summary>
    /// <returns>Current composer.</returns>
    public MarkdownComposer BlankLine() {
        _nodes.Add(new BlankLineNode());
        return this;
    }

    /// <summary>
    /// Builds markdown text.
    /// </summary>
    /// <returns>Rendered markdown.</returns>
    public string Build() {
        var sb = new StringBuilder();
        foreach (var node in _nodes) {
            switch (node) {
                case HeadingNode heading:
                    RenderHeading(sb, heading);
                    break;
                case ParagraphNode paragraph:
                    RenderParagraph(sb, paragraph.Text);
                    break;
                case BulletNode bullet:
                    RenderBullet(sb, bullet.Text);
                    break;
                case QuoteNode quote:
                    RenderQuote(sb, quote.Text);
                    break;
                case CodeFenceNode fence:
                    RenderCodeFence(sb, fence.Language, fence.Content);
                    break;
                case RawNode raw:
                    RenderRaw(sb, raw.Markdown);
                    break;
                case BlankLineNode:
                    sb.AppendLine();
                    break;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void RenderHeading(StringBuilder sb, HeadingNode heading) {
        var level = Math.Clamp(heading.Level, 1, 6);
        var text = (heading.Text ?? string.Empty).Trim();
        if (text.Length == 0) {
            return;
        }

        sb.Append(new string('#', level)).Append(' ').AppendLine(text);
    }

    private static void RenderParagraph(StringBuilder sb, string text) {
        foreach (var line in EnumerateLines(text)) {
            sb.AppendLine(line);
        }
    }

    private static void RenderBullet(StringBuilder sb, string text) {
        var lines = EnumerateLines(text);
        var first = true;
        foreach (var line in lines) {
            if (first) {
                sb.Append("- ").AppendLine(line);
                first = false;
            } else {
                sb.Append("  ").AppendLine(line);
            }
        }

        if (first) {
            sb.AppendLine("- ");
        }
    }

    private static void RenderQuote(StringBuilder sb, string text) {
        var hadLine = false;
        foreach (var line in EnumerateLines(text)) {
            sb.Append("> ").AppendLine(line);
            hadLine = true;
        }

        if (!hadLine) {
            sb.AppendLine("> ");
        }
    }

    private static void RenderCodeFence(StringBuilder sb, string language, string content) {
        var body = (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var fence = BuildFence(body);
        var lang = (language ?? string.Empty).Trim();
        sb.Append(fence).AppendLine(lang);
        foreach (var line in EnumerateLines(body)) {
            sb.AppendLine(line);
        }
        sb.AppendLine(fence);
    }

    private static void RenderRaw(StringBuilder sb, string markdown) {
        var lines = EnumerateLines(markdown);
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
    }

    private static IReadOnlyList<string> EnumerateLines(string text) {
        var value = (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return value.Split('\n', StringSplitOptions.None);
    }

    private static string BuildFence(string content) {
        var longestBackticks = LongestRun(content, '`');
        var longestTildes = LongestRun(content, '~');

        var backtickLength = Math.Max(3, longestBackticks + 1);
        var tildeLength = Math.Max(3, longestTildes + 1);
        var marker = backtickLength <= tildeLength ? '`' : '~';
        var length = marker == '`' ? backtickLength : tildeLength;
        return new string(marker, length);
    }

    private static int LongestRun(string text, char marker) {
        if (string.IsNullOrEmpty(text)) {
            return 0;
        }

        var longest = 0;
        var current = 0;
        for (var i = 0; i < text.Length; i++) {
            if (text[i] == marker) {
                current++;
                if (current > longest) {
                    longest = current;
                }
            } else {
                current = 0;
            }
        }

        return longest;
    }

    private abstract record MarkdownNode;
    private sealed record HeadingNode(string Text, int Level) : MarkdownNode;
    private sealed record ParagraphNode(string Text) : MarkdownNode;
    private sealed record BulletNode(string Text) : MarkdownNode;
    private sealed record QuoteNode(string Text) : MarkdownNode;
    private sealed record CodeFenceNode(string Language, string Content) : MarkdownNode;
    private sealed record RawNode(string Markdown) : MarkdownNode;
    private sealed record BlankLineNode : MarkdownNode;
}

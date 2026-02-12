using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Small markdown helpers intended for stable UI tool traces.
/// </summary>
/// <remarks>
/// Keep this intentionally minimal: produce CommonMark-friendly output (headings, bullet lists, code fences).
/// Prefer this over ad-hoc string concatenation inside tools so output stays consistent across packs.
/// </remarks>
public static class ToolMarkdown {
    /// <summary>
    /// Creates a markdown heading line.
    /// </summary>
    /// <param name="level">Heading level (1-6).</param>
    /// <param name="text">Heading text.</param>
    public static string Heading(int level, string text) {
        level = Math.Clamp(level, 1, 6);
        return new string('#', level) + " " + (text ?? string.Empty).Trim();
    }

    /// <summary>
    /// Creates a markdown bullet list from <paramref name="items"/>.
    /// </summary>
    public static string Bullets(IEnumerable<string> items) {
        if (items is null) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var item in items) {
            var v = EscapeInline(item);
            if (string.IsNullOrWhiteSpace(v)) {
                continue;
            }
            sb.Append("- ").AppendLine(v);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Creates a markdown bullet list from key/value pairs (rendering values as inline code).
    /// </summary>
    public static string BulletsCode(IEnumerable<(string Key, string Value)> items) {
        if (items is null) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var (k, v) in items) {
            if (string.IsNullOrWhiteSpace(k)) {
                continue;
            }
            sb.Append("- ").Append(EscapeInline(k.Trim())).Append(": ").AppendLine(InlineCode(v));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Creates a fenced code block. Uses a fence that avoids conflicts with code content.
    /// </summary>
    /// <param name="language">Language tag (for example: text, json, mermaid).</param>
    /// <param name="content">Block content.</param>
    public static string CodeBlock(string? language, string? content) {
        var body = NormalizeBlock(content);

        // If the content contains triple backticks, use tildes as the fence.
        var fence = body.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
        var lang = string.IsNullOrWhiteSpace(language) ? string.Empty : language.Trim();

        var sb = new StringBuilder(body.Length + 16);
        sb.Append(fence);
        if (!string.IsNullOrWhiteSpace(lang)) {
            sb.Append(lang);
        }
        sb.AppendLine();
        sb.AppendLine(body);
        sb.Append(fence);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Creates a Mermaid fenced code block.
    /// </summary>
    public static string Mermaid(string? source) => CodeBlock("mermaid", source);

    /// <summary>
    /// Renders <paramref name="value"/> as markdown inline code, using a safe backtick fence.
    /// </summary>
    public static string InlineCode(string? value) {
        var v = value ?? string.Empty;
        v = v.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

        if (v.Length == 0) {
            return "``";
        }

        // Find the longest run of backticks and wrap with a longer run.
        var longest = 0;
        var current = 0;
        for (var i = 0; i < v.Length; i++) {
            if (v[i] == '`') {
                current++;
                if (current > longest) longest = current;
            } else {
                current = 0;
            }
        }

        var fence = new string('`', longest + 1);
        // If the value starts/ends with space, pad with a space inside the fence as well.
        var pad = v.StartsWith(' ') || v.EndsWith(' ') ? " " : string.Empty;
        return fence + pad + v + pad + fence;
    }

    /// <summary>
    /// Best-effort "single line" escape for markdown list items (keeps output readable and stable).
    /// </summary>
    public static string EscapeInline(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        // Avoid accidental multi-line list nesting.
        var v = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();

        // Avoid markdown list injection. Keep it readable rather than perfectly escaped.
        if (v.StartsWith("-", StringComparison.Ordinal) || v.StartsWith("*", StringComparison.Ordinal) || v.StartsWith("+", StringComparison.Ordinal)) {
            v = "\\" + v;
        }
        return v;
    }

    /// <summary>
    /// Joins markdown blocks with a blank line.
    /// </summary>
    public static string JoinBlocks(params string[] blocks) {
        if (blocks is null || blocks.Length == 0) {
            return string.Empty;
        }
        var list = blocks
            .Where(static b => !string.IsNullOrWhiteSpace(b))
            .Select(static b => b.TrimEnd())
            .ToArray();
        return list.Length == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, list).TrimEnd();
    }

    /// <summary>
    /// Builds a common summary section: heading + key/value bullet list and optional code block.
    /// </summary>
    /// <param name="title">Heading text.</param>
    /// <param name="facts">Key/value facts rendered as bullet items with inline-code values.</param>
    /// <param name="codeLanguage">Optional code block language.</param>
    /// <param name="codeContent">Optional code block content.</param>
    public static string SummaryFacts(
        string title,
        IEnumerable<(string Key, string Value)> facts,
        string? codeLanguage = null,
        string? codeContent = null) {
        var heading = Heading(3, title);
        var bullets = BulletsCode(facts ?? Array.Empty<(string Key, string Value)>());

        if (codeContent is null) {
            return JoinBlocks(heading, bullets);
        }

        return JoinBlocks(heading, bullets, CodeBlock(codeLanguage, codeContent));
    }

    /// <summary>
    /// Builds a common summary section: heading + one or more paragraphs.
    /// </summary>
    /// <param name="title">Heading text.</param>
    /// <param name="paragraphs">Summary paragraphs.</param>
    public static string SummaryText(string title, params string[] paragraphs) {
        var blocks = new List<string> { Heading(3, title) };
        if (paragraphs is not null) {
            for (var i = 0; i < paragraphs.Length; i++) {
                var paragraph = paragraphs[i];
                if (string.IsNullOrWhiteSpace(paragraph)) {
                    continue;
                }
                blocks.Add(paragraph.Trim());
            }
        }

        return JoinBlocks(blocks.ToArray());
    }

    private static string NormalizeBlock(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        // Prefer LF for renderers.
        return value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal).TrimEnd('\n');
    }
}

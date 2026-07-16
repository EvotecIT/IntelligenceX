using System;
using System.Collections.Generic;
using System.Linq;
using ChartForgeX.Markup;
using ChartForgeX.Markup.Mermaid;
using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer.IntelligenceX;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Projects OfficeIMO native Markdown blocks into native transcript content and delegates visual artifacts to ChartForgeX.
/// </summary>
internal static class NativeMarkdownProjection {
    public static IReadOnlyList<NativeTranscriptContent> Project(string? markdown) {
        var document = IntelligenceXMarkdownRenderer.ParseTranscriptNativeDesktopShell(markdown ?? string.Empty);
        var result = new List<NativeTranscriptContent>(document.Blocks.Count);
        var visualParser = new MermaidVisualMarkupParser();

        foreach (var block in document.Blocks) {
            switch (block) {
                case MarkdownNativeParagraphBlock paragraph:
                    AddParagraph(result, paragraph);
                    break;
                case MarkdownNativeCodeBlock code:
                    AddCode(result, code);
                    break;
                case MarkdownNativeTableBlock table:
                    AddTable(result, table);
                    break;
                case MarkdownNativeVisualBlock visual:
                    AddVisual(result, visualParser, visual);
                    break;
                case MarkdownNativeOtherBlock other:
                    AddOther(result, other);
                    break;
            }
        }

        return result;
    }

    private static void AddParagraph(ICollection<NativeTranscriptContent> result, MarkdownNativeParagraphBlock paragraph) {
        if (string.IsNullOrWhiteSpace(paragraph.Text)) {
            return;
        }

        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Paragraph,
            paragraph.Text,
            sourceLine: paragraph.SourceSpan?.StartLine));
    }

    private static void AddCode(ICollection<NativeTranscriptContent> result, MarkdownNativeCodeBlock code) {
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Code,
            code.Content,
            language: code.Language,
            caption: code.Caption,
            sourceLine: code.SourceSpan?.StartLine));
    }

    private static void AddTable(ICollection<NativeTranscriptContent> result, MarkdownNativeTableBlock table) {
        var headers = table.HeaderCells.Select(static cell => cell.Text).ToArray();
        var rows = table.Rows
            .Select(static row => (IReadOnlyList<string>)row.Select(static cell => cell.Text).ToArray())
            .ToArray();

        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Table,
            string.Empty,
            table: new NativeTranscriptTable(headers, rows),
            sourceLine: table.SourceSpan?.StartLine));
    }

    private static void AddVisual(ICollection<NativeTranscriptContent> result, MermaidVisualMarkupParser parser, MarkdownNativeVisualBlock visual) {
        var fenceInfo = ResolveVisualFenceIdentifier(visual.SemanticKind, visual.Language, visual.InfoString);
        var fenceLine = visual.SourceSpan?.StartLine ?? 1;
        var endLine = visual.SourceSpan?.EndLine ?? fenceLine;
        var scan = VisualMarkupScanner.ParseFenceBlock(
            fenceInfo,
            visual.Content,
            fenceLine,
            Math.Min(fenceLine + 1, endLine),
            endLine);
        var parse = parser.Parse(scan);
        foreach (var diagnostic in parse.Diagnostics) {
            result.Add(new NativeTranscriptContent(
                NativeTranscriptContentKind.Diagnostic,
                diagnostic.Message,
                sourceLine: diagnostic.Line));
        }

        var scanned = scan.Blocks.SingleOrDefault();
        if (scanned is null) {
            if (parse.Diagnostics.Count == 0) {
                result.Add(new NativeTranscriptContent(
                    NativeTranscriptContentKind.Diagnostic,
                    "Unsupported visual fence '" + fenceInfo + "'.",
                    sourceLine: fenceLine));
            }
            return;
        }

        var visualBlock = scanned;
        if (parse.Artifacts.Count == 0) {
            result.Add(new NativeTranscriptContent(
                NativeTranscriptContentKind.Visual,
                string.Empty,
                caption: visual.Caption,
                visual: new NativeTranscriptVisual(
                    visualBlock.Kind,
                    visualBlock.FenceName,
                    visualBlock.FenceInfo,
                    visualBlock.Payload,
                    visualBlock.Attributes),
                sourceLine: fenceLine));
            return;
        }

        foreach (var artifact in parse.Artifacts) {
            var preview = NativeVisualPreviewRenderer.TryRender(artifact);
            result.Add(new NativeTranscriptContent(
                NativeTranscriptContentKind.Visual,
                string.Empty,
                caption: visual.Caption,
                visual: new NativeTranscriptVisual(
                    visualBlock.Kind,
                    visualBlock.FenceName,
                    visualBlock.FenceInfo,
                    visualBlock.Payload,
                    visualBlock.Attributes,
                    artifact,
                    preview),
                sourceLine: fenceLine));
        }
    }

    private static void AddOther(ICollection<NativeTranscriptContent> result, MarkdownNativeOtherBlock other) {
        if (string.IsNullOrWhiteSpace(other.Markdown)) {
            return;
        }

        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Paragraph,
            other.Markdown,
            sourceLine: other.SourceSpan?.StartLine));
    }

    internal static string ResolveVisualFenceIdentifier(string? semanticKind, string? language, string? infoString) {
        if (!string.IsNullOrWhiteSpace(infoString)) {
            return infoString.Trim();
        }

        if (!string.IsNullOrWhiteSpace(language)) {
            return language.Trim();
        }

        return string.IsNullOrWhiteSpace(semanticKind)
            ? "unknown"
            : semanticKind.Trim();
    }

}

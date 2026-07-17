using System;
using System.Collections.Generic;
using System.Linq;
using ChartForgeX.Markup;
using ChartForgeX.Markup.Mermaid;
using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer.IntelligenceX;

namespace IntelligenceX.Chat.App.Native.Rendering;

/// <summary>
/// Projects OfficeIMO native Markdown blocks into native transcript content and delegates visual artifacts to ChartForgeX.
/// </summary>
internal static class NativeMarkdownProjection {
    public static IReadOnlyList<NativeTranscriptContent> Project(string role, string? markdown) {
        if (!TranscriptOutcomePresentationParser.TryParse(role, markdown, out var outcome)) {
            return Project(markdown);
        }

        return new[] {
            new NativeTranscriptContent(
                NativeTranscriptContentKind.Callout,
                string.Empty,
                container: new NativeTranscriptContainer(
                    outcome.Kind,
                    outcome.Headline,
                    outcome.Badge,
                    Project(outcome.DetailMarkdown)))
        };
    }

    public static IReadOnlyList<NativeTranscriptContent> Project(string? markdown) {
        var options = IntelligenceXMarkdownRenderer.CreateTranscriptDesktopShell();
        // The native host never executes HTML. Parsing block HTML here lets OfficeIMO project
        // safe structural Markdown extensions such as <details> while unknown HTML remains text.
        options.ReaderOptions.HtmlBlocks = true;
        var document = OfficeIMO.MarkdownRenderer.MarkdownRenderer.ParseNativeDocument(markdown ?? string.Empty, options);
        return ProjectBlocks(document.Blocks);
    }

    private static IReadOnlyList<NativeTranscriptContent> ProjectBlocks(IReadOnlyList<MarkdownNativeBlock> blocks) {
        var result = new List<NativeTranscriptContent>(blocks.Count);
        var visualParser = new MermaidVisualMarkupParser();

        foreach (var block in blocks) {
            switch (block) {
                case MarkdownNativeHeadingBlock heading:
                    AddHeading(result, heading);
                    break;
                case MarkdownNativeParagraphBlock paragraph:
                    AddParagraph(result, paragraph);
                    break;
                case MarkdownNativeListBlock list:
                    AddList(result, list);
                    break;
                case MarkdownNativeQuoteBlock quote:
                    AddQuote(result, quote);
                    break;
                case MarkdownNativeCalloutBlock callout:
                    AddCallout(result, callout);
                    break;
                case MarkdownNativeCustomContainerBlock container:
                    AddCustomContainer(result, container);
                    break;
                case MarkdownNativeDetailsBlock details:
                    AddDetails(result, details);
                    break;
                case MarkdownNativeImageBlock image:
                    AddImage(result, image);
                    break;
                case MarkdownNativeCodeBlock code:
                    AddCode(result, code);
                    break;
                case MarkdownNativeThematicBreakBlock divider:
                    AddDivider(result, divider);
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
                default:
                    AddFallback(result, block);
                    break;
            }
        }

        return result;
    }

    private static void AddHeading(ICollection<NativeTranscriptContent> result, MarkdownNativeHeadingBlock heading) {
        if (string.IsNullOrWhiteSpace(heading.Text)) return;
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Heading,
            heading.Text,
            sourceLine: heading.SourceSpan?.StartLine,
            inlines: ProjectInlines(heading.InlineRuns),
            headingLevel: heading.Level));
    }

    private static void AddParagraph(ICollection<NativeTranscriptContent> result, MarkdownNativeParagraphBlock paragraph) {
        if (string.IsNullOrWhiteSpace(paragraph.Text)) {
            return;
        }

        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Paragraph,
            paragraph.Text,
            sourceLine: paragraph.SourceSpan?.StartLine,
            inlines: ProjectInlines(paragraph.InlineRuns)));
    }

    private static void AddList(ICollection<NativeTranscriptContent> result, MarkdownNativeListBlock list) {
        var items = new List<NativeTranscriptListItem>(list.Items.Count);
        foreach (var item in list.Items) {
            var childContent = new List<NativeTranscriptContent>();
            for (var paragraphIndex = 1; paragraphIndex < item.Paragraphs.Count; paragraphIndex++) {
                var paragraph = item.Paragraphs[paragraphIndex];
                childContent.Add(new NativeTranscriptContent(
                    NativeTranscriptContentKind.Paragraph,
                    paragraph.Text,
                    sourceLine: paragraph.SourceSpan?.StartLine,
                    inlines: ProjectInlines(paragraph.InlineRuns)));
            }

            var nestedBlocks = item.Children
                .Where(static child => child is not MarkdownNativeParagraphBlock)
                .ToArray();
            childContent.AddRange(ProjectBlocks(nestedBlocks));
            var leadText = item.Paragraphs.Count > 0 ? item.Paragraphs[0].Text : item.Text;
            var leadRuns = item.Paragraphs.Count > 0 ? item.Paragraphs[0].InlineRuns : item.InlineRuns;
            items.Add(new NativeTranscriptListItem(
                leadText,
                ProjectInlines(leadRuns),
                item.IsTask,
                item.Checked,
                childContent));
        }

        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.List,
            string.Empty,
            sourceLine: list.SourceSpan?.StartLine,
            list: new NativeTranscriptList(list.IsOrdered, list.Start ?? 1, items)));
    }

    private static void AddQuote(ICollection<NativeTranscriptContent> result, MarkdownNativeQuoteBlock quote) =>
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Quote,
            string.Empty,
            sourceLine: quote.SourceSpan?.StartLine,
            container: new NativeTranscriptContainer("quote", null, null, ProjectBlocks(quote.Children))));

    private static void AddCallout(ICollection<NativeTranscriptContent> result, MarkdownNativeCalloutBlock callout) =>
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Callout,
            string.Empty,
            sourceLine: callout.SourceSpan?.StartLine,
            container: new NativeTranscriptContainer(
                callout.CalloutKind,
                callout.Title,
                FormatCalloutBadge(callout.CalloutKind),
                ProjectBlocks(callout.Children))));

    private static void AddCustomContainer(ICollection<NativeTranscriptContent> result, MarkdownNativeCustomContainerBlock container) =>
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Callout,
            string.Empty,
            sourceLine: container.SourceSpan?.StartLine,
            container: new NativeTranscriptContainer(
                container.Name,
                string.IsNullOrWhiteSpace(container.Info) ? container.Name : container.Info,
                FormatCalloutBadge(container.Name),
                ProjectBlocks(container.Children))));

    private static void AddDetails(ICollection<NativeTranscriptContent> result, MarkdownNativeDetailsBlock details) =>
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Details,
            string.Empty,
            sourceLine: details.SourceSpan?.StartLine,
            container: new NativeTranscriptContainer(
                "details",
                string.IsNullOrWhiteSpace(details.Summary) ? "Details" : details.Summary,
                "Details",
                ProjectBlocks(details.Children),
                isExpanded: details.Open)));

    private static void AddImage(ICollection<NativeTranscriptContent> result, MarkdownNativeImageBlock image) {
        var label = string.IsNullOrWhiteSpace(image.PlainAlt)
            ? (string.IsNullOrWhiteSpace(image.Title) ? "Image" : image.Title)
            : image.PlainAlt;
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Image,
            label,
            caption: image.Caption,
            sourceLine: image.SourceSpan?.StartLine,
            image: new NativeTranscriptImage(
                image.Source,
                label,
                image.Title,
                image.LinkUrl,
                image.Width,
                image.Height)));
    }

    private static void AddCode(ICollection<NativeTranscriptContent> result, MarkdownNativeCodeBlock code) {
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Code,
            code.Content,
            language: code.Language,
            caption: code.Caption,
            sourceLine: code.SourceSpan?.StartLine));
    }

    private static void AddDivider(ICollection<NativeTranscriptContent> result, MarkdownNativeThematicBreakBlock divider) =>
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Divider,
            string.Empty,
            sourceLine: divider.SourceSpan?.StartLine));

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
                    artifact),
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

    private static void AddFallback(ICollection<NativeTranscriptContent> result, MarkdownNativeBlock block) {
        var markdown = block.SourceBlock.RenderMarkdown();
        if (string.IsNullOrWhiteSpace(markdown)) return;
        result.Add(new NativeTranscriptContent(
            NativeTranscriptContentKind.Paragraph,
            markdown,
            sourceLine: block.SourceSpan?.StartLine));
    }

    private static IReadOnlyList<NativeTranscriptInline> ProjectInlines(IReadOnlyList<MarkdownNativeInline> inlines) {
        if (inlines.Count == 0) return Array.Empty<NativeTranscriptInline>();
        var result = new NativeTranscriptInline[inlines.Count];
        for (var i = 0; i < inlines.Count; i++) {
            var inline = inlines[i];
            var target = inline.GetMetadata("target")
                ?? inline.GetMetadata("source")
                ?? inline.GetMetadata("htmlTarget");
            result[i] = new NativeTranscriptInline(
                inline.Kind,
                inline.Text,
                target,
                ProjectInlines(inline.Children));
        }
        return result;
    }

    private static string FormatCalloutBadge(string? kind) {
        var normalized = string.IsNullOrWhiteSpace(kind) ? "Note" : kind.Trim().Replace('_', ' ').Replace('-', ' ');
        return normalized.Length == 1
            ? normalized.ToUpperInvariant()
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
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

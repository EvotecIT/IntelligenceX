using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using FontWeight = Windows.UI.Text.FontWeight;
using MarkdownNativeInlineKind = OfficeIMO.Markdown.MarkdownNativeInlineKind;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Converts the OfficeIMO-backed inline projection into selectable WinUI rich text.
/// </summary>
internal static class NativeTranscriptRichTextBuilder {
    public static RichTextBlock Create(
        IReadOnlyList<NativeTranscriptInline> inlines,
        string fallbackText,
        double fontSize = 14,
        FontWeight? fontWeight = null,
        Brush? foreground = null) {
        var block = new RichTextBlock {
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = fontSize,
            LineHeight = Math.Max(20, fontSize * 1.5),
            Foreground = foreground ?? NativeControlBrushes.TextPrimary
        };
        if (fontWeight.HasValue) block.FontWeight = fontWeight.Value;

        var paragraph = new Paragraph();
        if (inlines.Count == 0) {
            paragraph.Inlines.Add(new Run { Text = fallbackText ?? string.Empty });
        } else {
            AppendInlines(paragraph.Inlines, inlines);
        }
        block.Blocks.Add(paragraph);
        return block;
    }

    private static void AppendInlines(InlineCollection target, IReadOnlyList<NativeTranscriptInline> inlines) {
        foreach (var inline in inlines) {
            target.Add(CreateInline(inline));
        }
    }

    private static Inline CreateInline(NativeTranscriptInline inline) {
        switch (inline.Kind) {
            case MarkdownNativeInlineKind.HardBreak:
            case MarkdownNativeInlineKind.SoftBreak:
                return new LineBreak();
            case MarkdownNativeInlineKind.Strong:
                return CreateContainer(new Bold(), inline);
            case MarkdownNativeInlineKind.Emphasis:
                return CreateContainer(new Italic(), inline);
            case MarkdownNativeInlineKind.StrongEmphasis:
                var bold = new Bold();
                bold.Inlines.Add(CreateContainer(new Italic(), inline));
                return bold;
            case MarkdownNativeInlineKind.Underline:
            case MarkdownNativeInlineKind.Inserted:
                return CreateContainer(new Underline(), inline);
            case MarkdownNativeInlineKind.Code:
                return CreateStyledSpan(inline, new FontFamily("Consolas"), NativeControlBrushes.Accent, FontWeights.SemiBold);
            case MarkdownNativeInlineKind.Highlight:
                return CreateStyledSpan(inline, null, NativeControlBrushes.WarningText, FontWeights.SemiBold);
            case MarkdownNativeInlineKind.Strikethrough:
                return CreateStrikethrough(inline);
            case MarkdownNativeInlineKind.Superscript:
            case MarkdownNativeInlineKind.Subscript:
            case MarkdownNativeInlineKind.FootnoteRef:
                return CreateTypographyVariant(inline);
            case MarkdownNativeInlineKind.Link:
            case MarkdownNativeInlineKind.ImageLink:
                return CreateLink(inline);
            case MarkdownNativeInlineKind.Image:
                return new Run { Text = string.IsNullOrWhiteSpace(inline.Text) ? "Image" : inline.Text };
            default:
                return CreateTextOrChildren(inline);
        }
    }

    private static Span CreateContainer(Span span, NativeTranscriptInline inline) {
        if (inline.Children.Count > 0) {
            AppendInlines(span.Inlines, inline.Children);
        } else {
            span.Inlines.Add(new Run { Text = inline.Text });
        }
        return span;
    }

    private static Span CreateStyledSpan(
        NativeTranscriptInline inline,
        FontFamily? fontFamily,
        Brush foreground,
        FontWeight fontWeight,
        double? fontSize = null) {
        var span = new Span {
            Foreground = foreground,
            FontWeight = fontWeight
        };
        if (fontFamily != null) span.FontFamily = fontFamily;
        if (fontSize.HasValue) span.FontSize = fontSize.Value;
        return CreateContainer(span, inline);
    }

    private static Span CreateStrikethrough(NativeTranscriptInline inline) {
        var span = new Span {
            Foreground = NativeControlBrushes.TextMuted,
            TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough
        };
        return CreateContainer(span, inline);
    }

    private static Span CreateTypographyVariant(NativeTranscriptInline inline) {
        var variant = ResolveFontVariant(inline.Kind) ?? FontVariants.Normal;
        var span = new Span();
        if (inline.Kind == MarkdownNativeInlineKind.FootnoteRef) {
            span.Foreground = NativeControlBrushes.Accent;
            span.FontWeight = FontWeights.SemiBold;
        }
        Typography.SetVariants(span, variant);
        return CreateContainer(span, inline);
    }

    internal static FontVariants? ResolveFontVariant(MarkdownNativeInlineKind kind) => kind switch {
        MarkdownNativeInlineKind.Superscript => FontVariants.Superscript,
        MarkdownNativeInlineKind.Subscript => FontVariants.Subscript,
        MarkdownNativeInlineKind.FootnoteRef => FontVariants.Superscript,
        _ => null
    };

    private static Inline CreateLink(NativeTranscriptInline inline) {
        if (!TryCreateSafeUri(inline.Target, out var uri)) {
            return CreateTextOrChildren(inline);
        }

        var hyperlink = new Hyperlink { NavigateUri = uri };
        if (inline.Children.Count > 0) {
            AppendInlines(hyperlink.Inlines, inline.Children);
        } else {
            hyperlink.Inlines.Add(new Run { Text = inline.Text });
        }
        return hyperlink;
    }

    private static Inline CreateTextOrChildren(NativeTranscriptInline inline) {
        if (inline.Children.Count == 0) return new Run { Text = inline.Text };
        return CreateContainer(new Span(), inline);
    }

    private static bool TryCreateSafeUri(string? value, out Uri uri) {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate)) return false;
        if (!candidate.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !candidate.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        uri = candidate;
        return true;
    }
}

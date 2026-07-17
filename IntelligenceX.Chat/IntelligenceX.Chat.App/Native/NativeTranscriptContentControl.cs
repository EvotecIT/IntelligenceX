using System;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Renders one semantic block from the shared OfficeIMO native Markdown projection.
/// </summary>
internal sealed class NativeTranscriptContentControl : UserControl {
    public NativeTranscriptContentControl(NativeTranscriptContent content) {
        ArgumentNullException.ThrowIfNull(content);
        Content = Build(content);
    }

    private static FrameworkElement Build(NativeTranscriptContent content) => content.Kind switch {
        NativeTranscriptContentKind.Heading => BuildHeading(content),
        NativeTranscriptContentKind.Paragraph => NativeTranscriptRichTextBuilder.Create(content.Inlines, content.Text),
        NativeTranscriptContentKind.List => BuildList(content.List),
        NativeTranscriptContentKind.Quote => BuildQuote(content.Container),
        NativeTranscriptContentKind.Callout => BuildCallout(content.Container),
        NativeTranscriptContentKind.Details => BuildDetails(content.Container),
        NativeTranscriptContentKind.Code => BuildCode(content),
        NativeTranscriptContentKind.Divider => BuildDivider(),
        NativeTranscriptContentKind.Table => content.Table == null
            ? BuildDiagnostic("Table artifact unavailable.")
            : new NativeTranscriptTablePreviewControl(content.Table, content.Caption),
        NativeTranscriptContentKind.Visual => new NativeVisualArtifactHostControl(content.Visual, content.Caption),
        NativeTranscriptContentKind.Diagnostic => BuildDiagnostic(content.Text),
        _ => NativeTranscriptRichTextBuilder.Create(content.Inlines, content.Text)
    };

    private static FrameworkElement BuildHeading(NativeTranscriptContent content) {
        var level = Math.Clamp(content.HeadingLevel ?? 3, 1, 6);
        var size = level switch {
            1 => 22d,
            2 => 19d,
            3 => 17d,
            _ => 15d
        };
        return NativeTranscriptRichTextBuilder.Create(
            content.Inlines,
            content.Text,
            size,
            level <= 2 ? FontWeights.Bold : FontWeights.SemiBold,
            NativeControlBrushes.TextPrimary);
    }

    private static FrameworkElement BuildList(NativeTranscriptList? list) {
        if (list == null || list.Items.Count == 0) {
            return NativeTranscriptRichTextBuilder.Create(Array.Empty<NativeTranscriptInline>(), string.Empty);
        }

        var panel = new StackPanel { Spacing = 7 };
        for (var index = 0; index < list.Items.Count; index++) {
            var item = list.Items[index];
            var row = new Grid { ColumnSpacing = 9 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marker = item.IsTask
                ? (item.IsChecked ? "☑" : "☐")
                : list.IsOrdered
                    ? (list.Start + index).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                    : "•";
            row.Children.Add(new TextBlock {
                Text = marker,
                MinWidth = 18,
                FontSize = 14,
                FontWeight = item.IsTask ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = item.IsTask && item.IsChecked ? NativeControlBrushes.Success : NativeControlBrushes.TextSecondary,
                TextAlignment = TextAlignment.Right
            });

            var itemPanel = new StackPanel { Spacing = 6 };
            itemPanel.Children.Add(NativeTranscriptRichTextBuilder.Create(item.Inlines, item.Text));
            foreach (var child in item.Children) {
                itemPanel.Children.Add(new NativeTranscriptContentControl(child) {
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            Grid.SetColumn(itemPanel, 1);
            row.Children.Add(itemPanel);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static FrameworkElement BuildQuote(NativeTranscriptContainer? container) {
        var body = BuildChildren(container?.Children);
        return new Border {
            Padding = new Thickness(14, 10, 12, 10),
            BorderThickness = new Thickness(4, 0, 0, 0),
            BorderBrush = NativeControlBrushes.Quote,
            Background = NativeControlBrushes.QuoteSoft,
            CornerRadius = new CornerRadius(5),
            Child = body
        };
    }

    private static FrameworkElement BuildCallout(NativeTranscriptContainer? container) {
        var kind = container?.Kind ?? "note";
        var palette = ResolveCalloutPalette(kind);
        var title = container?.Title;
        if (string.IsNullOrWhiteSpace(title)) title = FormatKind(kind);

        var stack = new StackPanel { Spacing = 10 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new FontIcon {
            Glyph = palette.Glyph,
            FontSize = 14,
            Foreground = palette.Foreground,
            VerticalAlignment = VerticalAlignment.Center
        });
        var titleText = new TextBlock {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = palette.Foreground
        };
        Grid.SetColumn(titleText, 1);
        header.Children.Add(titleText);
        if (!string.IsNullOrWhiteSpace(container?.Badge)) {
            var badge = new Border {
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(10),
                Background = palette.Badge,
                Child = new TextBlock {
                    Text = container.Badge,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = palette.Foreground
                }
            };
            Grid.SetColumn(badge, 2);
            header.Children.Add(badge);
        }
        stack.Children.Add(header);
        var children = BuildChildren(container?.Children);
        if (children.Children.Count > 0) stack.Children.Add(children);

        return new Border {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = palette.Border,
            Background = palette.Background,
            Child = stack
        };
    }

    private static FrameworkElement BuildDetails(NativeTranscriptContainer? container) {
        var title = string.IsNullOrWhiteSpace(container?.Title) ? "Details" : container.Title;
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock {
            Text = title,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        });
        if (!string.IsNullOrWhiteSpace(container?.Badge)) {
            var badge = new TextBlock {
                Text = container.Badge,
                FontSize = 11,
                Foreground = NativeControlBrushes.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badge, 1);
            header.Children.Add(badge);
        }

        return new Expander {
            Header = header,
            Content = BuildChildren(container?.Children),
            IsExpanded = container?.IsExpanded ?? false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = NativeControlBrushes.NeutralSoft,
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 8, 12, 10)
        };
    }

    private static FrameworkElement BuildCode(NativeTranscriptContent content) {
        var stack = new StackPanel { Spacing = 0 };
        var label = string.IsNullOrWhiteSpace(content.Caption) ? content.Language : content.Caption;
        if (!string.IsNullOrWhiteSpace(label)) {
            stack.Children.Add(new Border {
                Padding = new Thickness(11, 7, 11, 7),
                Background = NativeControlBrushes.CodeHeader,
                Child = new TextBlock {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = NativeControlBrushes.CodeText
                }
            });
        }
        stack.Children.Add(new ScrollViewer {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = new TextBlock {
                Text = content.Text,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 13,
                LineHeight = 20,
                Padding = new Thickness(12, 10, 12, 10),
                Foreground = NativeControlBrushes.CodeText
            }
        });
        return new Border {
            CornerRadius = new CornerRadius(7),
            BorderBrush = NativeControlBrushes.CodeBorder,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.CodeBackground,
            Child = stack
        };
    }

    private static FrameworkElement BuildDivider() => new Border {
        Height = 1,
        Margin = new Thickness(0, 6, 0, 6),
        Background = NativeControlBrushes.BorderStrong
    };

    private static FrameworkElement BuildDiagnostic(string text) => BuildCallout(
        new NativeTranscriptContainer(
            "warning",
            "Rendering notice",
            "Warning",
            new[] { new NativeTranscriptContent(NativeTranscriptContentKind.Paragraph, text) }));

    private static StackPanel BuildChildren(System.Collections.Generic.IReadOnlyList<NativeTranscriptContent>? children) {
        var panel = new StackPanel { Spacing = 8 };
        if (children == null) return panel;
        foreach (var child in children) panel.Children.Add(new NativeTranscriptContentControl(child));
        return panel;
    }

    private static string FormatKind(string kind) {
        var normalized = kind.Replace('_', ' ').Replace('-', ' ').Trim();
        return normalized.Length == 0 ? "Note" : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private static CalloutPalette ResolveCalloutPalette(string kind) {
        var normalized = kind.Trim().Replace('-', '_').ToLowerInvariant();
        if (normalized is "warning" or "warn" or "limit" or "caution") {
            return new CalloutPalette("\uE7BA", NativeControlBrushes.WarningSoft, NativeControlBrushes.WarningBorder, NativeControlBrushes.WarningText, NativeControlBrushes.WarningBadge);
        }
        if (normalized is "error" or "danger" or "failure") {
            return new CalloutPalette("\uEA39", NativeControlBrushes.ErrorSoft, NativeControlBrushes.ErrorBorder, NativeControlBrushes.ErrorText, NativeControlBrushes.ErrorBadge);
        }
        if (normalized is "success" or "tip" or "done") {
            return new CalloutPalette("\uE73E", NativeControlBrushes.SuccessSoft, NativeControlBrushes.SuccessBorder, NativeControlBrushes.Success, NativeControlBrushes.SuccessBadge);
        }
        if (normalized is "canceled" or "execution_blocked" or "details") {
            return new CalloutPalette("\uE711", NativeControlBrushes.NeutralSoft, NativeControlBrushes.BorderStrong, NativeControlBrushes.TextSecondary, NativeControlBrushes.NeutralBadge);
        }
        return new CalloutPalette("\uE946", NativeControlBrushes.InfoSoft, NativeControlBrushes.InfoBorder, NativeControlBrushes.InfoText, NativeControlBrushes.InfoBadge);
    }

    private sealed record CalloutPalette(
        string Glyph,
        Brush Background,
        Brush Border,
        Brush Foreground,
        Brush Badge);
}

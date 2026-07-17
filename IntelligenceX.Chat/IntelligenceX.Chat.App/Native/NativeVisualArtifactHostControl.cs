using System;
using System.Collections.Generic;
using ChartForgeX.VisualArtifacts;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native host surface for projected ChartForgeX visual artifacts.
/// </summary>
internal sealed class NativeVisualArtifactHostControl : UserControl {
    private readonly NativeTranscriptVisual? _visual;
    private readonly string _title;

    public NativeVisualArtifactHostControl(NativeTranscriptVisual? visual, string? caption = null) {
        _visual = visual;
        var artifact = visual?.Artifact;
        _title = !string.IsNullOrWhiteSpace(caption)
            ? caption.Trim()
            : artifact == null ? FormatVisualTitle(visual) : FormatArtifactTitle(artifact, visual);
        Content = Build();
    }

    private FrameworkElement Build() {
        var artifact = _visual?.Artifact;
        var hasPreview = _visual?.Preview is { } preview && (preview.Svg != null || preview.HasPng);
        var detail = artifact == null
            ? "ChartForgeX preview is not available for this artifact."
            : FormatArtifactDetail(artifact, hasPreview);
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(BuildHeader(hasPreview));
        stack.Children.Add(new TextBlock {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        });
        if (_visual?.Preview is { } visualPreview && (visualPreview.Svg != null || visualPreview.HasPng)) {
            stack.Children.Add(CreatePreviewImage(visualPreview, artifact));
            stack.Children.Add(new TextBlock {
                Text = "Open the interactive view to resize, fit, zoom, and pan.",
                FontSize = 11,
                Foreground = NativeControlBrushes.TextMuted
            });
        }

        return new Border {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Surface,
            Child = stack
        };
    }

    private FrameworkElement BuildHeader(bool hasPreview) {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock {
            Text = _title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        });
        if (hasPreview && _visual != null) {
            var visual = _visual;
            var open = new Button {
                Content = "Open interactive",
                MinWidth = 124,
                MinHeight = 32,
                Background = NativeControlBrushes.AccentSoft,
                BorderBrush = NativeControlBrushes.UserBorder,
                Foreground = NativeControlBrushes.Accent
            };
            open.Click += (_, _) => NativeArtifactWindow.Show(
                _title,
                () => new NativeVisualWorkspaceControl(visual),
                width: 1280,
                height: 820);
            Grid.SetColumn(open, 1);
            grid.Children.Add(open);
        }

        return grid;
    }

    private static FrameworkElement CreatePreviewImage(NativeVisualPreview preview, VisualArtifact? artifact) {
        var image = new Image {
            Stretch = Stretch.Uniform,
            MaxHeight = 380,
            MinHeight = 180,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var natural = artifact?.NaturalSize;
        _ = NativeVisualImageLoader.LoadAsync(
            image,
            preview,
            rasterWidth: (natural?.Width ?? 1200) * 1.5,
            rasterHeight: (natural?.Height ?? 700) * 1.5);
        return new Border {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(6),
            BorderBrush = NativeControlBrushes.Border,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.SurfaceMuted,
            Child = image
        };
    }

    private static string FormatVisualTitle(NativeTranscriptVisual? visual) =>
        visual == null ? "Visual artifact" : visual.Kind + ": " + visual.FenceName;

    private static string FormatArtifactTitle(VisualArtifact artifact, NativeTranscriptVisual? visual) {
        if (!string.IsNullOrWhiteSpace(artifact.Title)) return artifact.Title;
        if (!string.IsNullOrWhiteSpace(artifact.Id)) return artifact.Id;
        return FormatVisualTitle(visual);
    }

    private static string FormatArtifactDetail(VisualArtifact artifact, bool hasPreview) {
        var parts = new List<string> { artifact.Kind.ToString(), artifact.SourceLanguage.ToString() };
        if (artifact.ExportFormats != VisualArtifactExportFormat.None) parts.Add(artifact.ExportFormats.ToString());
        if (artifact.Metadata.Count > 0) {
            parts.Add(artifact.Metadata.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " metadata");
        }
        if (hasPreview) parts.Add("interactive preview");
        return string.Join(" · ", parts);
    }
}

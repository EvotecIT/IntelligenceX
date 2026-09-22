using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    private NativeVisualPreview? _preview;
    private string? _previewError;
    private Task? _previewLoadTask;
    private bool _isLoaded;
    private bool _isExpanded = true;

    public NativeVisualArtifactHostControl(NativeTranscriptVisual? visual, string? caption = null) {
        _visual = visual;
        var artifact = visual?.Artifact;
        _title = !string.IsNullOrWhiteSpace(caption)
            ? caption.Trim()
            : artifact == null ? FormatVisualTitle(visual) : FormatArtifactTitle(artifact, visual);
        _preview = visual?.Preview;
        Content = Build();
        Loaded += OnLoaded;
        Unloaded += (_, _) => _isLoaded = false;
    }

    private FrameworkElement Build() {
        var artifact = _visual?.Artifact;
        var hasPreview = _preview is { } preview && (preview.Svg != null || preview.HasPng);
        var detail = artifact == null
            ? "ChartForgeX preview is not available for this artifact."
            : FormatArtifactDetail(artifact, hasPreview);
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(BuildHeader(hasPreview));
        if (!_isExpanded) {
            return BuildCard(stack);
        }

        stack.Children.Add(new TextBlock {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        });
        if (_preview is { } visualPreview && (visualPreview.Svg != null || visualPreview.HasPng)) {
            stack.Children.Add(CreatePreviewImage(visualPreview, artifact));
            stack.Children.Add(new TextBlock {
                Text = "Open the interactive view to resize, fit, zoom, and pan.",
                FontSize = 11,
                Foreground = NativeControlBrushes.TextMuted
            });
        } else if (!string.IsNullOrWhiteSpace(_previewError)) {
            stack.Children.Add(new TextBlock {
                Text = "Visual preview unavailable: " + _previewError,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = NativeControlBrushes.WarningText
            });
        } else if (artifact is not null) {
            stack.Children.Add(new ProgressRing {
                IsActive = true,
                Width = 24,
                Height = 24,
                HorizontalAlignment = HorizontalAlignment.Left
            });
        }

        return BuildCard(stack);
    }

    private static Border BuildCard(FrameworkElement child) =>
        new() {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(8),
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Surface,
            Child = child
        };

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
        var actions = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        var toggle = new Button {
            Content = _isExpanded ? "Collapse" : "Show preview",
            MinHeight = 32
        };
        ToolTipService.SetToolTip(toggle, _isExpanded
            ? "Collapse this visual card while keeping it in the transcript."
            : "Restore this visual preview.");
        toggle.Click += (_, _) => {
            _isExpanded = !_isExpanded;
            Content = Build();
        };
        actions.Children.Add(toggle);
        if (hasPreview && _visual != null && _preview != null) {
            var visual = _visual.WithPreview(_preview);
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
            actions.Children.Add(open);
        }
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        return grid;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) {
        _isLoaded = true;
        if (_preview is not null || _previewError is not null) {
            Content = Build();
            return;
        }

        if (_visual?.Artifact is not null && _previewLoadTask is null) {
            _previewLoadTask = RenderPreviewAsync(_visual.Artifact);
        }
    }

    private async Task RenderPreviewAsync(VisualArtifact artifact) {
        try {
            var result = await Task.Run(() => {
                var preview = NativeVisualPreviewRenderer.TryRender(artifact, out var error);
                return (Preview: preview, Error: error);
            });
            _preview = result.Preview;
            _previewError = result.Error;
        } catch (Exception ex) {
            _previewError = ex.Message;
        }

        if (_isLoaded) {
            Content = Build();
        }
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

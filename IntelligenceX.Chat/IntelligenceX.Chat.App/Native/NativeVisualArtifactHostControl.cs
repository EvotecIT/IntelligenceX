using System;
using System.Collections.Generic;
using ChartForgeX.VisualArtifacts;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Native host surface for projected ChartForgeX visual artifacts.
/// </summary>
internal sealed class NativeVisualArtifactHostControl : UserControl {
    public NativeVisualArtifactHostControl(NativeTranscriptVisual? visual, string? caption = null) {
        Content = Build(visual, caption);
    }

    private static FrameworkElement Build(NativeTranscriptVisual? visual, string? caption) {
        var artifact = visual?.Artifact;
        var title = !string.IsNullOrWhiteSpace(caption)
            ? caption.Trim()
            : artifact == null
                ? FormatVisualTitle(visual)
                : FormatArtifactTitle(artifact, visual);
        var hasPreview = visual?.Preview?.HasPng == true;
        var detail = artifact == null
            ? "ChartForgeX preview is not available for this artifact."
            : FormatArtifactDetail(artifact, hasPreview);
        var stack = new StackPanel {
            Spacing = 8
        };
        stack.Children.Add(BuildHeader(title, visual));
        stack.Children.Add(new TextBlock {
            Text = detail,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = NativeControlBrushes.TextSecondary
        });

        if (visual?.Preview?.Png is { Length: > 0 } png) {
            stack.Children.Add(CreatePreviewImage(png));
        }

        return new Border {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(7),
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            Background = NativeControlBrushes.Surface,
            Child = stack
        };
    }

    private static FrameworkElement BuildHeader(string title, NativeTranscriptVisual? visual) {
        var grid = new Grid {
            ColumnSpacing = 8
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock {
            Text = title,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary
        };
        Grid.SetColumn(titleText, 0);
        grid.Children.Add(titleText);

        if (visual?.Preview?.Png is { Length: > 0 } png) {
            var open = new Button {
                Content = "Open",
                MinWidth = 72,
                MinHeight = 32
            };
            open.Click += async (_, _) => await ShowPreviewAsync(open, title, png).ConfigureAwait(true);
            Grid.SetColumn(open, 1);
            grid.Children.Add(open);
        }

        return grid;
    }

    private static string FormatVisualTitle(NativeTranscriptVisual? visual) {
        if (visual == null) {
            return "Visual artifact";
        }

        return visual.Kind + ": " + visual.FenceName;
    }

    private static string FormatArtifactTitle(VisualArtifact artifact, NativeTranscriptVisual? visual) {
        if (!string.IsNullOrWhiteSpace(artifact.Title)) {
            return artifact.Title;
        }

        if (!string.IsNullOrWhiteSpace(artifact.Id)) {
            return artifact.Id;
        }

        return FormatVisualTitle(visual);
    }

    private static string FormatArtifactDetail(VisualArtifact artifact, bool hasPreview) {
        var parts = new List<string>();

        parts.Add(artifact.Kind.ToString());
        parts.Add(artifact.SourceLanguage.ToString());
        if (artifact.ExportFormats != VisualArtifactExportFormat.None) {
            parts.Add(artifact.ExportFormats.ToString());
        }

        if (artifact.Metadata.Count > 0) {
            parts.Add(artifact.Metadata.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " metadata");
        }

        if (hasPreview) {
            parts.Add("static preview");
        }

        return parts.Count == 0 ? "Artifact ready" : string.Join(" | ", parts);
    }

    private static FrameworkElement CreatePreviewImage(byte[] png) {
        var image = new Image {
            Stretch = Stretch.Uniform,
            MaxHeight = 420,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _ = LoadPreviewAsync(image, png);
        return image;
    }

    private static async Task ShowPreviewAsync(FrameworkElement owner, string title, byte[] png) {
        var image = new Image {
            Stretch = Stretch.Uniform,
            MaxWidth = 900,
            MaxHeight = 640,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        await LoadPreviewAsync(image, png).ConfigureAwait(true);
        var dialog = new ContentDialog {
            XamlRoot = owner.XamlRoot,
            Title = title,
            Content = image,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        _ = await dialog.ShowAsync();
    }

    private static async Task LoadPreviewAsync(Image image, byte[] png) {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        image.Source = bitmap;
    }
}

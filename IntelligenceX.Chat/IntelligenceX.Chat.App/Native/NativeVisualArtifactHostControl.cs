using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    public NativeVisualArtifactHostControl(NativeTranscriptVisual? visual) {
        Content = Build(visual);
    }

    private static FrameworkElement Build(NativeTranscriptVisual? visual) {
        var artifact = visual?.Artifact;
        var title = artifact == null
            ? FormatVisualTitle(visual)
            : FormatArtifactTitle(artifact, visual);
        var hasPreview = visual?.Preview?.HasPng == true;
        var detail = artifact == null
            ? "Renderer pending"
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
            Background = NativeControlBrushes.SurfaceMuted,
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

    private static string FormatArtifactTitle(object artifact, NativeTranscriptVisual? visual) {
        var title = GetPropertyString(artifact, "Title");
        if (!string.IsNullOrWhiteSpace(title)) {
            return title!;
        }

        var id = GetPropertyString(artifact, "Id");
        if (!string.IsNullOrWhiteSpace(id)) {
            return id!;
        }

        return FormatVisualTitle(visual);
    }

    private static string FormatArtifactDetail(object artifact, bool hasPreview) {
        var kind = GetPropertyString(artifact, "Kind");
        var source = GetPropertyString(artifact, "SourceLanguage");
        var exports = GetPropertyString(artifact, "ExportFormats");
        var metadataCount = GetMetadataCount(artifact);
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(kind)) {
            parts.Add(kind!);
        }

        if (!string.IsNullOrWhiteSpace(source)) {
            parts.Add(source!);
        }

        if (!string.IsNullOrWhiteSpace(exports)) {
            parts.Add(exports!);
        }

        if (metadataCount > 0) {
            parts.Add(metadataCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " metadata");
        }

        if (hasPreview) {
            parts.Add("static preview");
        }

        return parts.Count == 0 ? "Artifact ready" : string.Join(" | ", parts);
    }

    private static string? GetPropertyString(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        var value = property?.GetValue(instance);
        return value?.ToString();
    }

    private static int GetMetadataCount(object instance) {
        var property = instance.GetType().GetProperty("Metadata", BindingFlags.Instance | BindingFlags.Public);
        return property?.GetValue(instance) is ICollection collection ? collection.Count : 0;
    }

    private static FrameworkElement CreatePreviewImage(byte[] png) {
        var image = new Image {
            Stretch = Stretch.Uniform,
            MaxHeight = 300,
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

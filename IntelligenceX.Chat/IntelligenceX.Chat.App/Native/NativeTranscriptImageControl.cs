using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Native.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Displays Markdown images as native previews and opens their linked or source artifact on demand.
/// </summary>
internal sealed class NativeTranscriptImageControl : UserControl {
    private const double MaximumPreviewHeight = 560;
    private const double MaximumRequestedWidth = 1600;
    private const long MaximumRemoteImageBytes = 20L * 1024 * 1024;
    private const ulong MaximumSourcePixels = 100_000_000;
    private static readonly TimeSpan RemoteImageTimeout = TimeSpan.FromSeconds(20);
    private static readonly HttpClient RemoteImageHttpClient = new();
    private readonly NativeTranscriptImage _content;
    private readonly Microsoft.UI.Xaml.Controls.Image _preview;
    private readonly TextBlock _status;
    private readonly Button? _loadExternalButton;

    public NativeTranscriptImageControl(NativeTranscriptImage content, string? caption) {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _preview = new Microsoft.UI.Xaml.Controls.Image {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Uniform,
            MaxHeight = MaximumPreviewHeight,
            Visibility = Visibility.Collapsed
        };
        if (content.Width is > 0) {
            _preview.MaxWidth = Math.Min(content.Width.Value, MaximumRequestedWidth);
        }
        if (content.Height is > 0) {
            _preview.MaxHeight = Math.Min(content.Height.Value, MaximumPreviewHeight);
        }
        _preview.ImageFailed += (_, args) => ShowFailure("Image could not be loaded: " + args.ErrorMessage);

        _status = new TextBlock {
            Text = "Loading image...",
            TextWrapping = TextWrapping.Wrap,
            Foreground = NativeControlBrushes.TextMuted,
            Margin = new Thickness(2, 10, 2, 10)
        };

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(new TextBlock {
            Text = content.Title ?? content.AlternateText,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = NativeControlBrushes.TextPrimary,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(caption)) {
            labels.Children.Add(new TextBlock {
                Text = caption,
                Foreground = NativeControlBrushes.TextMuted,
                TextWrapping = TextWrapping.Wrap
            });
        }
        header.Children.Add(labels);

        var actions = new StackPanel {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        if (RequiresExplicitRemoteLoad(content.Source)) {
            _loadExternalButton = new Button {
                Content = "Load preview",
                MinHeight = 30
            };
            _loadExternalButton.Click += async (_, _) => {
                _loadExternalButton.IsEnabled = false;
                _status.Text = "Loading external image...";
                _status.Foreground = NativeControlBrushes.TextMuted;
                _status.Visibility = Visibility.Visible;
                await LoadAsync(allowExternalSource: true).ConfigureAwait(true);
            };
            actions.Children.Add(_loadExternalButton);
        }

        var openButton = new Button {
            Content = "Open image",
            MinHeight = 30,
            IsEnabled = CanOpen(content.LinkUrl ?? content.Source)
        };
        openButton.Click += async (_, _) => await OpenAsync().ConfigureAwait(true);
        actions.Children.Add(openButton);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(header);
        panel.Children.Add(_preview);
        panel.Children.Add(_status);
        Content = new Border {
            Background = NativeControlBrushes.Surface,
            BorderBrush = NativeControlBrushes.BorderStrong,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(12),
            Child = panel
        };
        Loaded += async (_, _) => {
            if (RequiresExplicitRemoteLoad(_content.Source)) {
                ShowExternalLoadRequired();
                return;
            }

            await LoadAsync(allowExternalSource: false).ConfigureAwait(true);
        };
    }

    private async Task LoadAsync(bool allowExternalSource) {
        try {
            if (RequiresExplicitRemoteLoad(_content.Source) && !allowExternalSource) {
                ShowExternalLoadRequired();
                return;
            }

            if (TryCreateRemoteUri(_content.Source, out var remoteUri)) {
                using var downloaded = await DownloadRemoteImageAsync(remoteUri);
                using var downloadedRandomAccess = downloaded.AsRandomAccessStream();
                _preview.Source = await DecodeBoundedAsync(downloadedRandomAccess);
                ShowPreview();
                return;
            }

            if (TryCreateWebOrAppUri(_content.Source, out var appUri)) {
                var appFile = await StorageFile.GetFileFromApplicationUriAsync(appUri);
                using var appStream = await appFile.OpenReadAsync();
                _preview.Source = await DecodeBoundedAsync(appStream);
                ShowPreview();
                return;
            }

            var path = ResolveLocalPath(_content.Source);
            if (path is null) {
                ShowFailure("Image source is not a supported URL or local file: " + _content.Source);
                return;
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            _preview.Source = await DecodeBoundedAsync(stream);
            ShowPreview();
        } catch (Exception ex) {
            ShowFailure("Image could not be loaded: " + ex.Message);
        }
    }

    private async Task OpenAsync() {
        var target = _content.LinkUrl ?? _content.Source;
        try {
            if (TryCreateWebOrAppUri(target, out var uri)) {
                _ = await Launcher.LaunchUriAsync(uri);
                return;
            }

            var path = ResolveLocalPath(target);
            if (path is not null) {
                _ = await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
            }
        } catch (Exception ex) {
            ShowFailure("Image could not be opened: " + ex.Message);
        }
    }

    private void ShowPreview() {
        _preview.Visibility = Visibility.Visible;
        _status.Visibility = Visibility.Collapsed;
        if (_loadExternalButton is not null) {
            _loadExternalButton.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowExternalLoadRequired() {
        _preview.Visibility = Visibility.Collapsed;
        _status.Text = "External image preview is blocked for privacy. Choose Load preview to fetch it.";
        _status.Foreground = NativeControlBrushes.TextMuted;
        _status.Visibility = Visibility.Visible;
    }

    private void ShowFailure(string message) {
        _preview.Visibility = Visibility.Collapsed;
        if (_loadExternalButton is not null) {
            _loadExternalButton.IsEnabled = true;
            _loadExternalButton.Visibility = Visibility.Visible;
        }
        _status.Text = message;
        _status.Foreground = NativeControlBrushes.ErrorText;
        _status.Visibility = Visibility.Visible;
    }

    private static bool CanOpen(string? value) =>
        TryCreateWebOrAppUri(value, out _) || ResolveLocalPath(value) is not null;

    internal static bool RequiresExplicitRemoteLoad(string? value) {
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate)) {
            return false;
        }

        return string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    internal static (int Width, int Height) CalculateDecodeDimensions(uint pixelWidth, uint pixelHeight) {
        if (pixelWidth == 0 || pixelHeight == 0) {
            throw new InvalidDataException("Image dimensions are missing.");
        }

        var sourcePixels = (ulong)pixelWidth * pixelHeight;
        if (sourcePixels > MaximumSourcePixels) {
            throw new InvalidDataException("Image dimensions exceed the supported preview limit.");
        }

        var scale = Math.Min(
            1d,
            Math.Min(MaximumRequestedWidth / pixelWidth, MaximumPreviewHeight / pixelHeight));
        return (
            Math.Max(1, (int)Math.Round(pixelWidth * scale, MidpointRounding.AwayFromZero)),
            Math.Max(1, (int)Math.Round(pixelHeight * scale, MidpointRounding.AwayFromZero)));
    }

    private static async Task<BitmapImage> DecodeBoundedAsync(IRandomAccessStream stream) {
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var dimensions = CalculateDecodeDimensions(decoder.PixelWidth, decoder.PixelHeight);
        stream.Seek(0);
        var bitmap = new BitmapImage {
            DecodePixelWidth = dimensions.Width,
            DecodePixelHeight = dimensions.Height
        };
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static async Task<MemoryStream> DownloadRemoteImageAsync(Uri uri) {
        using var timeout = new CancellationTokenSource(RemoteImageTimeout);
        using var response = await RemoteImageHttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumRemoteImageBytes) {
            throw new InvalidDataException("External image exceeds the 20 MB preview limit.");
        }

        var initialCapacity = response.Content.Headers.ContentLength is > 0
            ? (int)response.Content.Headers.ContentLength.Value
            : 0;
        var output = new MemoryStream(initialCapacity);
        try {
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new byte[81_920];
            long totalBytes = 0;
            while (true) {
                var bytesRead = await input.ReadAsync(buffer, timeout.Token);
                if (bytesRead == 0) {
                    break;
                }

                totalBytes += bytesRead;
                if (totalBytes > MaximumRemoteImageBytes) {
                    throw new InvalidDataException("External image exceeds the 20 MB preview limit.");
                }

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), timeout.Token);
            }

            output.Position = 0;
            return output;
        } catch {
            output.Dispose();
            throw;
        }
    }

    private static bool TryCreateRemoteUri(string? value, out Uri uri) {
        uri = null!;
        if (!RequiresExplicitRemoteLoad(value)
            || !Uri.TryCreate(value!.Trim(), UriKind.Absolute, out var candidate)) {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool TryCreateWebOrAppUri(string? value, out Uri uri) {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate)) {
            return false;
        }

        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate.Scheme, "ms-appdata", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static string? ResolveLocalPath(string? value) {
        var source = (value ?? string.Empty).Trim();
        if (source.Length == 0) return null;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)) {
            source = uri.LocalPath;
        }

        try {
            var path = Path.GetFullPath(source);
            return path.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(path) ? null : path;
        } catch (Exception) when (source.Length > 0) {
            return null;
        }
    }
}

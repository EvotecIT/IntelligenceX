using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Loads the best available ChartForgeX preview into a WinUI image.
/// </summary>
internal static class NativeVisualImageLoader {
    public static async Task LoadAsync(
        Image image,
        Native.Rendering.NativeVisualPreview preview,
        double rasterWidth = 0,
        double rasterHeight = 0) {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (preview == null) throw new ArgumentNullException(nameof(preview));
        // WinUI's SVG decoder currently drops ChartForgeX text nodes on some systems.
        // Prefer the shared renderer's faithful PNG output so labels remain visible.
        if (preview.Png is { Length: > 0 } png) {
            using var pngStream = await WriteAsync(png).ConfigureAwait(true);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(pngStream);
            image.Source = bitmap;
            return;
        }

        if (string.IsNullOrWhiteSpace(preview.Svg)) return;
        using var stream = await WriteAsync(Encoding.UTF8.GetBytes(preview.Svg!)).ConfigureAwait(true);
        var source = new SvgImageSource();
        if (rasterWidth > 0) source.RasterizePixelWidth = rasterWidth;
        if (rasterHeight > 0) source.RasterizePixelHeight = rasterHeight;
        await source.SetSourceAsync(stream);
        image.Source = source;
    }

    private static async Task<InMemoryRandomAccessStream> WriteAsync(byte[] bytes) {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream)) {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        return stream;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
#if NETFRAMEWORK
using System.Net;
#else
using System.Net.Http;
#endif
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;

namespace IntelligenceX.PowerShell;

internal static class TurnOutputSaver {
#if !NETFRAMEWORK
    private static readonly HttpClient HttpClient = new();
#endif

    public static IReadOnlyList<TurnOutput> ApplyFirst(IReadOnlyList<TurnOutput> outputs, int first) {
        if (first <= 0 || outputs.Count <= first) {
            return outputs;
        }
        var slice = new List<TurnOutput>(first);
        for (var i = 0; i < first; i++) {
            slice.Add(outputs[i]);
        }
        return slice;
    }

    public static async Task<IReadOnlyList<string>> SaveImagesAsync(IReadOnlyList<TurnOutput> outputs, string directory, string turnId,
        bool downloadUrls, bool overwrite, string? fileNamePrefix, string? model, Action<string>? writeWarning, Action<string>? writeVerbose) {
        var saved = new List<string>();
        Directory.CreateDirectory(directory);

        var prefix = string.IsNullOrWhiteSpace(fileNamePrefix) ? BuildDefaultPrefix(model) : fileNamePrefix!;
        var counter = 1;

        foreach (var output in outputs) {
            var ext = GetExtension(output.MimeType, output.ImagePath ?? output.ImageUrl);
            var fileName = $"{prefix}{turnId}-{counter:D2}{ext}";
            var targetPath = Path.Combine(directory, fileName);

            if (!overwrite && File.Exists(targetPath)) {
                writeVerbose?.Invoke($"Skipping existing file: {targetPath}");
                counter++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(output.ImagePath) && File.Exists(output.ImagePath)) {
                File.Copy(output.ImagePath, targetPath, overwrite);
                saved.Add(targetPath);
                counter++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(output.Base64)) {
                if (TryDecodeBase64(output.Base64!, out var bytes, out var mimeOverride)) {
                    if (!string.IsNullOrWhiteSpace(mimeOverride)) {
                        ext = GetExtension(mimeOverride, null);
                        targetPath = Path.Combine(directory, $"{prefix}{turnId}-{counter:D2}{ext}");
                    }
                    File.WriteAllBytes(targetPath, bytes);
                    saved.Add(targetPath);
                } else {
                    writeWarning?.Invoke("Failed to decode base64 image content.");
                }
                counter++;
                continue;
            }

            if (downloadUrls && !string.IsNullOrWhiteSpace(output.ImageUrl)) {
                var bytes = await DownloadBytesAsync(output.ImageUrl!).ConfigureAwait(false);
                File.WriteAllBytes(targetPath, bytes);
                saved.Add(targetPath);
                counter++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(output.ImageUrl)) {
                writeWarning?.Invoke($"Image URL present but not downloaded: {output.ImageUrl}");
            }

            counter++;
        }

        return saved;
    }

    private static string GetExtension(string? mimeType, string? pathOrUrl) {
        var ext = TryGetExtensionFromPath(pathOrUrl);
        if (ext is not null && ext.Length > 0) {
            return ext;
        }

        if (mimeType is null) {
            return ".bin";
        }

        var mime = mimeType.Trim();
        if (mime.Length == 0) {
            return ".bin";
        }
        switch (mime.ToLowerInvariant()) {
            case "image/png":
                return ".png";
            case "image/jpeg":
            case "image/jpg":
                return ".jpg";
            case "image/gif":
                return ".gif";
            case "image/webp":
                return ".webp";
            case "image/bmp":
                return ".bmp";
            case "image/svg+xml":
                return ".svg";
            case "image/tiff":
                return ".tiff";
            case "image/heic":
                return ".heic";
            case "image/heif":
                return ".heif";
            default:
                return ".bin";
        }
    }

    private static string? TryGetExtensionFromPath(string? pathOrUrl) {
        if (pathOrUrl is null || pathOrUrl.Length == 0) {
            return null;
        }

        var ext = Path.GetExtension(pathOrUrl);
        if (!string.IsNullOrWhiteSpace(ext)) {
            return ext;
        }

        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri)) {
            ext = Path.GetExtension(uri.AbsolutePath);
            if (!string.IsNullOrWhiteSpace(ext)) {
                return ext;
            }
        }

        return null;
    }

    private static bool TryDecodeBase64(string input, out byte[] bytes, out string? mimeType) {
        mimeType = null;
        bytes = Array.Empty<byte>();

        var trimmed = input.Trim();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) {
            var commaIndex = trimmed.IndexOf(',');
            if (commaIndex <= 0 || commaIndex >= trimmed.Length - 1) {
                return false;
            }

            var header = trimmed.Substring(5, commaIndex - 5);
            var payload = trimmed.Substring(commaIndex + 1);
            var semicolonIndex = header.IndexOf(';');
            if (semicolonIndex >= 0) {
                mimeType = header.Substring(0, semicolonIndex);
            } else {
                mimeType = header;
            }

            try {
                bytes = Convert.FromBase64String(payload);
                return true;
            } catch {
                return false;
            }
        }

        try {
            bytes = Convert.FromBase64String(trimmed);
            return true;
        } catch {
            return false;
        }
    }

    private static string BuildDefaultPrefix(string? model) {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        if (model is null || IsWhiteSpace(model)) {
            return $"intelligencex-image-{timestamp}-";
        }

        var cleaned = SanitizeFileName(model);
        return $"intelligencex-image-{timestamp}-{cleaned}-";
    }

    private static string SanitizeFileName(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++) {
            if (Array.IndexOf(invalid, chars[i]) >= 0) {
                chars[i] = '_';
            }
        }
        return new string(chars);
    }

    private static bool IsWhiteSpace(string value) {
        for (var i = 0; i < value.Length; i++) {
            if (!char.IsWhiteSpace(value[i])) {
                return false;
            }
        }
        return true;
    }

    private static Task<byte[]> DownloadBytesAsync(string url) {
#if NETFRAMEWORK
        using (var client = new WebClient()) {
            return Task.FromResult(client.DownloadData(url));
        }
#else
        return HttpClient.GetByteArrayAsync(url);
#endif
    }
}

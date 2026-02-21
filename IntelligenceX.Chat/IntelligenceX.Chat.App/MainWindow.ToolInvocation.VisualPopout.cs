using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    internal static bool TryNormalizeVisualPopoutMimeType(string? mimeType, out string normalizedMimeType, out string normalizedFormat) {
        normalizedMimeType = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        normalizedFormat = string.Empty;

        if (normalizedMimeType == "image/png") {
            normalizedFormat = "png";
            return true;
        }
        if (normalizedMimeType == "image/svg+xml") {
            normalizedFormat = "svg";
            return true;
        }
        return false;
    }

    internal static string NormalizeVisualPopoutTitle(string? title) {
        var normalizedTitle = (title ?? string.Empty).Trim();
        if (normalizedTitle.Length > MaxVisualPopoutTitleChars) {
            normalizedTitle = normalizedTitle[..MaxVisualPopoutTitleChars].TrimEnd();
        }

        return normalizedTitle;
    }

    internal static bool TryPrepareVisualPopoutRequest(
        string? title,
        string? mimeType,
        string? dataBase64,
        out string normalizedTitle,
        out string normalizedFormat,
        out byte[] payloadBytes,
        out string errorMessage) {
        normalizedTitle = NormalizeVisualPopoutTitle(title);
        normalizedFormat = string.Empty;
        payloadBytes = Array.Empty<byte>();
        errorMessage = "Invalid popout request.";

        if (!TryNormalizeVisualPopoutMimeType(mimeType, out _, out normalizedFormat)) {
            errorMessage = "Unsupported popout mime type.";
            return false;
        }

        var payload = dataBase64 ?? string.Empty;
        if (payload.Length > MaxVisualPopoutBase64Chars) {
            errorMessage = "Popout payload exceeds maximum allowed size.";
            return false;
        }

        if (!TryDecodeVisualPopoutPayload(payload, out payloadBytes, out errorMessage)) {
            return false;
        }

        if (payloadBytes.Length > MaxVisualPopoutBytes) {
            errorMessage = "Popout payload exceeds maximum allowed size.";
            payloadBytes = Array.Empty<byte>();
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static string GetVisualPopoutDirectoryPath() {
        return Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "visual-popout");
    }

    internal static bool TryDecodeVisualPopoutPayload(string? dataBase64, out byte[] payloadBytes, out string errorMessage) {
        payloadBytes = Array.Empty<byte>();
        errorMessage = "Invalid popout payload.";
        var payload = dataBase64 ?? string.Empty;
        if (payload.Length == 0) {
            errorMessage = "Missing popout payload.";
            return false;
        }
        if (payload.Length > MaxVisualPopoutBase64Chars) {
            errorMessage = "Popout payload exceeds maximum allowed size.";
            return false;
        }

        try {
            payloadBytes = Convert.FromBase64String(payload);
        } catch (Exception ex) {
            errorMessage = "Invalid popout payload: " + ex.Message;
            return false;
        }

        if (payloadBytes.Length == 0) {
            errorMessage = "Popout payload is empty.";
            payloadBytes = Array.Empty<byte>();
            return false;
        }
        if (payloadBytes.Length > MaxVisualPopoutBytes) {
            errorMessage = "Popout payload exceeds maximum allowed size.";
            payloadBytes = Array.Empty<byte>();
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static void CleanupStaleVisualPopoutFiles(string directoryPath) {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath)) {
            return;
        }

        var cutoff = DateTime.UtcNow - VisualPopoutRetention;
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)) {
            try {
                var lastWrite = File.GetLastWriteTimeUtc(filePath);
                if (lastWrite <= cutoff) {
                    File.Delete(filePath);
                }
            } catch {
                // Ignore stale cleanup failures.
            }
        }
    }

    private async Task<VisualPopoutOpenResult> OpenVisualPopoutAsync(string title, string normalizedFormat, byte[] bytes) {
        try {
            var popoutDirectory = GetVisualPopoutDirectoryPath();
            Directory.CreateDirectory(popoutDirectory);
            CleanupStaleVisualPopoutFiles(popoutDirectory);

            var suggestedStem = BuildSuggestedExportFileName(title, normalizedFormat);
            var extension = GetExportFileExtension(normalizedFormat);
            var popoutPath = Path.Combine(popoutDirectory, suggestedStem + extension);
            if (File.Exists(popoutPath)) {
                popoutPath = Path.Combine(popoutDirectory, suggestedStem + "-" + Guid.NewGuid().ToString("N")[..8] + extension);
            }

            await File.WriteAllBytesAsync(popoutPath, bytes).ConfigureAwait(false);
            Process.Start(new ProcessStartInfo {
                FileName = popoutPath,
                UseShellExecute = true
            });

            var fileName = Path.GetFileName(popoutPath);
            return new VisualPopoutOpenResult(
                Ok: true,
                FilePath: popoutPath,
                Message: "Opened popout: " + fileName);
        } catch (Exception ex) {
            StartupLog.Write("OpenVisualPopoutAsync failed: " + ex);
            return new VisualPopoutOpenResult(
                Ok: false,
                FilePath: null,
                Message: "Popout failed. Please try again.");
        }
    }

    private async Task OpenVisualExportPathAsync(string path, bool reveal) {
        try {
            if (!File.Exists(path)) {
                await NotifyVisualExportActionResultAsync(ok: false, "Export file no longer exists.").ConfigureAwait(false);
                return;
            }

            if (reveal) {
                var args = "/select,\"" + path.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                Process.Start(new ProcessStartInfo {
                    FileName = "explorer.exe",
                    Arguments = args,
                    UseShellExecute = true
                });
                await NotifyVisualExportActionResultAsync(ok: true, "Opened export location.").ConfigureAwait(false);
                return;
            }

            Process.Start(new ProcessStartInfo {
                FileName = path,
                UseShellExecute = true
            });
            await NotifyVisualExportActionResultAsync(ok: true, "Opened exported file.").ConfigureAwait(false);
        } catch (Exception ex) {
            await NotifyVisualExportActionResultAsync(ok: false, "Export action failed: " + ex.Message).ConfigureAwait(false);
        }
    }

    private async Task HandleVisualExportActionAsync(string action, string path) {
        var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedPath = (path ?? string.Empty).Trim();
        if (normalizedAction.Length == 0) {
            await NotifyVisualExportActionResultAsync(ok: false, "Missing export action.").ConfigureAwait(false);
            return;
        }

        if (normalizedPath.Length == 0) {
            await NotifyVisualExportActionResultAsync(ok: false, "No export path available yet.").ConfigureAwait(false);
            return;
        }

        switch (normalizedAction) {
            case "copy_path":
                {
                    var dp = new DataPackage();
                    dp.SetText(normalizedPath);
                    Clipboard.SetContent(dp);
                    Clipboard.Flush();
                    await NotifyVisualExportActionResultAsync(ok: true, "Export path copied.").ConfigureAwait(false);
                    return;
                }
            case "open":
                await OpenVisualExportPathAsync(normalizedPath, reveal: false).ConfigureAwait(false);
                return;
            case "reveal":
                await OpenVisualExportPathAsync(normalizedPath, reveal: true).ConfigureAwait(false);
                return;
            default:
                await NotifyVisualExportActionResultAsync(ok: false, "Unknown export action: " + normalizedAction).ConfigureAwait(false);
                return;
        }
    }

    private static bool TryNormalizeVisualExportPath(string path, string normalizedFormat, out string normalizedPath, out string errorMessage) {
        normalizedPath = string.Empty;
        errorMessage = "Invalid export path.";
        var candidate = (path ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            errorMessage = "Export path is required.";
            return false;
        }

        string fullPath;
        try {
            fullPath = Path.GetFullPath(candidate);
        } catch {
            errorMessage = "Export path is invalid.";
            return false;
        }

        if (!Path.IsPathFullyQualified(fullPath)) {
            errorMessage = "Export path must be absolute.";
            return false;
        }

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName)) {
            errorMessage = "Export path must include a file name.";
            return false;
        }

        var expectedExtension = GetExportFileExtension(normalizedFormat);
        var currentExtension = Path.GetExtension(fullPath);
        if (string.IsNullOrWhiteSpace(currentExtension)) {
            fullPath += expectedExtension;
        } else if (!string.Equals(currentExtension, expectedExtension, StringComparison.OrdinalIgnoreCase)) {
            errorMessage = "Export path extension must be " + expectedExtension + ".";
            return false;
        }

        normalizedPath = fullPath;
        return true;
    }

    private static string BuildExportArgumentsJson(string format, string title, JsonElement rowsElement, string? outputPath = null) {
        var safeTitle = string.IsNullOrWhiteSpace(title) ? "dataset" : title.Trim();
        var payload = new Dictionary<string, object?> {
            ["format"] = format,
            ["title"] = safeTitle,
            ["rows"] = rowsElement
        };

        if (!string.IsNullOrWhiteSpace(outputPath)) {
            payload["output_path"] = outputPath.Trim();
        }

        return JsonSerializer.Serialize(payload);
    }

    private static string? TryExtractExportFilePath(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }

        try {
            using var doc = JsonDocument.Parse(output);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("file_path", out var filePathElement)) {
                return null;
            }

            return filePathElement.ValueKind == JsonValueKind.String ? filePathElement.GetString() : null;
        } catch {
            return null;
        }
    }
}

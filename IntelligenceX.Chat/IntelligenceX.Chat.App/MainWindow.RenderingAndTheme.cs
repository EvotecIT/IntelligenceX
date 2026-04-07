using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.App.Theming;
using IntelligenceX.Chat.Client;
using IntelligenceX.Chat.ExportArtifacts;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private async Task ExportTranscriptAsync() {
        try {
            var conversation = GetActiveConversation();
            var md = BuildTranscriptMarkdown(conversation.Messages, _timestampFormat);
            if (string.IsNullOrWhiteSpace(md)) {
                return;
            }

            var baseName = string.IsNullOrWhiteSpace(conversation.ThreadId) ? conversation.Id : conversation.ThreadId!;
            var preferredFormat = string.Equals(_exportDefaultFormat, ExportPreferencesContract.FormatDocx, StringComparison.OrdinalIgnoreCase)
                ? ExportPreferencesContract.FormatDocx
                : ExportPreferencesContract.FormatMarkdown;
            var pickedPath = await ShowTranscriptSavePickerAsync(baseName, preferredFormat).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pickedPath)) {
                return;
            }

            var normalizedPath = Path.GetFullPath(pickedPath);
            var normalizedExtension = Path.GetExtension(normalizedPath).Trim().ToLowerInvariant();
            var transcriptFormat = string.Equals(normalizedExtension, ".docx", StringComparison.OrdinalIgnoreCase)
                ? ExportPreferencesContract.FormatDocx
                : ExportPreferencesContract.FormatMarkdown;
            var filePath = LocalExportArtifactWriter.ResolveOutputPath(transcriptFormat, baseName, normalizedPath, defaultPrefix: "transcript");

            var result = await ExportTranscriptToPathAsync(baseName, md, transcriptFormat, filePath).ConfigureAwait(false);
            if (!result.Succeeded) {
                await SetStatusAsync(SessionStatus.ExportFailed()).ConfigureAwait(false);
                AppendSystem(BuildTranscriptExportFailureNoticeText(result));
                return;
            }

            await UpdateLastExportDirectoryFromFilePathAsync(result.OutputPath).ConfigureAwait(false);
            AppendSystem(BuildTranscriptExportSuccessNoticeText(result));
        } catch (Exception ex) {
            await SetStatusAsync(SessionStatus.ExportFailed()).ConfigureAwait(false);
            AppendSystem("Transcript export failed: " + ex.Message);
        }
    }

    private async Task<TranscriptExportResult> ExportTranscriptToPathAsync(
        string title,
        string markdown,
        string transcriptFormat,
        string filePath) {
        if (!string.Equals(transcriptFormat, ExportPreferencesContract.FormatDocx, StringComparison.OrdinalIgnoreCase)) {
            return LocalExportArtifactWriter.ExportTranscript(transcriptFormat, title, markdown, filePath);
        }

        using var runtimeMaterialization = await MaterializeTranscriptVisualsForDocxAsync(markdown, _exportDocxVisualMaxWidthPx).ConfigureAwait(false);
        if (runtimeMaterialization is null) {
            return LocalExportArtifactWriter.ExportTranscript(
                transcriptFormat,
                title,
                markdown,
                filePath,
                additionalAllowedImageDirectories: null,
                docxVisualMaxWidthPx: _exportDocxVisualMaxWidthPx);
        }

        var materializedResult = LocalExportArtifactWriter.ExportTranscript(
            transcriptFormat,
            title,
            runtimeMaterialization.Markdown,
            filePath,
            runtimeMaterialization.AllowedImageDirectories,
            _exportDocxVisualMaxWidthPx,
            allowMarkdownFallback: false);
        if (materializedResult.Succeeded) {
            return materializedResult;
        }

        var retryResult = LocalExportArtifactWriter.ExportTranscript(
            transcriptFormat,
            title,
            markdown,
            filePath,
            additionalAllowedImageDirectories: null,
            docxVisualMaxWidthPx: _exportDocxVisualMaxWidthPx,
            allowMarkdownFallback: true);
        return ResolveTranscriptExportResultAfterMaterializedDocxRetry(materializedResult, retryResult);
    }

    private void CopyTranscript() {
        var md = BuildTranscriptMarkdown(GetActiveConversation().Messages, _timestampFormat);
        if (string.IsNullOrWhiteSpace(md)) {
            return;
        }

        var dp = new DataPackage();
        dp.SetText(md);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
    }

    private static string BuildTranscriptMarkdown(IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages, string timestampFormat) {
        return TranscriptMarkdownFormatter.Format(messages, timestampFormat);
    }

    private static string BuildMessagesHtml(
        IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat,
        MarkdownRendererOptions markdownOptions,
        IReadOnlyDictionary<int, TranscriptMessageDecoration>? messageDecorations = null,
        bool showAssistantTurnTrace = true,
        bool showAssistantDraftBubbles = true) {
        return TranscriptHtmlFormatter.Format(
            messages,
            timestampFormat,
            markdownOptions,
            messageDecorations,
            showAssistantTurnTrace,
            showAssistantDraftBubbles);
    }

    private async Task<string?> ShowTranscriptSavePickerAsync(string? title, string preferredFormat) {
        string? selectedPath = null;
        await RunOnUiThreadAsync(async () => {
            var picker = new FileSavePicker {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = BuildSuggestedExportFileName(title, preferredFormat),
                DefaultFileExtension = string.Equals(preferredFormat, ExportPreferencesContract.FormatDocx, StringComparison.OrdinalIgnoreCase)
                    ? ".docx"
                    : ".md"
            };

            picker.FileTypeChoices.Add("Markdown Document", new List<string> { ".md" });
            picker.FileTypeChoices.Add("Word Document", new List<string> { ".docx" });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero) {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            selectedPath = file?.Path;
        }).ConfigureAwait(false);
        return selectedPath;
    }

    private static string BuildShellHtml() {
        return UiShellAssets.Load();
    }

    internal static TranscriptExportResult ResolveTranscriptExportResultAfterMaterializedDocxRetry(
        TranscriptExportResult materializedAttemptResult,
        TranscriptExportResult retryResult) {
        if (materializedAttemptResult.Succeeded || materializedAttemptResult.Failure is not { } materializedFailure) {
            return retryResult;
        }

        var promotedMaterializedFailure = new TranscriptExportFailure(
            TranscriptExportStage.DocxWriteWithMaterializedVisuals,
            materializedFailure.Message);
        var promotedRetryFailure = retryResult.Failure is { } retryFailure
            ? PromoteDocxRetryFailureStage(retryFailure)
            : (TranscriptExportFailure?)null;
        var promotedRetryFallback = retryResult.Fallback is { } retryFallback
            ? PromoteDocxRetryFallbackStage(retryFallback)
            : (TranscriptExportFallback?)null;
        if (retryResult.Succeeded &&
            string.Equals(retryResult.ActualFormat, ExportPreferencesContract.FormatDocx, StringComparison.OrdinalIgnoreCase)) {
            return TranscriptExportResult.SuccessWithFallback(
                ExportPreferencesContract.FormatDocx,
                ExportPreferencesContract.FormatDocx,
                retryResult.OutputPath,
                new TranscriptExportFallback(
                    TranscriptExportFallbackKind.DocxWithoutMaterializedVisuals,
                    retryResult.OutputPath,
                    promotedMaterializedFailure));
        }

        if (retryResult.Succeeded && promotedRetryFallback is { } successfulRetryFallback) {
            return TranscriptExportResult.SuccessWithFallback(
                ExportPreferencesContract.FormatDocx,
                retryResult.ActualFormat,
                retryResult.OutputPath,
                successfulRetryFallback);
        }

        return promotedRetryFallback is { } failedFallback
            ? TranscriptExportResult.Failed(
                ExportPreferencesContract.FormatDocx,
                retryResult.OutputPath,
                promotedRetryFailure ?? promotedMaterializedFailure,
                failedFallback)
            : TranscriptExportResult.Failed(
                ExportPreferencesContract.FormatDocx,
                retryResult.OutputPath,
                promotedRetryFailure ?? promotedMaterializedFailure);
    }

    internal static string BuildTranscriptExportSuccessNoticeText(TranscriptExportResult result) {
        if (!result.Succeeded) {
            return BuildTranscriptExportFailureNoticeText(result);
        }

        if (!result.UsedFallback || result.Fallback is not { } fallback) {
            return SystemNoticeFormatter.Format(SystemNotice.TranscriptExported(result.OutputPath));
        }

        return fallback.Kind switch {
            TranscriptExportFallbackKind.DocxWithoutMaterializedVisuals =>
                "Exported transcript: " + result.OutputPath + " (DOCX export retried without materialized visuals.)",
            TranscriptExportFallbackKind.Markdown =>
                "Exported transcript: " + result.OutputPath + " (DOCX export fell back to Markdown.)",
            _ => SystemNoticeFormatter.Format(SystemNotice.TranscriptExported(result.OutputPath))
        };
    }

    internal static string BuildTranscriptExportFailureNoticeText(TranscriptExportResult result) {
        var failure = result.Failure;
        var stage = DescribeTranscriptExportStage(failure?.Stage ?? TranscriptExportStage.None);
        var message = (failure?.Message ?? "Unknown error.").Trim();
        if (message.Length == 0) {
            message = "Unknown error.";
        }

        if (result.Fallback is { } fallback) {
            return "Transcript export failed during "
                   + stage
                   + ": "
                   + message
                   + " (attempted fallback path: "
                   + fallback.OutputPath
                   + ").";
        }

        return "Transcript export failed during " + stage + ": " + message;
    }

    private static string DescribeTranscriptExportStage(TranscriptExportStage stage) {
        return stage switch {
            TranscriptExportStage.MarkdownWrite => "markdown write",
            TranscriptExportStage.MarkdownFallbackWrite => "markdown fallback write",
            TranscriptExportStage.DocxWrite => "DOCX write",
            TranscriptExportStage.DocxWriteWithMaterializedVisuals => "DOCX write with materialized visuals",
            TranscriptExportStage.DocxWriteWithoutMaterializedVisuals => "DOCX retry without materialized visuals",
            _ => "export"
        };
    }

    private static TranscriptExportFailure PromoteDocxRetryFailureStage(TranscriptExportFailure failure) {
        var remappedStage = RemapDocxRetryStage(failure.Stage);
        return remappedStage != failure.Stage
            ? new TranscriptExportFailure(remappedStage, failure.Message)
            : failure;
    }

    private static TranscriptExportFallback PromoteDocxRetryFallbackStage(TranscriptExportFallback fallback) {
        var remappedCauseStage = RemapDocxRetryStage(fallback.Cause.Stage);
        return remappedCauseStage != fallback.Cause.Stage
            ? new TranscriptExportFallback(
                fallback.Kind,
                fallback.OutputPath,
                new TranscriptExportFailure(remappedCauseStage, fallback.Cause.Message))
            : fallback;
    }

    private static TranscriptExportStage RemapDocxRetryStage(TranscriptExportStage stage) {
        return stage == TranscriptExportStage.DocxWrite
            ? TranscriptExportStage.DocxWriteWithoutMaterializedVisuals
            : stage;
    }

    private static string EnsureAppIcon() {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath)) {
            return iconPath;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "app.ico");
        if (File.Exists(tempPath)) {
            return tempPath;
        }

        try {
            var bytes = BuildBrandedIco();
            try {
                File.WriteAllBytes(iconPath, bytes);
                return iconPath;
            } catch {
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
                File.WriteAllBytes(tempPath, bytes);
                return tempPath;
            }
        } catch {
            return string.Empty;
        }
    }

    private static byte[] BuildBrandedIco() {
        var img16 = BuildIconBitmap(16, 3);
        var img32 = BuildIconBitmap(32, 6);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ICONDIR
        bw.Write((short)0);  // Reserved
        bw.Write((short)1);  // Type: ICO
        bw.Write((short)2);  // Image count

        // Directory entries
        int offset = 6 + 16 * 2;
        WriteIconDirEntry(bw, 16, img16.Length, offset);
        offset += img16.Length;
        WriteIconDirEntry(bw, 32, img32.Length, offset);

        // Image data
        bw.Write(img16);
        bw.Write(img32);

        return ms.ToArray();
    }

    private static void WriteIconDirEntry(BinaryWriter bw, int size, int dataSize, int offset) {
        bw.Write((byte)size);   // Width
        bw.Write((byte)size);   // Height
        bw.Write((byte)0);      // Colors
        bw.Write((byte)0);      // Reserved
        bw.Write((short)1);     // Planes
        bw.Write((short)32);    // BPP
        bw.Write(dataSize);
        bw.Write(offset);
    }

    private static byte[] BuildIconBitmap(int size, int radius) {
        int andRowBytes = ((size + 31) / 32) * 4;
        int andSize = andRowBytes * size;
        int xorSize = size * size * 4;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // BITMAPINFOHEADER
        bw.Write(40);            // biSize
        bw.Write(size);          // biWidth
        bw.Write(size * 2);      // biHeight (doubled for ICO XOR+AND)
        bw.Write((short)1);      // biPlanes
        bw.Write((short)32);     // biBitCount
        bw.Write(0);             // biCompression
        bw.Write(xorSize + andSize);
        bw.Write(0);             // biXPelsPerMeter
        bw.Write(0);             // biYPelsPerMeter
        bw.Write(0);             // biClrUsed
        bw.Write(0);             // biClrImportant

        // XOR bitmap — bottom-up scanlines, BGRA
        // Brand color #4CC3FF
        const byte colR = 76, colG = 195, colB = 255;
        for (int row = size - 1; row >= 0; row--) {
            for (int col = 0; col < size; col++) {
                byte alpha = IsInsideRoundedRect(col, row, size, radius) ? (byte)255 : (byte)0;
                bw.Write(colB);
                bw.Write(colG);
                bw.Write(colR);
                bw.Write(alpha);
            }
        }

        // AND mask (all zero — alpha channel handles transparency)
        bw.Write(new byte[andSize]);

        return ms.ToArray();
    }

    private static bool IsInsideRoundedRect(int x, int y, int size, int radius) {
        if (x >= radius && x < size - radius) {
            return true;
        }
        if (y >= radius && y < size - radius) {
            return true;
        }
        int cx = x < radius ? radius : size - radius - 1;
        int cy = y < radius ? radius : size - radius - 1;
        int dx = x - cx, dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }

    private async Task SetThemeAsync(string presetName) {
        if (!_webViewReady) {
            return;
        }

        var normalized = NormalizeTheme(presetName) ?? "default";
        if (string.Equals(normalized, "default", StringComparison.OrdinalIgnoreCase)) {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixResetTheme && window.ixResetTheme('default');").AsTask()).ConfigureAwait(false);
            return;
        }

        if (!ThemeRegistry.TryGetVariables(normalized, out var vars) || vars.Count == 0) {
            return;
        }

        var payloadJson = JsonSerializer.Serialize(new {
            name = normalized,
            vars
        });
        await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixSetTheme(" + payloadJson + ");").AsTask()).ConfigureAwait(false);
    }

    private async Task<RuntimeVisualExportMaterialization?> MaterializeTranscriptVisualsForDocxAsync(string markdown, int docxVisualMaxWidthPx) {
        if (!_webViewReady || string.IsNullOrWhiteSpace(markdown)) {
            return null;
        }

        var payloadJson = JsonSerializer.Serialize(new {
            markdown,
            themeMode = _exportVisualThemeMode,
            docxVisualMaxWidthPx = ExportPreferencesContract.NormalizeDocxVisualMaxWidthPx(docxVisualMaxWidthPx)
        });
        var script = "(async () => {" +
                     "if (!window.ixMaterializeVisualFencesForDocx) { return null; }" +
                     "try { return await window.ixMaterializeVisualFencesForDocx(" + payloadJson + "); }" +
                     "catch (_) { return null; }" +
                     "})()";

        string? rawResult = null;
        await RunOnUiThreadAsync(async () => {
            rawResult = await _webView.ExecuteScriptAsync(script).AsTask().ConfigureAwait(false);
        }).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawResult)) {
            return null;
        }

        JsonElement root;
        try {
            using var resultDoc = JsonDocument.Parse(rawResult);
            if (resultDoc.RootElement.ValueKind != JsonValueKind.Object) {
                return null;
            }

            root = resultDoc.RootElement.Clone();
        } catch {
            return null;
        }

        if (!root.TryGetProperty("markdown", out var markdownElement) || markdownElement.ValueKind != JsonValueKind.String) {
            return null;
        }

        var materializedMarkdown = markdownElement.GetString() ?? string.Empty;
        if (materializedMarkdown.Length == 0) {
            return null;
        }

        if (!root.TryGetProperty("images", out var imagesElement) || imagesElement.ValueKind != JsonValueKind.Array) {
            return null;
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "docx-runtime-visuals", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var imageCount = 0;
        try {
            foreach (var image in imagesElement.EnumerateArray()) {
                if (image.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                var id = TryReadVisualExportString(image, "id", maxLength: 64);
                var mimeType = TryReadVisualExportString(image, "mimeType", maxLength: 64);
                var dataBase64 = TryReadVisualExportString(image, "dataBase64", maxLength: 16 * 1024 * 1024);
                if (id.Length == 0 || mimeType.Length == 0 || dataBase64.Length == 0) {
                    continue;
                }

                var bytes = TryDecodeBase64(dataBase64);
                if (bytes is null || bytes.Length == 0) {
                    continue;
                }

                var extension = ResolveVisualImageExtension(mimeType);
                var fileName = "visual-" + id + extension;
                var imagePath = Path.Combine(tempDirectory, fileName);
                File.WriteAllBytes(imagePath, bytes);

                var markdownImagePath = imagePath.Replace('\\', '/');
                materializedMarkdown = materializedMarkdown.Replace(
                    "ix-export-image://" + id,
                    markdownImagePath,
                    StringComparison.Ordinal);
                imageCount++;
            }
        } catch {
            TryDeleteRuntimeVisualDirectory(tempDirectory);
            return null;
        }

        if (imageCount == 0) {
            TryDeleteRuntimeVisualDirectory(tempDirectory);
            return null;
        }

        return new RuntimeVisualExportMaterialization(materializedMarkdown, tempDirectory);
    }

    private static string TryReadVisualExportString(JsonElement root, string propertyName, int maxLength) {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }

        var text = (value.GetString() ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > maxLength) {
            return string.Empty;
        }

        return text;
    }

    private static byte[]? TryDecodeBase64(string payload) {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > 16 * 1024 * 1024) {
            return null;
        }

        try {
            return Convert.FromBase64String(payload);
        } catch {
            return null;
        }
    }

    private static string ResolveVisualImageExtension(string mimeType) {
        var normalized = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            "image/svg+xml" => ".svg",
            "image/jpeg" => ".jpg",
            "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };
    }

    private static void TryDeleteRuntimeVisualDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup.
        }
    }

    private sealed class RuntimeVisualExportMaterialization : IDisposable {
        private readonly string? _directory;

        internal RuntimeVisualExportMaterialization(string markdown, string directory) {
            Markdown = markdown ?? string.Empty;
            _directory = string.IsNullOrWhiteSpace(directory) ? null : directory;
        }

        public string Markdown { get; }
        public IReadOnlyList<string> AllowedImageDirectories =>
            string.IsNullOrWhiteSpace(_directory) ? Array.Empty<string>() : [_directory!];

        public void Dispose() {
            if (string.IsNullOrWhiteSpace(_directory)) {
                return;
            }

            TryDeleteRuntimeVisualDirectory(_directory);
        }
    }
}

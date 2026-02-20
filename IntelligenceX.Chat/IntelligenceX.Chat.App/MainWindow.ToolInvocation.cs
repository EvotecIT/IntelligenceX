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
    private const int MaxVisualExportBytes = 12 * 1024 * 1024;
    private const int MaxVisualExportBase64Chars = ((MaxVisualExportBytes + 2) / 3) * 4;
    private static readonly TimeSpan VisualPopoutRetention = TimeSpan.FromHours(12);

    private async Task ExportTableArtifactAsync(string format, string title, JsonElement rowsElement, string exportId = "", string? outputPath = null) {
        if (!ExportPreferencesContract.TryNormalizeFormat(format, out var normalizedFormat)) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.InvalidFormat, normalizedFormat)).ConfigureAwait(false);
            return;
        }

        if (!LocalExportArtifactWriter.TryReadRows(rowsElement, out var rows)) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.NoRows, normalizedFormat)).ConfigureAwait(false);
            return;
        }

        string? remoteFailure = null;
        var toolKnown = _toolDescriptions.ContainsKey("export_table_artifact");
        var toolEnabled = !_toolStates.TryGetValue("export_table_artifact", out var enabled) || enabled;
        var client = _client;
        var canInvokeRemote = _isConnected && client is not null && toolKnown && toolEnabled;

        if (canInvokeRemote) {
            var argumentsJson = BuildExportArgumentsJson(normalizedFormat, title, rowsElement, outputPath);
            var request = new InvokeToolRequest {
                RequestId = NextId(),
                ToolName = "export_table_artifact",
                ArgumentsJson = argumentsJson
            };

            await SetStatusAsync(SessionStatus.Exporting()).ConfigureAwait(false);
            try {
                var response = await client!.RequestAsync<InvokeToolResultMessage>(request, CancellationToken.None).ConfigureAwait(false);
                var output = response.Output;
                if (output.Ok == true) {
                    var filePath = TryExtractExportFilePath(output.Output);
                    var completedNotice = ExportNotice.Succeeded(normalizedFormat, filePath);
                    if (!string.IsNullOrWhiteSpace(filePath)) {
                        await UpdateLastExportDirectoryFromFilePathAsync(filePath).ConfigureAwait(false);
                    }
                    await ReportExportNoticeAsync(exportId, completedNotice).ConfigureAwait(false);
                    return;
                }

                remoteFailure = (output.Error ?? string.Empty).Trim();
            } catch (Exception ex) {
                remoteFailure = ex.Message;
            }
        }

        await SetStatusAsync(SessionStatus.Exporting()).ConfigureAwait(false);
        try {
            var resolvedPath = LocalExportArtifactWriter.ResolveOutputPath(normalizedFormat, title, outputPath);
            LocalExportArtifactWriter.ExportTable(normalizedFormat, title, rows, resolvedPath);
            await UpdateLastExportDirectoryFromFilePathAsync(resolvedPath).ConfigureAwait(false);
            await ReportExportNoticeAsync(exportId, ExportNotice.Succeeded(normalizedFormat, resolvedPath)).ConfigureAwait(false);
        } catch (Exception ex) {
            var detail = (remoteFailure ?? string.Empty).Trim();
            if (detail.Length > 0) {
                detail = "Runtime export failed (" + detail + "); local export failed: " + ex.Message;
            } else {
                detail = ex.Message;
            }
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.Exception, normalizedFormat, detail)).ConfigureAwait(false);
        }
    }

    private async Task ReportExportNoticeAsync(string exportId, ExportNotice notice) {
        await SetStatusAsync(ExportNoticeFormatter.Status(notice)).ConfigureAwait(false);
        AppendSystem(ExportNoticeFormatter.SystemText(notice));
        await NotifyDataViewExportResultAsync(
            exportId,
            notice.Format,
            ok: notice.Ok,
            filePath: notice.FilePath,
            message: ExportNoticeFormatter.DataViewText(notice)).ConfigureAwait(false);
    }

    private async Task NotifyDataViewExportResultAsync(string exportId, string format, bool ok, string? filePath, string message) {
        if (string.IsNullOrWhiteSpace(exportId) || !_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            exportId,
            format,
            ok,
            filePath,
            message
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnDataViewExportResult && window.ixOnDataViewExportResult(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures; system transcript already reports export status.
        }
    }

    private async Task PickDataViewExportPathAsync(string requestId, string format, string title) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        if (!ExportPreferencesContract.TryNormalizeFormat(format, out var normalizedFormat)) {
            normalizedFormat = string.Empty;
        }
        if (normalizedFormat is not ("csv" or "xlsx" or "docx")) {
            await NotifyExportPathSelectedAsync(
                requestId,
                ok: false,
                path: null,
                message: "Unsupported export format.",
                canceled: false).ConfigureAwait(false);
            return;
        }

        try {
            var path = await ShowExportSavePickerAsync(normalizedFormat, title).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path)) {
                await NotifyExportPathSelectedAsync(
                    requestId,
                    ok: false,
                    path: null,
                    message: "Export canceled.",
                    canceled: true).ConfigureAwait(false);
                return;
            }

            await NotifyExportPathSelectedAsync(
                requestId,
                ok: true,
                path: path,
                message: "Save location selected.",
                canceled: false).ConfigureAwait(false);
        } catch (Exception ex) {
            await NotifyExportPathSelectedAsync(
                requestId,
                ok: false,
                path: null,
                message: "Failed to open save picker: " + ex.Message,
                canceled: false).ConfigureAwait(false);
        }
    }

    private static bool TryNormalizeVisualExportFormat(string? format, out string normalizedFormat) {
        normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedFormat is "png" or "svg") {
            return true;
        }

        normalizedFormat = string.Empty;
        return false;
    }

    private async Task PickVisualExportPathAsync(string requestId, string format, string title) {
        if (string.IsNullOrWhiteSpace(requestId)) {
            return;
        }

        if (!TryNormalizeVisualExportFormat(format, out var normalizedFormat)) {
            await NotifyVisualExportPathSelectedAsync(
                requestId,
                ok: false,
                path: null,
                message: "Unsupported visual export format.",
                canceled: false).ConfigureAwait(false);
            return;
        }

        try {
            var path = await ShowExportSavePickerAsync(normalizedFormat, title).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(path)) {
                await NotifyVisualExportPathSelectedAsync(
                    requestId,
                    ok: false,
                    path: null,
                    message: "Export canceled.",
                    canceled: true).ConfigureAwait(false);
                return;
            }

            await NotifyVisualExportPathSelectedAsync(
                requestId,
                ok: true,
                path: path,
                message: "Save location selected.",
                canceled: false).ConfigureAwait(false);
        } catch (Exception ex) {
            await NotifyVisualExportPathSelectedAsync(
                requestId,
                ok: false,
                path: null,
                message: "Failed to open save picker: " + ex.Message,
                canceled: false).ConfigureAwait(false);
        }
    }

    private async Task<string?> ShowExportSavePickerAsync(string normalizedFormat, string title) {
        string? selectedPath = null;
        await RunOnUiThreadAsync(async () => {
            var picker = new FileSavePicker();
            var extension = GetExportFileExtension(normalizedFormat);
            var displayLabel = normalizedFormat switch {
                "xlsx" => "Excel Workbook",
                "docx" => "Word Document",
                "csv" => "CSV File",
                "png" => "PNG Image",
                "svg" => "SVG Image",
                _ => "Export File"
            };

            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.FileTypeChoices.Add(displayLabel, new List<string> { extension });
            picker.DefaultFileExtension = extension;
            picker.SuggestedFileName = BuildSuggestedExportFileName(title, normalizedFormat);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd != IntPtr.Zero) {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            selectedPath = file?.Path;
        }).ConfigureAwait(false);
        return selectedPath;
    }

    private static string BuildSuggestedExportFileName(string? title, string normalizedFormat) {
        var safeTitle = (title ?? "dataset").Trim();
        if (safeTitle.Length == 0) {
            safeTitle = "dataset";
        }

        foreach (var ch in Path.GetInvalidFileNameChars()) {
            safeTitle = safeTitle.Replace(ch, '_');
        }

        if (safeTitle.Length > 80) {
            safeTitle = safeTitle[..80].TrimEnd();
        }

        return safeTitle + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    private static string GetExportFileExtension(string normalizedFormat) {
        return normalizedFormat switch {
            "xlsx" => ".xlsx",
            "docx" => ".docx",
            "png" => ".png",
            "svg" => ".svg",
            ExportPreferencesContract.FormatMarkdown => ".md",
            _ => ".csv"
        };
    }

    private async Task NotifyExportPathSelectedAsync(string requestId, bool ok, string? path, string message, bool canceled) {
        if (!_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            requestId,
            ok,
            path,
            canceled,
            message = (message ?? string.Empty).Trim()
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnExportPathSelected && window.ixOnExportPathSelected(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures; client stays responsive.
        }
    }

    private async Task HandleDataViewExportActionAsync(string action, string path) {
        var normalizedAction = (action ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedPath = (path ?? string.Empty).Trim();
        if (normalizedAction.Length == 0) {
            await NotifyDataViewActionResultAsync(ok: false, "Missing export action.").ConfigureAwait(false);
            return;
        }

        if (normalizedPath.Length == 0) {
            await NotifyDataViewActionResultAsync(ok: false, "No export path available yet.").ConfigureAwait(false);
            return;
        }

        switch (normalizedAction) {
            case "copy_path":
                {
                    var dp = new DataPackage();
                    dp.SetText(normalizedPath);
                    Clipboard.SetContent(dp);
                    Clipboard.Flush();
                    await NotifyDataViewActionResultAsync(ok: true, "Export path copied.").ConfigureAwait(false);
                    return;
                }
            case "open":
                await OpenExportPathAsync(normalizedPath, reveal: false).ConfigureAwait(false);
                return;
            case "reveal":
                await OpenExportPathAsync(normalizedPath, reveal: true).ConfigureAwait(false);
                return;
            default:
                await NotifyDataViewActionResultAsync(ok: false, "Unknown export action: " + normalizedAction).ConfigureAwait(false);
                return;
        }
    }

    private async Task OpenExportPathAsync(string path, bool reveal) {
        try {
            if (!File.Exists(path)) {
                await NotifyDataViewActionResultAsync(ok: false, "Export file no longer exists.").ConfigureAwait(false);
                return;
            }

            if (reveal) {
                var args = "/select,\"" + path.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                Process.Start(new ProcessStartInfo {
                    FileName = "explorer.exe",
                    Arguments = args,
                    UseShellExecute = true
                });
                await NotifyDataViewActionResultAsync(ok: true, "Opened export location.").ConfigureAwait(false);
                return;
            }

            Process.Start(new ProcessStartInfo {
                FileName = path,
                UseShellExecute = true
            });
            await NotifyDataViewActionResultAsync(ok: true, "Opened exported file.").ConfigureAwait(false);
        } catch (Exception ex) {
            await NotifyDataViewActionResultAsync(ok: false, "Export action failed: " + ex.Message).ConfigureAwait(false);
        }
    }

    private async Task NotifyDataViewActionResultAsync(bool ok, string message) {
        if (!_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            ok,
            message = (message ?? string.Empty).Trim()
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnDataViewActionResult && window.ixOnDataViewActionResult(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures for action feedback.
        }
    }

    private async Task NotifyVisualExportPathSelectedAsync(string requestId, bool ok, string? path, string message, bool canceled) {
        if (!_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            requestId,
            ok,
            path,
            canceled,
            message = (message ?? string.Empty).Trim()
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnVisualExportPathSelected && window.ixOnVisualExportPathSelected(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures; client stays responsive.
        }
    }

    private async Task NotifyVisualExportResultAsync(string exportId, string format, bool ok, string? filePath, string message) {
        if (string.IsNullOrWhiteSpace(exportId) || !_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            exportId,
            format,
            ok,
            filePath,
            message
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnVisualExportResult && window.ixOnVisualExportResult(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures; system transcript already reports export status.
        }
    }

    private async Task ExportVisualArtifactAsync(string format, string title, string dataBase64, string mimeType, string exportId = "", string? outputPath = null) {
        if (!TryNormalizeVisualExportFormat(format, out var normalizedFormat)) {
            await NotifyVisualExportResultAsync(exportId, format, ok: false, filePath: null, message: "Unsupported visual export format.").ConfigureAwait(false);
            return;
        }

        var normalizedMimeType = (mimeType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedFormat == "svg" && normalizedMimeType.Length > 0 && normalizedMimeType != "image/svg+xml") {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "SVG export payload mime type is invalid.").ConfigureAwait(false);
            return;
        }
        if (normalizedFormat == "png" && normalizedMimeType.Length > 0 && normalizedMimeType != "image/png") {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "PNG export payload mime type is invalid.").ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(dataBase64)) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Missing visual export data.").ConfigureAwait(false);
            return;
        }

        var payload = dataBase64;
        if (payload.Length > MaxVisualExportBase64Chars) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Export payload exceeds maximum allowed size.").ConfigureAwait(false);
            return;
        }

        byte[] bytes;
        try {
            bytes = Convert.FromBase64String(payload);
        } catch (Exception ex) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Invalid export payload: " + ex.Message).ConfigureAwait(false);
            return;
        }

        if (bytes.Length == 0) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Export payload is empty.").ConfigureAwait(false);
            return;
        }
        if (bytes.Length > MaxVisualExportBytes) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Export payload exceeds maximum allowed size.").ConfigureAwait(false);
            return;
        }

        var resolvedPath = (outputPath ?? string.Empty).Trim();
        if (resolvedPath.Length > 0 && !TryNormalizeVisualExportPath(resolvedPath, normalizedFormat, out resolvedPath, out var pathValidationError)) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: pathValidationError).ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(resolvedPath)) {
            resolvedPath = (await ShowExportSavePickerAsync(normalizedFormat, title).ConfigureAwait(false) ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedPath)) {
                await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Export canceled.").ConfigureAwait(false);
                return;
            }

            if (!TryNormalizeVisualExportPath(resolvedPath, normalizedFormat, out resolvedPath, out pathValidationError)) {
                await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: pathValidationError).ConfigureAwait(false);
                return;
            }
        }

        try {
            var dir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }

            await File.WriteAllBytesAsync(resolvedPath, bytes).ConfigureAwait(false);
            await UpdateLastExportDirectoryFromFilePathAsync(resolvedPath).ConfigureAwait(false);
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: true, filePath: resolvedPath, message: string.Empty).ConfigureAwait(false);
        } catch (Exception ex) {
            await NotifyVisualExportResultAsync(exportId, normalizedFormat, ok: false, filePath: null, message: "Visual export failed: " + ex.Message).ConfigureAwait(false);
        }
    }

    private async Task NotifyVisualExportActionResultAsync(bool ok, string message) {
        if (!_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            ok,
            message = (message ?? string.Empty).Trim()
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnVisualExportActionResult && window.ixOnVisualExportActionResult(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures for action feedback.
        }
    }

    private async Task NotifyVisualPopoutResultAsync(bool ok, string? filePath, string message) {
        if (!_webViewReady) {
            return;
        }

        var payload = JsonSerializer.Serialize(new {
            ok,
            filePath,
            message = (message ?? string.Empty).Trim()
        });

        try {
            await RunOnUiThreadAsync(() => _webView.ExecuteScriptAsync("window.ixOnVisualPopoutResult && window.ixOnVisualPopoutResult(" + payload + ");").AsTask()).ConfigureAwait(false);
        } catch {
            // Ignore UI callback failures for action feedback.
        }
    }

    private static bool TryNormalizeVisualPopoutMimeType(string? mimeType, out string normalizedMimeType, out string normalizedFormat) {
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

    private static string GetVisualPopoutDirectoryPath() {
        return Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "visual-popout");
    }

    private static bool TryDecodeVisualPopoutPayload(string? dataBase64, out byte[] payloadBytes, out string errorMessage) {
        payloadBytes = Array.Empty<byte>();
        errorMessage = "Invalid popout payload.";
        var payload = dataBase64 ?? string.Empty;
        if (payload.Length == 0) {
            errorMessage = "Missing popout payload.";
            return false;
        }
        if (payload.Length > MaxVisualExportBase64Chars) {
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
        if (payloadBytes.Length > MaxVisualExportBytes) {
            errorMessage = "Popout payload exceeds maximum allowed size.";
            payloadBytes = Array.Empty<byte>();
            return false;
        }

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

    private async Task OpenVisualPopoutAsync(string title, string normalizedFormat, byte[] bytes) {
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
            await NotifyVisualPopoutResultAsync(ok: true, filePath: popoutPath, message: "Opened popout: " + fileName).ConfigureAwait(false);
        } catch (Exception ex) {
            await NotifyVisualPopoutResultAsync(ok: false, filePath: null, message: "Popout failed: " + ex.Message).ConfigureAwait(false);
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

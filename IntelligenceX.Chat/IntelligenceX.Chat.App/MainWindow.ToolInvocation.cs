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
    private const int MaxVisualPopoutBytes = 12 * 1024 * 1024;
    private const int MaxVisualPopoutBase64Chars = ((MaxVisualPopoutBytes + 2) / 3) * 4;
    private const int MaxVisualPopoutTitleChars = 160;
    private static readonly TimeSpan VisualPopoutRetention = TimeSpan.FromHours(12);
    internal readonly record struct VisualPopoutOpenResult(bool Ok, string? FilePath, string Message);

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

}

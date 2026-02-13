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
    private async Task ExportTableArtifactAsync(string format, string title, JsonElement rowsElement, string exportId = "", string? outputPath = null) {
        if (!ExportPreferencesContract.TryNormalizeFormat(format, out var normalizedFormat)) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.InvalidFormat, normalizedFormat)).ConfigureAwait(false);
            return;
        }

        if (rowsElement.ValueKind != JsonValueKind.Array || rowsElement.GetArrayLength() == 0) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.NoRows, normalizedFormat)).ConfigureAwait(false);
            return;
        }

        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.Disconnected, normalizedFormat)).ConfigureAwait(false);
            return;
        }

        var client = _client;
        if (client is null) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.Disconnected, normalizedFormat)).ConfigureAwait(false);
            return;
        }

        var argumentsJson = BuildExportArgumentsJson(normalizedFormat, title, rowsElement, outputPath);
        var request = new InvokeToolRequest {
            RequestId = NextId(),
            ToolName = "export_table_artifact",
            ArgumentsJson = argumentsJson
        };

        await SetStatusAsync(SessionStatus.Exporting()).ConfigureAwait(false);
        try {
            var response = await client.RequestAsync<InvokeToolResultMessage>(request, CancellationToken.None).ConfigureAwait(false);
            var output = response.Output;
            if (output.Ok == false) {
                await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.ToolError, normalizedFormat, output.Error)).ConfigureAwait(false);
                return;
            }

            var filePath = TryExtractExportFilePath(output.Output);
            var completedNotice = ExportNotice.Succeeded(normalizedFormat, filePath);
            if (!string.IsNullOrWhiteSpace(filePath)) {
                await UpdateLastExportDirectoryFromFilePathAsync(filePath).ConfigureAwait(false);
            }
            await ReportExportNoticeAsync(exportId, completedNotice).ConfigureAwait(false);
        } catch (Exception ex) {
            await ReportExportNoticeAsync(exportId, ExportNotice.Failed(ExportNoticeKind.Exception, normalizedFormat, ex.Message)).ConfigureAwait(false);
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

    private async Task<string?> ShowExportSavePickerAsync(string normalizedFormat, string title) {
        string? selectedPath = null;
        await RunOnUiThreadAsync(async () => {
            var picker = new FileSavePicker();
            var extension = GetExportFileExtension(normalizedFormat);
            var displayLabel = normalizedFormat switch {
                "xlsx" => "Excel Workbook",
                "docx" => "Word Document",
                "csv" => "CSV File",
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

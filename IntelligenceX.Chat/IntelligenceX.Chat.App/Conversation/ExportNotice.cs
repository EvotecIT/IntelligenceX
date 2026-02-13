using System;
using System.IO;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Structured export notice contract for system + data-view feedback.
/// </summary>
internal readonly record struct ExportNotice(ExportNoticeKind Kind, string Format = "", string? Detail = null, string? FilePath = null) {
    public static ExportNotice Failed(ExportNoticeKind kind, string format = "", string? detail = null) => new(kind, format, detail, null);

    public static ExportNotice Succeeded(string format, string? filePath = null) => new(ExportNoticeKind.Completed, format, null, filePath);

    public bool Ok => Kind == ExportNoticeKind.Completed;
}

/// <summary>
/// Export notice kinds.
/// </summary>
internal enum ExportNoticeKind {
    InvalidFormat,
    NoRows,
    Disconnected,
    ToolError,
    Exception,
    Completed
}

/// <summary>
/// Renders export notices to user-facing strings.
/// </summary>
internal static class ExportNoticeFormatter {
    public static string Status(ExportNotice notice) => notice.Ok ? "Exported" : "Export failed";

    public static string SystemText(ExportNotice notice) {
        return notice.Kind switch {
            ExportNoticeKind.InvalidFormat => "Export failed: format is empty.",
            ExportNoticeKind.NoRows => "Export failed: no tabular rows were provided.",
            ExportNoticeKind.Disconnected => "Export failed: service is disconnected.",
            ExportNoticeKind.ToolError => "Export failed: " + NormalizeDetail(notice.Detail),
            ExportNoticeKind.Exception => "Export failed: " + NormalizeDetail(notice.Detail),
            ExportNoticeKind.Completed => BuildCompletedSystemText(notice),
            _ => "Export failed: unknown error."
        };
    }

    public static string DataViewText(ExportNotice notice) {
        return notice.Kind switch {
            ExportNoticeKind.InvalidFormat => "Export failed: format is empty.",
            ExportNoticeKind.NoRows => "Export failed: no rows were provided.",
            ExportNoticeKind.Disconnected => "Export failed: service is disconnected.",
            ExportNoticeKind.ToolError => "Export failed: " + NormalizeDetail(notice.Detail),
            ExportNoticeKind.Exception => "Export failed: " + NormalizeDetail(notice.Detail),
            ExportNoticeKind.Completed => BuildCompletedDataViewText(notice),
            _ => "Export failed: unknown error."
        };
    }

    private static string BuildCompletedSystemText(ExportNotice notice) {
        if (string.IsNullOrWhiteSpace(notice.FilePath)) {
            return "Export completed.";
        }

        return "Exported " + NormalizeFormat(notice.Format) + ": " + notice.FilePath;
    }

    private static string BuildCompletedDataViewText(ExportNotice notice) {
        var format = NormalizeFormat(notice.Format);
        if (string.IsNullOrWhiteSpace(notice.FilePath)) {
            return "Exported " + format + ".";
        }

        var fileName = Path.GetFileName(notice.FilePath);
        return string.IsNullOrWhiteSpace(fileName)
            ? "Exported " + format + "."
            : "Exported " + format + ": " + fileName;
    }

    private static string NormalizeDetail(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Unknown error." : normalized;
    }

    private static string NormalizeFormat(string? format) {
        var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length == 0 ? "artifact" : normalized;
    }
}

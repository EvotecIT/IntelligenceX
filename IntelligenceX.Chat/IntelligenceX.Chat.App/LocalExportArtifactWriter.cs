using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.ExportArtifacts;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Local export writer used by the desktop app when service-side export tooling is unavailable.
/// </summary>
internal static class LocalExportArtifactWriter {
    internal delegate void MarkdownTranscriptWriter(string outputPath, string markdown);
    internal delegate void DocxTranscriptWriter(
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories,
        int? docxVisualMaxWidthPx);

    public static bool TryReadRows(JsonElement rowsElement, out List<string[]> rows) {
        rows = new List<string[]>();
        if (rowsElement.ValueKind != JsonValueKind.Array) {
            return false;
        }

        int maxColumns = 0;
        foreach (var rowElement in rowsElement.EnumerateArray()) {
            string[] row;
            if (rowElement.ValueKind == JsonValueKind.Array) {
                var values = new List<string>();
                foreach (var cell in rowElement.EnumerateArray()) {
                    values.Add(ReadCellAsText(cell));
                }
                row = values.ToArray();
            } else {
                row = [ReadCellAsText(rowElement)];
            }

            if (row.Length > maxColumns) {
                maxColumns = row.Length;
            }

            rows.Add(row);
        }

        if (rows.Count == 0 || maxColumns == 0) {
            rows.Clear();
            return false;
        }

        for (int i = 0; i < rows.Count; i++) {
            if (rows[i].Length == maxColumns) {
                continue;
            }

            var expanded = new string[maxColumns];
            Array.Copy(rows[i], expanded, rows[i].Length);
            for (int c = rows[i].Length; c < expanded.Length; c++) {
                expanded[c] = string.Empty;
            }
            rows[i] = expanded;
        }

        return true;
    }

    public static string ResolveOutputPath(string format, string? title, string? outputPath, string defaultPrefix = "dataset") {
        var extension = GetFileExtension(format);
        var normalizedPath = (outputPath ?? string.Empty).Trim();

        if (normalizedPath.Length == 0) {
            var dir = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "exports");
            Directory.CreateDirectory(dir);
            var stem = SanitizeFileStem(title, defaultPrefix);
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(dir, stem + "-" + ts + extension);
        }

        var fullPath = Path.GetFullPath(normalizedPath);
        var currentExtension = Path.GetExtension(fullPath);
        if (!string.Equals(currentExtension, extension, StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.ChangeExtension(fullPath, extension);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    public static void ExportTable(string format, string title, IReadOnlyList<string[]> rows, string outputPath) {
        if (rows is null || rows.Count == 0) {
            throw new InvalidOperationException("No rows were provided for export.");
        }

        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalizedFormat) {
            case ExportPreferencesContract.FormatCsv:
                WriteCsv(rows, outputPath);
                break;
            case ExportPreferencesContract.FormatXlsx:
                OfficeImoArtifactWriter.WriteXlsx(title, rows, outputPath);
                break;
            case ExportPreferencesContract.FormatDocx:
                OfficeImoArtifactWriter.WriteDocxTable(title, rows, outputPath);
                break;
            default:
                throw new InvalidOperationException("Unsupported export format: " + normalizedFormat);
        }
    }

    public static TranscriptExportResult ExportTranscript(
        string format,
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories = null,
        int? docxVisualMaxWidthPx = null,
        bool allowMarkdownFallback = true) {
        return ExportTranscript(
            format,
            title,
            markdown,
            outputPath,
            additionalAllowedImageDirectories,
            docxVisualMaxWidthPx,
            allowMarkdownFallback,
            static (path, text) => File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
            static (docxTitle, docxMarkdown, docxPath, allowedImageDirectories, maxWidthPx) =>
                OfficeImoArtifactWriter.WriteDocxTranscript(docxTitle, docxMarkdown, docxPath, allowedImageDirectories, maxWidthPx));
    }

    /// <summary>
    /// Writes a materialized DOCX first, then retries from the original Markdown so temporary images
    /// can be disposed without breaking a Markdown fallback artifact.
    /// </summary>
    public static TranscriptExportResult ExportDocxWithMaterializedVisualFallback(
        string title,
        string originalMarkdown,
        string materializedMarkdown,
        string outputPath,
        IReadOnlyList<string>? materializedImageDirectories,
        int? docxVisualMaxWidthPx = null) {
        return ExportDocxWithMaterializedVisualFallback(
            title,
            originalMarkdown,
            materializedMarkdown,
            outputPath,
            materializedImageDirectories,
            docxVisualMaxWidthPx,
            static (path, text) => File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
            static (docxTitle, docxMarkdown, docxPath, allowedImageDirectories, maxWidthPx) =>
                OfficeImoArtifactWriter.WriteDocxTranscript(docxTitle, docxMarkdown, docxPath, allowedImageDirectories, maxWidthPx));
    }

    internal static TranscriptExportResult ExportDocxWithMaterializedVisualFallback(
        string title,
        string originalMarkdown,
        string materializedMarkdown,
        string outputPath,
        IReadOnlyList<string>? materializedImageDirectories,
        int? docxVisualMaxWidthPx,
        MarkdownTranscriptWriter markdownWriter,
        DocxTranscriptWriter docxWriter) {
        var materializedResult = ExportTranscript(
            ExportPreferencesContract.FormatDocx,
            title,
            materializedMarkdown,
            outputPath,
            materializedImageDirectories,
            docxVisualMaxWidthPx,
            allowMarkdownFallback: false,
            markdownWriter,
            docxWriter);
        if (materializedResult.Succeeded) {
            return materializedResult;
        }

        var retryResult = ExportTranscript(
            ExportPreferencesContract.FormatDocx,
            title,
            originalMarkdown,
            outputPath,
            additionalAllowedImageDirectories: null,
            docxVisualMaxWidthPx,
            allowMarkdownFallback: true,
            markdownWriter,
            docxWriter);
        return ResolveTranscriptExportResultAfterMaterializedDocxRetry(materializedResult, retryResult);
    }

    internal static TranscriptExportResult ExportTranscript(
        string format,
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories,
        int? docxVisualMaxWidthPx,
        bool allowMarkdownFallback,
        MarkdownTranscriptWriter markdownWriter,
        DocxTranscriptWriter docxWriter) {
        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalizedFormat) {
            case ExportPreferencesContract.FormatMarkdown:
                try {
                    var safeMarkdown = TranscriptMarkdownPreparation.PrepareTranscriptMarkdownForPortableExport(markdown ?? string.Empty);
                    markdownWriter(outputPath, safeMarkdown);
                    return TranscriptExportResult.Success(normalizedFormat, ExportPreferencesContract.FormatMarkdown, outputPath);
                } catch (Exception ex) {
                    return TranscriptExportResult.Failed(
                        normalizedFormat,
                        outputPath,
                        new TranscriptExportFailure(TranscriptExportStage.MarkdownWrite, ex.Message));
                }
            case ExportPreferencesContract.FormatDocx:
                try {
                    var safeMarkdown = TranscriptMarkdownPreparation.PrepareTranscriptMarkdownForExport(markdown ?? string.Empty);
                    docxWriter(title, safeMarkdown, outputPath, additionalAllowedImageDirectories, docxVisualMaxWidthPx);
                    return TranscriptExportResult.Success(normalizedFormat, ExportPreferencesContract.FormatDocx, outputPath);
                } catch (Exception ex) {
                    var docxFailure = new TranscriptExportFailure(TranscriptExportStage.DocxWrite, ex.Message);
                    if (!allowMarkdownFallback) {
                        return TranscriptExportResult.Failed(normalizedFormat, outputPath, docxFailure);
                    }

                    var fallbackPath = ResolveOutputPath(
                        ExportPreferencesContract.FormatMarkdown,
                        title,
                        outputPath,
                        defaultPrefix: "transcript");
                    try {
                        var safeMarkdown = TranscriptMarkdownPreparation.PrepareTranscriptMarkdownForPortableExport(markdown ?? string.Empty);
                        markdownWriter(fallbackPath, safeMarkdown);
                        return TranscriptExportResult.SuccessWithFallback(
                            normalizedFormat,
                            ExportPreferencesContract.FormatMarkdown,
                            fallbackPath,
                            new TranscriptExportFallback(
                                TranscriptExportFallbackKind.Markdown,
                                fallbackPath,
                                docxFailure));
                    } catch (Exception fallbackEx) {
                        return TranscriptExportResult.Failed(
                            normalizedFormat,
                            outputPath,
                            new TranscriptExportFailure(TranscriptExportStage.MarkdownFallbackWrite, fallbackEx.Message),
                            new TranscriptExportFallback(
                                TranscriptExportFallbackKind.Markdown,
                                fallbackPath,
                                docxFailure));
                    }
                }
            default:
                throw new InvalidOperationException("Unsupported transcript export format: " + normalizedFormat);
        }
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

    private static string ReadCellAsText(JsonElement cell) {
        return cell.ValueKind switch {
            JsonValueKind.String => cell.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => cell.GetRawText(),
            JsonValueKind.Object => cell.GetRawText(),
            JsonValueKind.Array => cell.GetRawText(),
            _ => cell.ToString()
        };
    }

    private static void WriteCsv(IReadOnlyList<string[]> rows, string outputPath) {
        using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        DelimitedTextFormatter.WriteCsv(writer, rows, terminateLastRow: true);
    }

    private static string GetFileExtension(string format) {
        return (format ?? string.Empty).Trim().ToLowerInvariant() switch {
            ExportPreferencesContract.FormatXlsx => ".xlsx",
            ExportPreferencesContract.FormatDocx => ".docx",
            ExportPreferencesContract.FormatMarkdown => ".md",
            _ => ".csv"
        };
    }

    private static string SanitizeFileStem(string? title, string fallback) {
        var stem = (title ?? string.Empty).Trim();
        if (stem.Length == 0) {
            stem = fallback;
        }

        foreach (var ch in Path.GetInvalidFileNameChars()) {
            stem = stem.Replace(ch, '_');
        }

        if (stem.Length > 80) {
            stem = stem[..80].TrimEnd();
        }

        return stem.Length == 0 ? fallback : stem;
    }
}

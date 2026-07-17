using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ChartForgeX.Markup;
using ChartForgeX.Markup.Mermaid;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Materializes ChartForgeX and Mermaid transcript fences for native DOCX export.
/// </summary>
internal static class NativeTranscriptVisualExportMaterializer {
    internal static NativeTranscriptVisualExportMaterialization? TryMaterialize(string? markdown) {
        if (string.IsNullOrWhiteSpace(markdown)) {
            return null;
        }

        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var scan = VisualMarkupScanner.Scan(normalized);
        if (scan.Blocks.Count == 0) {
            return null;
        }

        var lines = normalized.Split('\n').ToList();
        var parser = new MermaidVisualMarkupParser();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IntelligenceX.Chat",
            "docx-native-visuals",
            Guid.NewGuid().ToString("N"));
        var imageCount = 0;

        try {
            foreach (var block in scan.Blocks.OrderByDescending(static block => block.FenceLine)) {
                var singleBlock = VisualMarkupScanner.ParseFenceBlock(
                    block.FenceInfo,
                    block.Payload,
                    block.FenceLine,
                    block.StartLine,
                    block.EndLine);
                var artifact = parser.Parse(singleBlock).Artifacts.FirstOrDefault();
                if (artifact is null) {
                    continue;
                }

                var preview = Rendering.NativeVisualPreviewRenderer.TryRender(artifact, out _);
                if (preview?.Png is not { Length: > 0 } png) {
                    continue;
                }

                Directory.CreateDirectory(directory);
                imageCount++;
                var fileName = "visual-" + imageCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png";
                var imagePath = Path.Combine(directory, fileName);
                File.WriteAllBytes(imagePath, png);

                var startIndex = Math.Clamp(block.FenceLine - 1, 0, lines.Count - 1);
                var endIndex = FindFenceEnd(lines, startIndex, block.EndLine);
                var title = string.IsNullOrWhiteSpace(artifact.Title) ? "Visual" : artifact.Title.Trim();
                var markdownImagePath = imagePath.Replace('\\', '/');
                lines.RemoveRange(startIndex, endIndex - startIndex + 1);
                lines.Insert(startIndex, "![" + EscapeImageAlt(title) + "](" + markdownImagePath + ")");
            }

            if (imageCount == 0) {
                TryDeleteDirectory(directory);
                return null;
            }

            return new NativeTranscriptVisualExportMaterialization(string.Join("\n", lines), directory);
        } catch {
            TryDeleteDirectory(directory);
            return null;
        }
    }

    private static int FindFenceEnd(IReadOnlyList<string> lines, int startIndex, int scannerEndLine) {
        var opening = lines[startIndex].TrimStart();
        if (opening.Length >= 3 && (opening[0] == '`' || opening[0] == '~')) {
            var marker = opening[0];
            var markerLength = CountPrefix(opening, marker);
            for (var index = startIndex + 1; index < lines.Count; index++) {
                var candidate = lines[index].TrimStart();
                if (lines[index].Length - candidate.Length > 3 || CountPrefix(candidate, marker) < markerLength) {
                    continue;
                }

                if (candidate.SkipWhile(value => value == marker).All(char.IsWhiteSpace)) {
                    return index;
                }
            }
        }

        return Math.Clamp(scannerEndLine, startIndex, lines.Count - 1);
    }

    private static int CountPrefix(string value, char marker) {
        var count = 0;
        while (count < value.Length && value[count] == marker) {
            count++;
        }
        return count;
    }

    private static string EscapeImageAlt(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    internal static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup for temporary export images.
        }
    }
}

/// <summary>
/// Owns native DOCX visual materialization files for the duration of one export.
/// </summary>
internal sealed class NativeTranscriptVisualExportMaterialization : IDisposable {
    private readonly string _directory;

    internal NativeTranscriptVisualExportMaterialization(string markdown, string directory) {
        Markdown = markdown ?? string.Empty;
        _directory = directory ?? string.Empty;
    }

    internal string Markdown { get; }

    internal IReadOnlyList<string> AllowedImageDirectories =>
        string.IsNullOrWhiteSpace(_directory) ? Array.Empty<string>() : [_directory];

    public void Dispose() => NativeTranscriptVisualExportMaterializer.TryDeleteDirectory(_directory);
}

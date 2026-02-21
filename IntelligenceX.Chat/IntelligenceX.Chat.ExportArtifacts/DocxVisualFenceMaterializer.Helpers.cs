using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.ExportArtifacts;

internal static partial class DocxVisualFenceMaterializer {
    private static string ReadNodeId(JsonElement node) {
        if (!TryGetProperty(node, "id", out var idElement)) {
            return string.Empty;
        }

        return idElement.ValueKind switch {
            JsonValueKind.String => TrimAndCap(idElement.GetString(), 80),
            JsonValueKind.Number => idElement.TryGetDouble(out var n) && double.IsFinite(n)
                ? n.ToString("0.####", CultureInfo.InvariantCulture)
                : string.Empty,
            _ => string.Empty
        };
    }

    private static string ReadNodeIdProperty(JsonElement edge, string propertyName) {
        if (!TryGetProperty(edge, propertyName, out var value)) {
            return string.Empty;
        }

        return value.ValueKind switch {
            JsonValueKind.String => TrimAndCap(value.GetString(), 80),
            JsonValueKind.Number => value.TryGetDouble(out var n) && double.IsFinite(n)
                ? n.ToString("0.####", CultureInfo.InvariantCulture)
                : string.Empty,
            _ => string.Empty
        };
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value) {
        foreach (var property in element.EnumerateObject()) {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadStringProperty(JsonElement element, string propertyName, int maxLength) {
        if (!TryGetProperty(element, propertyName, out var value)) {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? TrimAndCap(value.GetString(), maxLength)
            : string.Empty;
    }

    private static string TrimAndCap(string? value, int maxLength) {
        var text = (value ?? string.Empty).Trim();
        if (text.Length <= maxLength) {
            return text;
        }

        return text[..maxLength];
    }

    private static string WriteSvg(string tempDirectory, int imageIndex, string kind, string svg) {
        Directory.CreateDirectory(tempDirectory);
        var path = Path.Combine(tempDirectory, "visual-" + imageIndex.ToString("000", CultureInfo.InvariantCulture) + "-" + kind + ".svg");
        File.WriteAllText(path, svg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string CreateTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "docx-visuals", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NormalizeText(string value) {
        return (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static string ToMarkdownPath(string path) {
        return (path ?? string.Empty).Replace('\\', '/');
    }

    private static string DetectLineEnding(string text) {
        if (text.Contains("\r\n", StringComparison.Ordinal)) {
            return "\r\n";
        }
        if (text.Contains('\r')) {
            return "\r";
        }
        return "\n";
    }

    private static string EscapeXml(string text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }

        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("'", "&apos;", StringComparison.Ordinal);
    }

    private static void TryDeleteDirectory(string path) {
        try {
            if (Directory.Exists(path)) {
                Directory.Delete(path, recursive: true);
            }
        } catch {
            // Best-effort cleanup only.
        }
    }
}

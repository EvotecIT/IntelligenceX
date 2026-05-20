using System;
using System.IO;
using IntelligenceX.Utils;

namespace IntelligenceX.Treatment;

internal static class TreatmentLocalInputReader {
    private const int MaximumInlineFileCharacters = 1_000_000;

    public static TreatmentLocalInputContent? TryRead(TreatmentInputArtifact input, TreatmentPromptBuildOptions options) {
        if (input is null || options is null || !options.InlineLocalFiles || string.IsNullOrWhiteSpace(input.Path)) {
            return null;
        }
        if (!IsTextLike(input)) {
            return null;
        }

        string path;
        try {
            path = ResolvePath(input.Path!, options.BaseDirectory);
        } catch (Exception ex) when (IsLocalReadException(ex)) {
            return new TreatmentLocalInputContent(input.Path!, null, false, false, ex.Message);
        }

        try {
            if (!File.Exists(path)) {
                return new TreatmentLocalInputContent(path, null, false, false, "file not found");
            }

            var requestedMax = Math.Max(0, options.MaxInlineFileCharacters);
            var max = Math.Min(requestedMax, MaximumInlineFileCharacters);
            using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[max + 1];
            var count = reader.ReadBlock(buffer, 0, buffer.Length);
            var truncated = count > max;
            var text = new string(buffer, 0, truncated ? max : count);
            var warning = requestedMax > MaximumInlineFileCharacters
                ? "inline character limit clamped to " + MaximumInlineFileCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
            return new TreatmentLocalInputContent(path, text, true, truncated, warning);
        } catch (Exception ex) when (IsLocalReadException(ex)) {
            return new TreatmentLocalInputContent(path, null, true, false, ex.Message);
        }
    }

    private static bool IsTextLike(TreatmentInputArtifact input) {
        var mediaType = NormalizeMediaType(input.MediaType);
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/markdown", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Equals("application/x-ndjson", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var extension = Path.GetExtension(input.Path ?? string.Empty);
        return extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolvePath(string path, string? baseDirectory) {
        var root = ResolveRoot(baseDirectory);
        var resolved = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));
        PathSafety.EnsureUnderRoot(resolved, root);
        return resolved;
    }

    private static string ResolveRoot(string? baseDirectory) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(baseDirectory) ? Directory.GetCurrentDirectory() : baseDirectory!);

    private static string NormalizeMediaType(string? mediaType) {
        if (string.IsNullOrWhiteSpace(mediaType)) {
            return string.Empty;
        }

        var semicolon = mediaType!.IndexOf(';');
        return (semicolon < 0 ? mediaType : mediaType.Substring(0, semicolon)).Trim();
    }

    private static bool IsLocalReadException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException;

}

internal sealed class TreatmentLocalInputContent {
    public TreatmentLocalInputContent(string path, string? text, bool exists, bool truncated, string? warning) {
        Path = path;
        Text = text;
        Exists = exists;
        Truncated = truncated;
        Warning = warning;
    }

    public string Path { get; }
    public string? Text { get; }
    public bool Exists { get; }
    public bool Truncated { get; }
    public string? Warning { get; }
}

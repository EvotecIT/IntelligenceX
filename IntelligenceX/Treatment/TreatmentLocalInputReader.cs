using System;
using System.IO;

namespace IntelligenceX.Treatment;

internal static class TreatmentLocalInputReader {
    public static TreatmentLocalInputContent? TryRead(TreatmentInputArtifact input, TreatmentPromptBuildOptions options) {
        if (input is null || options is null || !options.InlineLocalFiles || string.IsNullOrWhiteSpace(input.Path)) {
            return null;
        }
        if (!IsTextLike(input)) {
            return null;
        }

        var path = ResolvePath(input.Path!, options.BaseDirectory);
        if (!File.Exists(path)) {
            return new TreatmentLocalInputContent(path, null, false, false, "file not found");
        }

        var max = Math.Max(0, options.MaxInlineFileCharacters);
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[max + 1];
        var count = reader.Read(buffer, 0, buffer.Length);
        var truncated = count > max;
        var text = new string(buffer, 0, truncated ? max : count);
        return new TreatmentLocalInputContent(path, text, true, truncated, null);
    }

    private static bool IsTextLike(TreatmentInputArtifact input) {
        var mediaType = input.MediaType ?? string.Empty;
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

    private static string ResolvePath(string path, string? baseDirectory) {
        if (Path.IsPathRooted(path)) {
            return Path.GetFullPath(path);
        }
        var root = string.IsNullOrWhiteSpace(baseDirectory) ? Directory.GetCurrentDirectory() : baseDirectory!;
        return Path.GetFullPath(Path.Combine(root, path));
    }
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

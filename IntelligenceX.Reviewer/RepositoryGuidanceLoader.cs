using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal static class RepositoryGuidanceLoader {
    public static string BuildBlock(ReviewSettings settings) {
        if (!settings.RepositoryGuidance.Enabled || settings.RepositoryGuidance.Paths.Count == 0) {
            return string.Empty;
        }

        var entries = LoadEntries(settings.RepositoryGuidance.Paths, settings.RepositoryGuidance.MaxChars);
        if (entries.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Repository guidance:");
        sb.AppendLine("Treat this as repo-owned review guidance. Use it to understand architecture, conventions, and expected review posture.");
        foreach (var entry in entries) {
            sb.AppendLine();
            sb.AppendLine($"### {entry.Path}");
            sb.AppendLine(entry.Content.Trim());
        }
        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<RepositoryGuidanceEntry> LoadEntries(IReadOnlyList<string> paths, int maxChars) {
        if (maxChars <= 0) {
            return Array.Empty<RepositoryGuidanceEntry>();
        }

        var root = Path.GetFullPath(Directory.GetCurrentDirectory());
        var remaining = maxChars;
        var entries = new List<RepositoryGuidanceEntry>();
        foreach (var path in paths) {
            if (remaining <= 0 || string.IsNullOrWhiteSpace(path)) {
                break;
            }

            var normalized = path.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized)) {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
            if (!IsUnderRoot(root, fullPath) || !File.Exists(fullPath)) {
                continue;
            }

            var text = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            if (text.Length > remaining) {
                text = text[..remaining];
            }

            entries.Add(new RepositoryGuidanceEntry(path.Trim(), text));
            remaining -= text.Length;
        }

        return entries;
    }

    private static bool IsUnderRoot(string root, string path) {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
        var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(normalizedPath, root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

    private sealed record RepositoryGuidanceEntry(string Path, string Content);
}

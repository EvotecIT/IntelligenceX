using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace IntelligenceX.Reviewer;

internal enum CleanupMode {
    Comment,
    Edit,
    Hybrid
}

internal sealed class CleanupSettings {
    public bool Enabled { get; set; }
    public string Scope { get; set; } = "pr";
    public CleanupMode Mode { get; set; } = CleanupMode.Comment;
    public string? RequireLabel { get; set; }
    public double MinConfidence { get; set; } = 0.85;
    public bool PostEditComment { get; set; } = true;
    public IReadOnlyList<string> AllowedEdits { get; set; } =
        new[] { "formatting", "grammar", "title", "sections" };
    public string? Template { get; set; }
    public string? TemplatePath { get; set; }

    public bool AllowsPr => ScopeMatches("pr") || ScopeMatches("pull_request") || ScopeMatches("both");
    public bool AllowsIssues => ScopeMatches("issue") || ScopeMatches("issues") || ScopeMatches("both");

    public bool RequiresLabel => !string.IsNullOrWhiteSpace(RequireLabel);

    public bool AllowsTitleEdit => AllowedEdits.Any(ed => ed.Equals("title", StringComparison.OrdinalIgnoreCase));

    public bool AllowsBodyEdit => AllowedEdits.Any(ed =>
        ed.Equals("body", StringComparison.OrdinalIgnoreCase) ||
        ed.Equals("formatting", StringComparison.OrdinalIgnoreCase) ||
        ed.Equals("grammar", StringComparison.OrdinalIgnoreCase) ||
        ed.Equals("sections", StringComparison.OrdinalIgnoreCase));

    public bool HasLabel(IReadOnlyList<string> labels) {
        if (!RequiresLabel) {
            return true;
        }
        foreach (var label in labels) {
            if (label.Equals(RequireLabel, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    public string? ResolveTemplate() {
        if (!string.IsNullOrWhiteSpace(Template)) {
            return Template;
        }
        if (!string.IsNullOrWhiteSpace(TemplatePath)) {
            var path = ResolveTemplatePath(TemplatePath!);
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
                var info = new FileInfo(path);
                if (info.Length <= 50_000) {
                    return File.ReadAllText(path);
                }
            }
        }
        return null;
    }

    private static string? ResolveTemplatePath(string path) {
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var baseDir = !string.IsNullOrWhiteSpace(workspace) ? workspace : Environment.CurrentDirectory;
        var baseFull = Path.GetFullPath(baseDir);
        var fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseFull, path));
        if (!IsUnderRoot(baseFull, fullPath)) {
            return null;
        }
        return fullPath;
    }

    private static bool IsUnderRoot(string root, string candidate) {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidateFull = Path.GetFullPath(candidate);
        if (candidateFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        var prefix = rootFull + Path.DirectorySeparatorChar;
        return candidateFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private bool ScopeMatches(string value) {
        return Scope.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    public static CleanupMode ParseMode(string? value, CleanupMode fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return value.Trim().ToLowerInvariant() switch {
            "edit" => CleanupMode.Edit,
            "hybrid" => CleanupMode.Hybrid,
            _ => CleanupMode.Comment
        };
    }

    public static double ParseConfidence(string? value, double fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return ClampConfidence(parsed);
        }
        return fallback;
    }

    public static double ClampConfidence(double value) {
        if (value < 0) {
            return 0;
        }
        if (value > 1) {
            return 1;
        }
        return value;
    }
}

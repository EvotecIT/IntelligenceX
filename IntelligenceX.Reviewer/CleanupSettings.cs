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
        if (!string.IsNullOrWhiteSpace(TemplatePath) && File.Exists(TemplatePath)) {
            return File.ReadAllText(TemplatePath);
        }
        return null;
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
            return ClampConfidence(parsed, fallback);
        }
        return fallback;
    }

    public static double ClampConfidence(double value, double fallback) {
        return value >= 0 && value <= 1 ? value : fallback;
    }
}

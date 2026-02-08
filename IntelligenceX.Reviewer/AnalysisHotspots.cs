using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Reviewer;

internal static class AnalysisHotspots {
    private const string HotspotHeader = "### Security Hotspots 🔥";
    private const int DefaultMaxItems = 10;

    internal static string BuildBlock(ReviewSettings settings, IReadOnlyList<AnalysisFinding> findings) {
        if (settings?.Analysis?.Enabled != true) {
            return string.Empty;
        }
        var hotspotSettings = settings.Analysis.Hotspots;
        if (hotspotSettings is null || !hotspotSettings.Show) {
            return string.Empty;
        }

        findings ??= Array.Empty<AnalysisFinding>();

        var workspace = ResolveWorkspaceRoot();
        var catalog = TryLoadCatalog(workspace);
        var hotspotFindings = FilterHotspots(findings, catalog);

        if (hotspotFindings.Count == 0 && !hotspotSettings.AlwaysRender) {
            return string.Empty;
        }

        var maxItems = hotspotSettings.MaxItems < 0 ? 0 : hotspotSettings.MaxItems;
        if (maxItems == 0) {
            maxItems = 0;
        }

        var statePath = ResolveStatePath(workspace, hotspotSettings.StatePath);
        var stateFile = HotspotStateStore.TryLoad(statePath);
        var state = HotspotStateStore.ToMap(stateFile.Items);

        var rendered = new List<(AnalysisFinding Finding, string Key, string Status, string? Note)>();
        var missingKeys = new List<string>();
        foreach (var finding in hotspotFindings) {
            var key = ComputeHotspotKey(finding);
            if (!state.TryGetValue(key, out var entry)) {
                missingKeys.Add(key);
                entry = new HotspotStateEntry(key, "to-review", Note: null, CreatedAt: null);
            }
            rendered.Add((finding, key, NormalizeStatus(entry.Status), entry.Note));
        }

        // Don't show suppressed items in the list; still count them.
        var suppressedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in rendered) {
            if (IsSuppressedStatus(item.Status)) {
                suppressedKeys.Add(item.Key);
            }
        }

        var counts = rendered
            .GroupBy(item => item.Status, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        counts.TryGetValue("to-review", out var toReviewCount);
        counts.TryGetValue("safe", out var safeCount);
        counts.TryGetValue("fixed", out var fixedCount);
        counts.TryGetValue("accepted-risk", out var acceptedRiskCount);
        counts.TryGetValue("wont-fix", out var wontFixCount);
        counts.TryGetValue("suppress", out var suppressCount);

        var lines = new List<string> { HotspotHeader };
        lines.Add(
            $"- Hotspots: {rendered.Count} (to-review: {toReviewCount}, safe: {safeCount}, fixed: {fixedCount}, accepted-risk: {acceptedRiskCount}, wont-fix: {wontFixCount}, suppress: {suppressCount})");

        if (hotspotSettings.ShowStateSummary) {
            var relStatePath = TryGetRelativePathWithinWorkspace(workspace, statePath) ?? statePath;
            var stateNote = stateFile.Loaded
                ? "found"
                : (File.Exists(statePath) ? "unreadable" : "missing");
            lines.Add($"- State file: {relStatePath.Replace('\\', '/')} ({stateNote})");
            if (missingKeys.Count > 0) {
                lines.Add($"- Missing state entries: {missingKeys.Count}");
            }
        }

        if (rendered.Count == 0) {
            lines.Add("- Items: none");
            return string.Join("\n", lines).TrimEnd();
        }

        if (hotspotSettings.MaxItems == 0) {
            lines.Add("- Items: hidden (maxItems=0)");
        } else {
            var visibleItems = rendered.Where(item => !IsSuppressedStatus(item.Status)).ToList();
            var ordered = OrderFindings(visibleItems);
            var shown = ordered.Take(maxItems <= 0 ? DefaultMaxItems : maxItems).ToList();
            lines.Add("- Items:");
            foreach (var item in shown) {
                var location = FormatLocation(item.Finding);
                var rule = string.IsNullOrWhiteSpace(item.Finding.RuleId) ? string.Empty : $" ({item.Finding.RuleId})";
                var status = NormalizeStatus(item.Status);
                var message = TrimMessage(item.Finding.Message);
                var note = string.IsNullOrWhiteSpace(item.Note) ? string.Empty : $" Note: {TrimMessage(item.Note)}";
                lines.Add($"- [{status}] `{location}`{rule} {message} (key {item.Key}){note}");
            }
            if (visibleItems.Count > shown.Count) {
                lines.Add($"- Showing first {shown.Count} of {visibleItems.Count} hotspot(s).");
            }
        }

        if (hotspotSettings.ShowStateSummary && missingKeys.Count > 0) {
            var snippet = HotspotStateStore.BuildSuggestedStateSnippet(missingKeys, defaultStatus: "to-review");
            lines.Add(string.Empty);
            lines.Add("Suggested state entries (add/merge into the state file):");
            lines.Add("```json");
            lines.Add(snippet.TrimEnd());
            lines.Add("```");
        }

        return string.Join("\n", lines).TrimEnd();
    }

    internal static string ComputeHotspotKey(AnalysisFinding finding) {
        return ComputeHotspotKeyInternal(finding);
    }

    private static IReadOnlyList<AnalysisFinding> FilterHotspots(IReadOnlyList<AnalysisFinding> findings, AnalysisCatalog? catalog) {
        if (findings is null || findings.Count == 0) {
            return Array.Empty<AnalysisFinding>();
        }
        var list = new List<AnalysisFinding>();
        foreach (var finding in findings) {
            var ruleId = finding.RuleId;
            if (string.IsNullOrWhiteSpace(ruleId)) {
                continue;
            }
            if (catalog is not null && catalog.TryGetRule(ruleId, out var rule)) {
                if (string.Equals(rule.Type, "security-hotspot", StringComparison.OrdinalIgnoreCase)) {
                    list.Add(finding);
                }
                continue;
            }
            // If catalog is unavailable, allow opting-in by rule id prefixing convention.
            if (ruleId.StartsWith("IXHOT", StringComparison.OrdinalIgnoreCase)) {
                list.Add(finding);
            }
        }
        return list;
    }

    private static IEnumerable<(AnalysisFinding Finding, string Key, string Status, string? Note)> OrderFindings(
        IReadOnlyList<(AnalysisFinding Finding, string Key, string Status, string? Note)> items) {
            return items
            .OrderByDescending(item => StatusRank(item.Status))
            .ThenBy(item => item.Finding.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Finding.Line)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static int StatusRank(string? status) {
        return NormalizeStatus(status) switch {
            "to-review" => 6,
            "accepted-risk" => 5,
            "wont-fix" => 4,
            "safe" => 3,
            "fixed" => 2,
            "suppress" => 1,
            _ => 0
        };
    }

    internal static string NormalizeStatus(string? status) {
        if (string.IsNullOrWhiteSpace(status)) {
            return "to-review";
        }
        var value = status.Trim().ToLowerInvariant();
        return value switch {
            "todo" => "to-review",
            "to_review" => "to-review",
            "toreview" => "to-review",
            "review" => "to-review",
            "reviewed" => "safe",
            "ok" => "safe",
            "accepted" => "accepted-risk",
            "accept" => "accepted-risk",
            "risk-accepted" => "accepted-risk",
            "wontfix" => "wont-fix",
            "won't-fix" => "wont-fix",
            "ignore" => "suppress",
            "ignored" => "suppress",
            "suppress" => "suppress",
            _ => value
        };
    }

    internal static bool IsSuppressedStatus(string? status) {
        return string.Equals(NormalizeStatus(status), "suppress", StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeHotspotKeyInternal(AnalysisFinding finding) {
        var ruleId = string.IsNullOrWhiteSpace(finding.RuleId) ? "unknown" : finding.RuleId!.Trim();
        var fingerprint = finding.Fingerprint;
        if (!string.IsNullOrWhiteSpace(fingerprint)) {
            return $"{ruleId}:{fingerprint!.Trim()}";
        }

        // Fallback: stable 64-bit hash over finding identity.
        var basis = string.Join("|", new[] {
            finding.Tool ?? string.Empty,
            ruleId,
            finding.Path ?? string.Empty,
            finding.Line.ToString(CultureInfo.InvariantCulture),
            finding.Message ?? string.Empty
        });
        return $"{ruleId}:{Fnv1a64Hex(basis)}";
    }

    private static string Fnv1a64Hex(string value) {
        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var ch in value ?? string.Empty) {
            hash ^= ch;
            hash *= prime;
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static string ResolveStatePath(string workspace, string configured) {
        if (string.IsNullOrWhiteSpace(configured)) {
            configured = ".intelligencex/hotspots.json";
        }
        var trimmed = configured.Trim();
        return Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(workspace, trimmed);
    }

    private static string FormatLocation(AnalysisFinding finding) {
        if (finding.Line > 0) {
            return $"{finding.Path}:{finding.Line}";
        }
        return finding.Path;
    }

    private static string TrimMessage(string? message) {
        if (string.IsNullOrWhiteSpace(message)) {
            return string.Empty;
        }
        var trimmed = message.Trim();
        return trimmed.Length <= 220 ? trimmed : trimmed.Substring(0, 220) + "...";
    }

    private static AnalysisCatalog? TryLoadCatalog(string workspace) {
        try {
            return AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        } catch {
            return null;
        }
    }

    private static string? TryGetRelativePathWithinWorkspace(string workspace, string path) {
        try {
            var fullWorkspace = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            return Path.GetRelativePath(fullWorkspace, fullPath);
        } catch {
            return null;
        }
    }

    private static string ResolveWorkspaceRoot() {
        var current = Environment.CurrentDirectory;
        for (var i = 0; i < 12; i++) {
            var rulesDir = Path.Combine(current, "Analysis", "Catalog", "rules");
            var packsDir = Path.Combine(current, "Analysis", "Packs");
            if (Directory.Exists(rulesDir) && Directory.Exists(packsDir)) {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent is null) {
                break;
            }
            current = parent.FullName;
        }
        return Environment.CurrentDirectory;
    }
}

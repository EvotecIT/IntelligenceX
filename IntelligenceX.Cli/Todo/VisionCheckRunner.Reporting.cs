using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Cli.Todo;

internal static partial class VisionCheckRunner {
    private static List<PullRequestCandidate> LoadCandidates(string indexPath) {
        if (!File.Exists(indexPath)) {
            throw new FileNotFoundException($"Triage index not found: {indexPath}");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
        var root = doc.RootElement;
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array) {
            return new List<PullRequestCandidate>();
        }

        var candidates = new List<PullRequestCandidate>();
        foreach (var item in items.EnumerateArray()) {
            var kind = ReadString(item, "kind");
            if (!kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var id = ReadString(item, "id");
            var number = ReadInt(item, "number");
            var title = ReadString(item, "title");
            var url = ReadString(item, "url");
            var score = ReadNullableDouble(item, "score");
            var labels = ReadStringArray(item, "labels");
            if (string.IsNullOrWhiteSpace(id) || number <= 0) {
                continue;
            }
            candidates.Add(new PullRequestCandidate(id, number, title, url, labels, score));
        }
        return candidates;
    }

    internal static VisionAssessment EvaluateAlignment(PullRequestCandidate candidate, VisionSignals signals) {
        var tokens = TriageIndexRunner.Tokenize($"{candidate.Title} {string.Join(' ', candidate.Labels)}");
        var inMatches = tokens
            .Where(token => signals.InScopeTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var outMatches = tokens
            .Where(token => signals.OutOfScopeTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitAcceptMatches = tokens
            .Where(token => signals.ExplicitAcceptTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitRejectMatches = tokens
            .Where(token => signals.ExplicitRejectTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitReviewMatches = tokens
            .Where(token => signals.ExplicitReviewTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string classification;
        double confidence;
        string reason;
        if (explicitRejectMatches.Count > 0 && explicitAcceptMatches.Count == 0) {
            classification = "likely-out-of-scope";
            confidence = Math.Min(0.99, 0.80 + (explicitRejectMatches.Count * 0.06));
            reason = $"Explicit reject-policy matches: {string.Join(", ", explicitRejectMatches.Take(5))}.";
        } else if (explicitAcceptMatches.Count > 0 && explicitRejectMatches.Count == 0 && explicitReviewMatches.Count == 0) {
            classification = "aligned";
            confidence = Math.Min(0.98, 0.78 + (explicitAcceptMatches.Count * 0.05));
            reason = $"Explicit accept-policy matches: {string.Join(", ", explicitAcceptMatches.Take(5))}.";
        } else if (explicitAcceptMatches.Count > 0 && explicitRejectMatches.Count > 0) {
            classification = "needs-human-review";
            confidence = 0.66;
            reason = $"Conflicting explicit policy matches (accept: {string.Join(", ", explicitAcceptMatches.Take(3))}; reject: {string.Join(", ", explicitRejectMatches.Take(3))}).";
        } else if (explicitReviewMatches.Count > 0) {
            classification = "needs-human-review";
            confidence = Math.Min(0.80, 0.60 + (explicitReviewMatches.Count * 0.05));
            reason = $"Explicit human-review policy matches: {string.Join(", ", explicitReviewMatches.Take(5))}.";
        } else if (outMatches.Count >= 2 && inMatches.Count == 0) {
            classification = "likely-out-of-scope";
            confidence = Math.Min(0.98, 0.65 + (outMatches.Count * 0.10));
            reason = $"Out-of-scope token matches: {string.Join(", ", outMatches.Take(5))}.";
        } else if (inMatches.Count >= 2 && outMatches.Count == 0) {
            classification = "aligned";
            confidence = Math.Min(0.95, 0.60 + (inMatches.Count * 0.08));
            reason = $"In-scope token matches: {string.Join(", ", inMatches.Take(5))}.";
        } else if (outMatches.Count > 0 && inMatches.Count > 0) {
            classification = "needs-human-review";
            confidence = 0.55;
            reason = $"Mixed scope signals (in: {string.Join(", ", inMatches.Take(3))}; out: {string.Join(", ", outMatches.Take(3))}).";
        } else {
            classification = "needs-human-review";
            confidence = 0.40;
            reason = "No strong scope evidence in PR title/labels.";
        }

        return new VisionAssessment(
            candidate.Id,
            candidate.Number,
            candidate.Title,
            candidate.Url,
            classification,
            Math.Round(confidence, 2, MidpointRounding.AwayFromZero),
            candidate.Score,
            inMatches,
            outMatches,
            explicitAcceptMatches,
            explicitRejectMatches,
            explicitReviewMatches,
            reason
        );
    }

    private static int ClassificationRank(string classification) {
        return classification switch {
            "likely-out-of-scope" => 0,
            "needs-human-review" => 1,
            _ => 2
        };
    }

    private static string BuildSummaryMarkdown(
        Options options,
        IReadOnlyList<VisionAssessment> assessments,
        VisionContract contract,
        IReadOnlyList<VisionAssessment> highConfidenceLikelyOutOfScope) {
        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Vision Check");
        sb.AppendLine();
        sb.AppendLine($"- Vision file: `{options.VisionPath}`");
        sb.AppendLine($"- Repo: `{options.Repo}`");
        sb.AppendLine($"- PRs evaluated: {assessments.Count}");
        sb.AppendLine();
        sb.AppendLine("## Vision Contract");
        sb.AppendLine();
        sb.AppendLine($"- required sections status: {(contract.MissingSections.Count == 0 ? "ok" : "missing sections detected")}");
        sb.AppendLine($"- goals bullets: {contract.GoalsBullets}");
        sb.AppendLine($"- non-goals bullets: {contract.NonGoalsBullets}");
        sb.AppendLine($"- in-scope bullets: {contract.InScopeBullets}");
        sb.AppendLine($"- out-of-scope bullets: {contract.OutOfScopeBullets}");
        sb.AppendLine($"- decision-principles bullets: {contract.DecisionPrinciplesBullets}");
        sb.AppendLine($"- explicit policy bullets (aligned / out-of-scope / review): {contract.ExplicitAcceptBullets} / {contract.ExplicitRejectBullets} / {contract.ExplicitReviewBullets}");
        if (contract.Diagnostics.Count == 0) {
            sb.AppendLine("- contract diagnostics: none");
        } else {
            sb.AppendLine("- contract diagnostics:");
            foreach (var diagnostic in contract.Diagnostics.Take(options.MaxItems)) {
                sb.AppendLine($"  - {diagnostic}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Drift Gate");
        sb.AppendLine();
        sb.AppendLine($"- threshold: {options.DriftThreshold.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- high-confidence likely-out-of-scope: {highConfidenceLikelyOutOfScope.Count}");
        sb.AppendLine($"- fail-on-drift: {(options.FailOnDrift ? "enabled" : "disabled")}");
        sb.AppendLine();
        if (highConfidenceLikelyOutOfScope.Count > 0) {
            sb.AppendLine("### High-Confidence Drift Items");
            sb.AppendLine();
            foreach (var item in highConfidenceLikelyOutOfScope.Take(options.MaxItems)) {
                var scoreLabel = item.Score.HasValue ? $" | triage score {item.Score.Value.ToString("0.00", CultureInfo.InvariantCulture)}" : string.Empty;
                sb.AppendLine($"- #{item.Number} ({item.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}){scoreLabel}: [{item.Title}]({item.Url})");
                sb.AppendLine($"  - {item.Reason}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Classification Summary");
        sb.AppendLine();
        sb.AppendLine($"- likely-out-of-scope: {assessments.Count(item => item.Classification == "likely-out-of-scope")}");
        sb.AppendLine($"- needs-human-review: {assessments.Count(item => item.Classification == "needs-human-review")}");
        sb.AppendLine($"- aligned: {assessments.Count(item => item.Classification == "aligned")}");
        sb.AppendLine();

        AppendSection(sb, "Likely Out Of Scope", assessments.Where(item => item.Classification == "likely-out-of-scope").Take(options.MaxItems).ToList());
        AppendSection(sb, "Needs Human Review", assessments.Where(item => item.Classification == "needs-human-review").Take(options.MaxItems).ToList());
        AppendSection(sb, "Aligned", assessments.Where(item => item.Classification == "aligned").Take(options.MaxItems).ToList());

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<VisionAssessment> items) {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (items.Count == 0) {
            sb.AppendLine("None.");
            sb.AppendLine();
            return;
        }
        foreach (var item in items) {
            var scoreLabel = item.Score.HasValue ? $" | triage score {item.Score.Value.ToString("0.00", CultureInfo.InvariantCulture)}" : string.Empty;
            sb.AppendLine($"- #{item.Number} ({item.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}){scoreLabel}: [{item.Title}]({item.Url})");
            sb.AppendLine($"  - {item.Reason}");
        }
        sb.AppendLine();
    }

    private static void WriteText(string path, string content) {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static int ReadInt(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static double? ReadNullableDouble(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetDouble(out var value)) {
            return null;
        }
        return value;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        foreach (var item in prop.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.String) {
                continue;
            }
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) {
                list.Add(value);
            }
        }
        return list;
    }
}

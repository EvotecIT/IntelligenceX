using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace IntelligenceX.Cli.Setup;

internal static partial class SetupRunner {
    private readonly record struct VisionReviewDefaults(string? Intent, string? Strictness);

    private static VisionReviewDefaults ResolveReviewDefaultsFromVisionDocument(string? configuredPath, bool configuredPathSet) {
        if (configuredPathSet && string.IsNullOrWhiteSpace(configuredPath)) {
            throw new InvalidOperationException("--review-vision-path requires a non-empty path.");
        }

        var candidates = string.IsNullOrWhiteSpace(configuredPath)
            ? new[] { "VISION.md", "vision.md" }
            : new[] { configuredPath.Trim() };

        foreach (var candidate in candidates) {
            string fullPath;
            try {
                fullPath = Path.GetFullPath(candidate);
            } catch (Exception ex) {
                if (configuredPathSet) {
                    throw new InvalidOperationException(
                        $"--review-vision-path is invalid: {candidate}. {ex.Message}");
                }
                continue;
            }

            if (!File.Exists(fullPath)) {
                if (configuredPathSet) {
                    throw new InvalidOperationException($"--review-vision-path file not found: {candidate}");
                }
                continue;
            }

            string text;
            try {
                text = File.ReadAllText(fullPath);
            } catch (Exception ex) {
                if (configuredPathSet) {
                    throw new InvalidOperationException(
                        $"--review-vision-path could not be read: {candidate}. {ex.Message}");
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(text)) {
                if (configuredPathSet) {
                    throw new InvalidOperationException($"--review-vision-path is empty: {candidate}");
                }
                continue;
            }

            return InferReviewDefaultsFromVisionText(text);
        }

        return default;
    }

    private static VisionReviewDefaults InferReviewDefaultsFromVisionText(string visionMarkdown) {
        var lowered = visionMarkdown.ToLowerInvariant();
        var tokenMatches = Regex.Matches(lowered, "[a-z0-9][a-z0-9-]*", RegexOptions.CultureInvariant);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in tokenMatches) {
            var token = match.Value;
            if (counts.TryGetValue(token, out var existing)) {
                counts[token] = existing + 1;
            } else {
                counts[token] = 1;
            }
        }

        var securityScore = ScoreTheme(
            counts,
            lowered,
            new[] { "security", "auth", "authentication", "authorization", "secret", "secrets", "token", "tokens",
                "vulnerability", "vulnerabilities", "hardening", "injection", "compliance" },
            new[] { "access control", "least privilege", "secrets handling", "threat model", "owasp" });
        var performanceScore = ScoreTheme(
            counts,
            lowered,
            new[] { "performance", "latency", "throughput", "allocation", "allocations", "memory", "cpu", "optimize",
                "optimization", "optimizations", "scale", "scalability", "hotpath", "hotpaths" },
            new[] { "hot paths", "algorithmic complexity", "resource usage" });
        var maintainabilityScore = ScoreTheme(
            counts,
            lowered,
            new[] { "maintainability", "readability", "testability", "testing", "tests", "complexity", "duplication",
                "clarity", "ownership", "reliability", "mergeability" },
            new[] { "clear ownership", "delivery speed", "reduce duplicate", "review throughput", "triage quality" });

        string? intent = null;
        if (securityScore > performanceScore && securityScore > maintainabilityScore && securityScore > 0) {
            intent = "security";
        } else if (performanceScore > securityScore && performanceScore > maintainabilityScore && performanceScore > 0) {
            intent = "performance";
        } else if (maintainabilityScore > securityScore && maintainabilityScore > performanceScore &&
                   maintainabilityScore > 0) {
            intent = "maintainability";
        }

        var strictScore = ScoreTheme(
            counts,
            lowered,
            new[] { "strict", "blocking", "blocker", "enforce", "enforced", "mandatory", "must", "required",
                "non-negotiable", "gate" },
            new[] { "required checks", "fail on", "hard gate", "non negotiable" });
        var lenientScore = ScoreTheme(
            counts,
            lowered,
            new[] { "advisory", "optional", "suggestion", "suggestions", "non-blocking", "best", "effort" },
            new[] { "best effort", "non blocking", "guidance only" });

        string? strictness = null;
        if (strictScore >= lenientScore + 2) {
            strictness = "strict";
        } else if (lenientScore >= strictScore + 2) {
            strictness = "balanced";
        } else if (string.Equals(intent, "security", StringComparison.Ordinal) && strictScore > lenientScore &&
                   strictScore > 0) {
            strictness = "strict";
        }

        return new VisionReviewDefaults(intent, strictness);
    }

    private static int ScoreTheme(
        IReadOnlyDictionary<string, int> tokenCounts,
        string loweredText,
        IReadOnlyList<string> tokens,
        IReadOnlyList<string> phrases) {
        var score = 0;
        foreach (var token in tokens) {
            if (tokenCounts.TryGetValue(token, out var count)) {
                score += count;
            }
        }
        foreach (var phrase in phrases) {
            if (loweredText.Contains(phrase, StringComparison.Ordinal)) {
                score += 2;
            }
        }
        return score;
    }
}

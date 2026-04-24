using System;
using System.Collections.Generic;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Shared helpers for OpenAI/Codex model identifiers used across IntelligenceX.
/// </summary>
public static class OpenAIModelCatalog {
    /// <summary>
    /// Default OpenAI model for new IntelligenceX sessions and reviewer runs.
    /// </summary>
    public const string DefaultModel = "gpt-5.5";

    private static readonly string[] BaselineFallbackModels = {
        DefaultModel,
        "gpt-5.4",
        "gpt-5.4-codex",
        "gpt-5-mini",
        "gpt-5-nano",
        "gpt-5.3",
        "gpt-5.3-codex",
        "gpt-5.2",
        "gpt-5.2-codex",
        "gpt-5.1",
        "gpt-5.1-codex"
    };

    /// <summary>
    /// Normalizes provider-prefixed model identifiers while preserving known mode suffixes such as <c>/fast</c>.
    /// </summary>
    public static string NormalizeModelId(string? model, string? fallback = null) {
        var value = string.IsNullOrWhiteSpace(model) ? fallback : model;
        if (string.IsNullOrWhiteSpace(value)) {
            return DefaultModel;
        }

        var trimmed = value!.Trim();
        var rawParts = trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var parts = new List<string>(rawParts.Length);
        for (var i = 0; i < rawParts.Length; i++) {
            var part = rawParts[i].Trim();
            if (part.Length > 0) {
                parts.Add(part);
            }
        }

        if (parts.Count <= 1) {
            return trimmed;
        }

        var lastIndex = parts.Count - 1;
        if (IsModeToken(parts[lastIndex]) && parts.Count >= 2) {
            return parts[lastIndex - 1] + "/" + parts[lastIndex];
        }

        return parts[lastIndex];
    }

    internal static IReadOnlyList<string> GetBaselineFallbackModels() => BaselineFallbackModels;

    internal static int CompareChatGptFallbackPriority(string currentModel, string left, string right) {
        var leftScore = GetChatGptFallbackScore(currentModel, left);
        var rightScore = GetChatGptFallbackScore(currentModel, right);
        if (leftScore != rightScore) {
            return rightScore.CompareTo(leftScore);
        }
        return string.CompareOrdinal(left, right);
    }

    /// <summary>
    /// Classifies usage events into stable surface buckets, including fast-tier Codex usage when exposed by telemetry.
    /// </summary>
    /// <param name="surface">Raw product surface identifier.</param>
    /// <param name="processingTier">Optional processing tier or service tier identifier.</param>
    /// <returns>A normalized surface bucket name.</returns>
    public static string ClassifyProductSurface(string? surface, string? processingTier = null) {
        var normalizedSurface = string.IsNullOrWhiteSpace(surface) ? string.Empty : surface!.Trim().ToLowerInvariant();
        var normalizedTier = string.IsNullOrWhiteSpace(processingTier) ? string.Empty : processingTier!.Trim().ToLowerInvariant();

        var isFast = ContainsFastQualifier(normalizedSurface) ||
                     normalizedTier.IndexOf("fast", StringComparison.Ordinal) >= 0 ||
                     normalizedTier.IndexOf("priority", StringComparison.Ordinal) >= 0;
        var isSpark = normalizedSurface.IndexOf("spark", StringComparison.Ordinal) >= 0;
        var isCodex = normalizedSurface.IndexOf("codex", StringComparison.Ordinal) >= 0;

        if (isSpark) {
            return isFast ? "spark-fast" : "spark";
        }
        if (isCodex) {
            return isFast ? "codex-fast" : "codex";
        }
        if (isFast) {
            return "fast";
        }
        return string.IsNullOrWhiteSpace(normalizedSurface) ? "unknown" : normalizedSurface;
    }

    private static int GetChatGptFallbackScore(string currentModel, string candidate) {
        var current = NormalizeModelId(currentModel, DefaultModel).ToLowerInvariant();
        var normalized = NormalizeModelId(candidate, DefaultModel).ToLowerInvariant();

        var score = 0;
        var minor = TryGetGpt5MinorVersion(normalized);
        if (minor.HasValue) {
            score += 100 + (minor.Value * 10);
        }

        if (normalized.IndexOf("codex", StringComparison.Ordinal) >= 0) {
            score += 30;
        }

        if (HasSameQualifier(current, normalized,
                delegate(string value) { return value.IndexOf("codex", StringComparison.Ordinal) >= 0; })) {
            score += 8;
        }
        score += ScoreModeAffinity(
            current,
            normalized,
            delegate(string value) { return value.IndexOf("spark", StringComparison.Ordinal) >= 0; },
            reward: 14,
            penalty: 8);
        score += ScoreModeAffinity(
            current,
            normalized,
            ContainsFastQualifier,
            reward: 10,
            penalty: 6);

        return score;
    }

    private static int ScoreModeAffinity(string current, string candidate, Func<string, bool> detector, int reward, int penalty) {
        var currentHasMode = detector(current);
        var candidateHasMode = detector(candidate);
        if (currentHasMode == candidateHasMode) {
            return reward;
        }
        return candidateHasMode ? -penalty : 0;
    }

    private static bool HasSameQualifier(string current, string candidate, Func<string, bool> detector) {
        return detector(current) == detector(candidate);
    }

    private static bool ContainsFastQualifier(string value) {
        return value.IndexOf("fast", StringComparison.Ordinal) >= 0;
    }

    private static int? TryGetGpt5MinorVersion(string value) {
        var index = value.IndexOf("gpt-5", StringComparison.Ordinal);
        if (index < 0) {
            return null;
        }

        index += "gpt-5".Length;
        if (index >= value.Length || value[index] != '.') {
            return 0;
        }

        index++;
        var start = index;
        while (index < value.Length && char.IsDigit(value[index])) {
            index++;
        }

        if (index == start) {
            return 0;
        }

        return int.TryParse(value.Substring(start, index - start), out var minor)
            ? minor
            : null;
    }

    private static bool IsModeToken(string value) {
        return value.Equals("fast", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("spark", StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Reviewer;

internal static class ReviewHistoryMarker {
    private const string MarkerPrefix = "<!-- intelligencex:history:v1 ";
    private const string MarkerSuffix = " -->";
    private const string Schema = "intelligencex.review.history.v1";
    private const int StickyCommentSizeWarningThreshold = 60000;

    public static string AppendOrReplace(string commentBody, ReviewHistorySnapshot snapshot, PullRequestContext context,
        ReviewSettings settings) {
        var cleaned = Remove(commentBody);
        if (!settings.History.Enabled) {
            return cleaned.TrimEnd();
        }
        if (settings.History.MaxRounds <= 0) {
            return cleaned.TrimEnd();
        }

        var currentRound = ReviewHistoryBuilder.BuildSummaryRound(cleaned, null, context.HeadSha, settings, 0);
        if (currentRound is null) {
            return cleaned.TrimEnd();
        }

        var rounds = new List<ReviewHistoryRound>();
        var priorRounds = snapshot.Rounds;
        var startIndex = Math.Max(0, priorRounds.Count - settings.History.MaxRounds);
        foreach (var round in priorRounds.Skip(startIndex)) {
            rounds.Add(CloneRound(round, rounds.Count + 1, context.HeadSha));
        }

        if (rounds.Count > 0 &&
            !string.IsNullOrWhiteSpace(currentRound.ReviewedSha) &&
            string.Equals(rounds[^1].ReviewedSha, currentRound.ReviewedSha, StringComparison.OrdinalIgnoreCase)) {
            rounds[^1] = CloneRound(currentRound, rounds.Count, context.HeadSha);
        } else {
            rounds.Add(CloneRound(currentRound, rounds.Count + 1, context.HeadSha));
        }

        rounds = KeepNewestRounds(rounds, settings.History.MaxRounds, context.HeadSha);

        var payload = new {
            schema = Schema,
            generatedAtUtc = DateTimeOffset.UtcNow,
            repository = context.RepoFullName,
            pullRequest = context.Number,
            headSha = context.HeadSha,
            rounds = rounds.Select(round => new {
                sequence = round.Sequence,
                source = round.Source,
                reviewedSha = round.ReviewedSha,
                hasMergeBlockers = round.HasMergeBlockers,
                mergeBlockerStatus = round.MergeBlockerStatus,
                recommendation = round.Recommendation,
                positiveHighlights = round.PositiveHighlights.ToArray(),
                riskNotes = round.RiskNotes.ToArray(),
                followUps = round.FollowUps.ToArray(),
                findingsHitLimit = round.FindingsHitLimit,
                findingsParseIncomplete = round.FindingsParseIncomplete,
                findings = round.Findings.Select(finding => new {
                    fingerprint = finding.Fingerprint,
                    section = finding.Section,
                    text = finding.Text,
                    status = finding.Status
                }).ToArray()
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(payload);
        var encoded = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var result = $"{cleaned.TrimEnd()}\n\n{MarkerPrefix}{encoded}{MarkerSuffix}";
        if (settings.Diagnostics && result.Length >= StickyCommentSizeWarningThreshold) {
            Console.Error.WriteLine(
                $"Review history marker produced a large sticky comment ({result.Length} chars). Reduce review.history.maxRounds or review.history.maxItems if GitHub rejects the update.");
        }

        return result;
    }

    public static bool TryReadRounds(string? body, string? currentHeadSha, ReviewSettings settings,
        out IReadOnlyList<ReviewHistoryRound> rounds) {
        rounds = Array.Empty<ReviewHistoryRound>();
        if (string.IsNullOrWhiteSpace(body) || !TryExtractEncodedPayload(body!, out var encoded)) {
            return false;
        }

        try {
            var json = Encoding.UTF8.GetString(Base64UrlDecode(encoded));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("schema", out var schemaElement) ||
                !string.Equals(schemaElement.GetString(), Schema, StringComparison.Ordinal)) {
                return false;
            }
            if (!root.TryGetProperty("rounds", out var roundsElement) ||
                roundsElement.ValueKind != global::System.Text.Json.JsonValueKind.Array) {
                return false;
            }

            var parsed = new List<ReviewHistoryRound>();
            foreach (var roundElement in roundsElement.EnumerateArray()) {
                parsed.Add(ReadRound(roundElement, currentHeadSha, parsed.Count + 1));
            }

            parsed = KeepNewestRounds(parsed, settings.History.MaxRounds, currentHeadSha);
            rounds = parsed;
            return parsed.Count > 0;
        } catch {
            rounds = Array.Empty<ReviewHistoryRound>();
            return false;
        }
    }

    public static string Remove(string body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return body;
        }

        var result = body;
        while (true) {
            var start = result.IndexOf(MarkerPrefix, StringComparison.Ordinal);
            if (start < 0) {
                return result.TrimEnd();
            }

            var end = result.IndexOf(MarkerSuffix, start + MarkerPrefix.Length, StringComparison.Ordinal);
            if (end < 0) {
                return result.Substring(0, start).TrimEnd();
            }

            result = (result.Substring(0, start) + result.Substring(end + MarkerSuffix.Length)).TrimEnd();
        }
    }

    private static bool TryExtractEncodedPayload(string body, out string encoded) {
        encoded = string.Empty;
        var start = body.IndexOf(MarkerPrefix, StringComparison.Ordinal);
        if (start < 0) {
            return false;
        }

        start += MarkerPrefix.Length;
        var end = body.IndexOf(MarkerSuffix, start, StringComparison.Ordinal);
        if (end < 0 || end <= start) {
            return false;
        }

        encoded = body.Substring(start, end - start).Trim();
        return encoded.Length > 0;
    }

    private static ReviewHistoryRound ReadRound(JsonElement element, string? currentHeadSha, int sequence) {
        var reviewedSha = ReadString(element, "reviewedSha");
        var sameHeadAsCurrent = IsSameHead(reviewedSha, currentHeadSha);
        return new ReviewHistoryRound {
            Sequence = sequence,
            Source = ReadString(element, "source", "intelligencex"),
            ReviewedSha = reviewedSha,
            SameHeadAsCurrent = sameHeadAsCurrent,
            HasMergeBlockers = ReadBoolean(element, "hasMergeBlockers"),
            MergeBlockerStatus = ReadString(element, "mergeBlockerStatus"),
            Recommendation = ReadString(element, "recommendation"),
            PositiveHighlights = ReadStringArray(element, "positiveHighlights"),
            RiskNotes = ReadStringArray(element, "riskNotes"),
            FollowUps = ReadStringArray(element, "followUps"),
            FindingsHitLimit = ReadBoolean(element, "findingsHitLimit"),
            FindingsParseIncomplete = ReadBoolean(element, "findingsParseIncomplete"),
            Findings = ReadFindings(element)
        };
    }

    private static IReadOnlyList<ReviewHistoryFinding> ReadFindings(JsonElement element) {
        if (!element.TryGetProperty("findings", out var findingsElement) ||
            findingsElement.ValueKind != global::System.Text.Json.JsonValueKind.Array) {
            return Array.Empty<ReviewHistoryFinding>();
        }

        var findings = new List<ReviewHistoryFinding>();
        foreach (var findingElement in findingsElement.EnumerateArray()) {
            findings.Add(new ReviewHistoryFinding {
                Fingerprint = ReadString(findingElement, "fingerprint"),
                Section = ReadString(findingElement, "section"),
                Text = ReadString(findingElement, "text"),
                Status = ReadString(findingElement, "status", "open")
            });
        }
        return findings;
    }

    private static ReviewHistoryRound CloneRound(ReviewHistoryRound round, int sequence, string? currentHeadSha) {
        return new ReviewHistoryRound {
            Sequence = sequence,
            Source = round.Source,
            SummaryCommentId = round.SummaryCommentId,
            ReviewedSha = round.ReviewedSha,
            SameHeadAsCurrent = IsSameHead(round.ReviewedSha, currentHeadSha),
            HasMergeBlockers = round.HasMergeBlockers,
            MergeBlockerStatus = round.MergeBlockerStatus,
            Recommendation = round.Recommendation,
            PositiveHighlights = round.PositiveHighlights,
            RiskNotes = round.RiskNotes,
            FollowUps = round.FollowUps,
            FindingsHitLimit = round.FindingsHitLimit,
            FindingsParseIncomplete = round.FindingsParseIncomplete,
            Findings = round.Findings
        };
    }

    private static List<ReviewHistoryRound> KeepNewestRounds(IReadOnlyList<ReviewHistoryRound> rounds, int maxRounds,
        string? currentHeadSha) {
        if (maxRounds <= 0 || rounds.Count <= maxRounds) {
            return rounds
                .Select((round, index) => CloneRound(round, index + 1, currentHeadSha))
                .ToList();
        }

        var firstNewestIndex = Math.Max(0, rounds.Count - maxRounds);
        return rounds
            .Skip(firstNewestIndex)
            .Select((round, index) => CloneRound(round, index + 1, currentHeadSha))
            .ToList();
    }

    private static bool IsSameHead(string? reviewedSha, string? currentHeadSha) =>
        !string.IsNullOrWhiteSpace(reviewedSha) &&
        !string.IsNullOrWhiteSpace(currentHeadSha) &&
        string.Equals(currentHeadSha.Trim(), reviewedSha.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonElement element, string propertyName, string fallback = "") {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == global::System.Text.Json.JsonValueKind.String
            ? value.GetString()?.Trim() ?? fallback
            : fallback;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != global::System.Text.Json.JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray()) {
            if (item.ValueKind != global::System.Text.Json.JsonValueKind.String) {
                continue;
            }

            var text = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) {
                items.Add(text!);
            }
        }

        return items;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == global::System.Text.Json.JsonValueKind.True;
    }

    private static string Base64UrlEncode(byte[] bytes) {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value) {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4) {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        return Convert.FromBase64String(padded);
    }
}

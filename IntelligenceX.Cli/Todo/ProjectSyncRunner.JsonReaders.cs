using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class ProjectSyncRunner {
    private static IReadOnlyList<RelatedIssueCandidate> ParseRelatedIssueCandidates(JsonElement item) {
        if (!TryGetProperty(item, "relatedIssues", out var relatedProp) || relatedProp.ValueKind != JsonValueKind.Array) {
            return Array.Empty<RelatedIssueCandidate>();
        }

        var results = new List<RelatedIssueCandidate>();
        foreach (var related in relatedProp.EnumerateArray()) {
            var number = ReadInt(related, "number");
            var url = ReadString(related, "url");
            var confidence = ReadNullableDouble(related, "confidence");
            var reason = ReadNullableString(related, "reason") ?? string.Empty;
            if (number <= 0 || string.IsNullOrWhiteSpace(url) || !confidence.HasValue) {
                continue;
            }

            results.Add(new RelatedIssueCandidate(
                Number: number,
                Url: url,
                Confidence: Math.Round(Math.Clamp(confidence.Value, 0.0, 1.0), 4, MidpointRounding.AwayFromZero),
                Reason: reason
            ));
        }

        return results
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(10)
            .ToList();
    }

    private static IReadOnlyList<RelatedPullRequestCandidate> ParseRelatedPullRequestCandidates(JsonElement item) {
        if (!TryGetProperty(item, "relatedPullRequests", out var relatedProp) || relatedProp.ValueKind != JsonValueKind.Array) {
            return Array.Empty<RelatedPullRequestCandidate>();
        }

        var results = new List<RelatedPullRequestCandidate>();
        foreach (var related in relatedProp.EnumerateArray()) {
            var number = ReadInt(related, "number");
            var url = ReadString(related, "url");
            var confidence = ReadNullableDouble(related, "confidence");
            var reason = ReadNullableString(related, "reason") ?? string.Empty;
            if (number <= 0 || string.IsNullOrWhiteSpace(url) || !confidence.HasValue) {
                continue;
            }

            results.Add(new RelatedPullRequestCandidate(
                Number: number,
                Url: url,
                Confidence: Math.Round(Math.Clamp(confidence.Value, 0.0, 1.0), 4, MidpointRounding.AwayFromZero),
                Reason: reason
            ));
        }

        return results
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenBy(candidate => candidate.Number)
            .Take(10)
            .ToList();
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (obj.TryGetProperty(name, out value)) {
            return true;
        }

        foreach (var property in obj.EnumerateObject()) {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static string? ReadNullableStringCaseInsensitive(JsonElement obj, string name) {
        if (!TryGetPropertyCaseInsensitive(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.String) {
            return null;
        }
        return prop.GetString();
    }

    private static bool? ReadNullableBoolCaseInsensitive(JsonElement obj, string name) {
        if (!TryGetPropertyCaseInsensitive(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False) {
            return null;
        }
        return prop.GetBoolean();
    }

    private static double? ReadNullableDoubleCaseInsensitive(JsonElement obj, string name) {
        if (!TryGetPropertyCaseInsensitive(obj, name, out var prop)) {
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

    private static string? ReadNullableString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.String) {
            return null;
        }
        return prop.GetString();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var element in prop.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.String) {
                continue;
            }

            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value)) {
                values.Add(value);
            }
        }

        return values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, double> ReadStringDoubleMap(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Object) {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in prop.EnumerateObject()) {
            var key = property.Name?.Trim();
            if (string.IsNullOrWhiteSpace(key)) {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Number ||
                !property.Value.TryGetDouble(out var confidence)) {
                continue;
            }

            values[key] = Math.Clamp(confidence, 0, 1);
        }

        return values;
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

    private static (string Kind, int Number) ParseKindAndNumberFromUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return ("pull_request", 0);
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 4 &&
                int.TryParse(segments[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) &&
                number > 0) {
                var kindSegment = segments[^2];
                if (kindSegment.Equals("issues", StringComparison.OrdinalIgnoreCase)) {
                    return ("issue", number);
                }
                if (kindSegment.Equals("pull", StringComparison.OrdinalIgnoreCase) ||
                    kindSegment.Equals("pulls", StringComparison.OrdinalIgnoreCase)) {
                    return ("pull_request", number);
                }
            }
        }

        return ("pull_request", 0);
    }
}

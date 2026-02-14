using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App;

internal sealed class AssistantMemoryUpdate {
    public List<AssistantMemoryUpsert> Upserts { get; } = new();
    public List<string> DeleteFacts { get; } = new();
}

internal sealed class AssistantMemoryUpsert {
    public string Fact { get; set; } = string.Empty;
    public int Weight { get; set; } = 3;
    public string[] Tags { get; set; } = Array.Empty<string>();
}

internal static class MemoryModelProtocol {
    private static readonly Regex MemoryEnvelopeRegex =
        new(@"```ix_memory\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryExtractLastMemoryUpdate(string? assistantText, out AssistantMemoryUpdate update, out string cleanedText) {
        update = new AssistantMemoryUpdate();
        var input = (assistantText ?? string.Empty).Trim();
        cleanedText = input;
        if (input.Length == 0) {
            return false;
        }

        var matches = MemoryEnvelopeRegex.Matches(input);
        if (matches.Count == 0) {
            return false;
        }

        var match = matches[matches.Count - 1];
        if (match.Groups.Count < 2) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            var root = doc.RootElement;
            if (root.TryGetProperty("upserts", out var upserts) && upserts.ValueKind == JsonValueKind.Array) {
                foreach (var item in upserts.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.Object) {
                        continue;
                    }

                    var fact = item.TryGetProperty("fact", out var factNode) && factNode.ValueKind == JsonValueKind.String
                        ? factNode.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(fact)) {
                        continue;
                    }

                    var weight = 3;
                    if (item.TryGetProperty("weight", out var weightNode)
                        && weightNode.ValueKind == JsonValueKind.Number
                        && weightNode.TryGetInt32(out var parsedWeight)) {
                        weight = Math.Clamp(parsedWeight, 1, 5);
                    }

                    var tags = Array.Empty<string>();
                    if (item.TryGetProperty("tags", out var tagsNode) && tagsNode.ValueKind == JsonValueKind.Array) {
                        var tagList = new List<string>();
                        foreach (var tag in tagsNode.EnumerateArray()) {
                            if (tag.ValueKind != JsonValueKind.String) {
                                continue;
                            }

                            var normalizedTag = (tag.GetString() ?? string.Empty).Trim();
                            if (normalizedTag.Length > 0) {
                                tagList.Add(normalizedTag);
                            }
                        }

                        if (tagList.Count > 0) {
                            tags = tagList.ToArray();
                        }
                    }

                    update.Upserts.Add(new AssistantMemoryUpsert {
                        Fact = fact.Trim(),
                        Weight = weight,
                        Tags = tags
                    });
                }
            }

            if (root.TryGetProperty("deleteFacts", out var deletes) && deletes.ValueKind == JsonValueKind.Array) {
                foreach (var item in deletes.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String) {
                        continue;
                    }

                    var value = (item.GetString() ?? string.Empty).Trim();
                    if (value.Length > 0) {
                        update.DeleteFacts.Add(value);
                    }
                }
            }
        } catch {
            return false;
        }

        cleanedText = MemoryEnvelopeRegex.Replace(input, string.Empty).Trim();
        return update.Upserts.Count > 0 || update.DeleteFacts.Count > 0;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal static class ReviewFindingsBuilder {
    private const string FindingsMarker = "<!-- intelligencex:findings -->";
    private const string SchemaId = "intelligencex.findings.v1";
    private const int MaxMessageChars = 300;

    public static string Build(IReadOnlyList<InlineReviewComment> comments) {
        if (comments is null || comments.Count == 0) {
            return string.Empty;
        }

        var items = new JsonArray();
        foreach (var comment in comments) {
            if (comment is null) {
                continue;
            }
            if (string.IsNullOrWhiteSpace(comment.Path) || comment.Line <= 0) {
                continue;
            }
            var message = TrimMessage(comment.Body);
            var item = new JsonObject()
                .Add("path", comment.Path)
                .Add("line", comment.Line)
                .Add("severity", "unknown")
                .Add("message", message);
            items.Add(item);
        }

        if (items.Count == 0) {
            return string.Empty;
        }

        var payload = new JsonObject()
            .Add("schema", SchemaId)
            .Add("items", items);
        var json = JsonLite.Serialize(payload);

        var sb = new StringBuilder();
        sb.AppendLine(FindingsMarker);
        sb.AppendLine("```json");
        sb.AppendLine(json);
        sb.AppendLine("```");
        return sb.ToString().TrimEnd();
    }

    private static string TrimMessage(string? body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return string.Empty;
        }
        var text = body.Trim();
        if (text.Length <= MaxMessageChars) {
            return text;
        }
        return text.Substring(0, MaxMessageChars) + "...";
    }
}

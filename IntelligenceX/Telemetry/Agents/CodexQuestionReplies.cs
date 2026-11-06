using System;
using System.Collections.Generic;
using System.Text.Json;
using static IntelligenceX.Telemetry.Agents.AgentSessionLogReader;

namespace IntelligenceX.Telemetry.Agents;

// Desktop question identity is ["request_user_input_async", call_id, question_index].
// A tool acknowledgement is not an answer; all questions in that call must be answered.
internal static class CodexQuestionReplies {
    internal const string Opening = "<send_user_message_question_reply>";
    internal const string Closing = "</send_user_message_question_reply>";

    internal static int QuestionCount(JsonElement item) {
        var projected = Object(item, "questionCount");
        if (projected.ValueKind == JsonValueKind.Number && projected.TryGetInt32(out var count)) return count;
        var questions = Object(item, "questions");
        return questions.ValueKind == JsonValueKind.Array ? questions.GetArrayLength() : 0;
    }

    internal static IEnumerable<(string CallId, int Index)> Read(JsonElement payload) {
        var content = Object(payload, "content");
        if (content.ValueKind != JsonValueKind.Array) yield break;
        foreach (var block in content.EnumerateArray()) {
            var ids = Object(block, "questionReplyIds");
            if (ids.ValueKind == JsonValueKind.Array) {
                foreach (var id in ids.EnumerateArray()) if (TryIdentity(id.GetString(), out var reply)) yield return reply;
            } else {
                var text = Text(block, "text")?.Trim();
                if (text is null || !text.StartsWith(Opening, StringComparison.Ordinal) || !text.EndsWith(Closing, StringComparison.Ordinal)) continue;
                using var body = TryParse(text.Substring(Opening.Length, text.Length - Opening.Length - Closing.Length));
                if (body?.RootElement.ValueKind != JsonValueKind.Array) continue;
                foreach (var answer in body.RootElement.EnumerateArray()) {
                    if (TryIdentity(Text(answer, "questionItemId"), out var reply)) yield return reply;
                }
            }
        }
    }

    private static bool TryIdentity(string? id, out (string CallId, int Index) reply) {
        reply = default;
        using var document = id is null ? null : TryParse(id);
        if (document?.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != 3) return false;
        var parts = document.RootElement;
        if (parts[0].ValueKind != JsonValueKind.String || parts[0].GetString() != "request_user_input_async"
            || parts[1].ValueKind != JsonValueKind.String || parts[2].ValueKind != JsonValueKind.Number
            || !parts[2].TryGetInt32(out var index)) return false;
        reply = (parts[1].GetString()!, index);
        return true;
    }

    private static JsonDocument? TryParse(string value) {
        try { return JsonDocument.Parse(value); } catch (JsonException) { return null; }
    }
}

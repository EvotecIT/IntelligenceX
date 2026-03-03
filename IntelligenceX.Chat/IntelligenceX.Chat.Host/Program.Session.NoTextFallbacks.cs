using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private sealed partial class ReplSession {
        private const int NoTextFallbackMaxBullets = 3;
        private const int NoTextFallbackSummaryMaxChars = 220;

        private static string BuildNoTextReplFallbackText(
            string assistantDraft,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            string? model,
            OpenAITransportKind transport,
            string? baseUrl) {
            var normalizedAssistantDraft = assistantDraft ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedAssistantDraft)) {
                return normalizedAssistantDraft;
            }

            if (TryBuildToolOutputNoTextFallback(toolCalls, toolOutputs, out var toolOutputFallback)) {
                return toolOutputFallback;
            }

            return BuildNoTextModelWarning(model, transport, baseUrl);
        }

        internal static string BuildNoTextReplFallbackTextForTesting(
            string assistantDraft,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            string? model,
            OpenAITransportKind transport,
            string? baseUrl) {
            return BuildNoTextReplFallbackText(assistantDraft, toolCalls, toolOutputs, model, transport, baseUrl);
        }

        private static bool TryBuildToolOutputNoTextFallback(
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            out string text) {
            text = string.Empty;
            if (toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            var toolNameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (toolCalls is not null) {
                for (var i = 0; i < toolCalls.Count; i++) {
                    var call = toolCalls[i];
                    if (call is null) {
                        continue;
                    }

                    var callId = (call.CallId ?? string.Empty).Trim();
                    var toolName = (call.Name ?? string.Empty).Trim();
                    if (callId.Length == 0 || toolName.Length == 0) {
                        continue;
                    }

                    toolNameByCallId[callId] = toolName;
                }
            }

            var bulletLines = new List<string>(Math.Min(NoTextFallbackMaxBullets, toolOutputs.Count));
            var usableOutputCount = 0;
            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                usableOutputCount++;
                if (bulletLines.Count >= NoTextFallbackMaxBullets) {
                    continue;
                }

                var summary = BuildToolOutputNoTextSummary(output.Output);
                if (summary.Length == 0) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                var toolName = toolNameByCallId.TryGetValue(callId, out var knownToolName)
                    ? knownToolName
                    : "tool";
                bulletLines.Add("- `" + toolName + "`: " + summary);
            }

            if (bulletLines.Count == 0) {
                return false;
            }

            var builder = new StringBuilder(512);
            builder.AppendLine("Recovered findings from executed tools (model returned no text):");
            for (var i = 0; i < bulletLines.Count; i++) {
                builder.AppendLine(bulletLines[i]);
            }

            var remainingCount = Math.Max(0, usableOutputCount - bulletLines.Count);
            if (remainingCount > 0) {
                builder.Append("... and ")
                    .Append(remainingCount.ToString())
                    .Append(" more tool output(s).");
            }

            text = builder.ToString().TrimEnd();
            return text.Length > 0;
        }

        private static string BuildToolOutputNoTextSummary(string rawOutput) {
            var raw = (rawOutput ?? string.Empty).Trim();
            if (raw.Length == 0) {
                return "completed successfully.";
            }

            if (TryExtractToolOutputSummaryFromJson(raw, out var parsedSummary)) {
                return TruncateNoTextSummary(parsedSummary);
            }

            if (LooksLikeJsonPayload(raw)) {
                return "returned structured output.";
            }

            return TruncateNoTextSummary(raw);
        }

        private static bool TryExtractToolOutputSummaryFromJson(string raw, out string summary) {
            summary = string.Empty;
            if (!LooksLikeJsonPayload(raw)) {
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) {
                    return false;
                }

                if (TryGetJsonString(root, "summary_markdown", out summary)
                    || TryGetJsonString(root, "summary", out summary)
                    || TryGetJsonString(root, "message", out summary)
                    || TryGetJsonString(root, "error", out summary)) {
                    return summary.Length > 0;
                }

                if (root.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array) {
                    summary = "returned " + rows.GetArrayLength().ToString() + " row(s).";
                    return true;
                }

                return false;
            } catch (JsonException) {
                return false;
            }
        }

        private static bool TryGetJsonString(JsonElement node, string key, out string value) {
            value = string.Empty;
            if (!node.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.String) {
                return false;
            }

            var candidate = (property.GetString() ?? string.Empty).Trim();
            if (candidate.Length == 0) {
                return false;
            }

            value = candidate;
            return true;
        }

        private static bool LooksLikeJsonPayload(string text) {
            var normalized = (text ?? string.Empty).TrimStart();
            return normalized.StartsWith("{", StringComparison.Ordinal) || normalized.StartsWith("[", StringComparison.Ordinal);
        }

        private static string TruncateNoTextSummary(string text) {
            var normalized = CollapseWhitespace((text ?? string.Empty).Trim());
            if (normalized.Length <= NoTextFallbackSummaryMaxChars) {
                return normalized;
            }

            return normalized.Substring(0, NoTextFallbackSummaryMaxChars - 3).TrimEnd() + "...";
        }

        private static string BuildNoTextModelWarning(string? model, OpenAITransportKind transport, string? baseUrl) {
            var normalizedModel = (model ?? string.Empty).Trim();
            if (normalizedModel.Length == 0) {
                normalizedModel = "unknown";
            }

            if (transport == OpenAITransportKind.CompatibleHttp) {
                var endpoint = string.IsNullOrWhiteSpace(baseUrl) ? "configured endpoint" : baseUrl!.Trim();
                return "[warning] No response text was produced by the runtime.\n\n"
                       + "Model: " + normalizedModel + "\n"
                       + "Endpoint: " + endpoint + "\n\n"
                       + "Try a different model, then run Refresh Models and retry.";
            }

            return "[warning] No response text was produced by the model.\n\n"
                   + "Model: " + normalizedModel + "\n\n"
                   + "Retry the turn, or choose a different model.";
        }
    }
}

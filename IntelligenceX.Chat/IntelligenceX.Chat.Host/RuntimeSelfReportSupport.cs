using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static class RuntimeSelfReportSupport {
    private static readonly string[] RuntimeCueWords = {
        "model",
        "runtime",
        "tool",
        "tools",
        "tooling",
        "pack",
        "packs",
        "plugin",
        "plugins",
        "transport"
    };

    internal static bool LooksLikeCompactRuntimeSelfReportQuestion(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0 || text.Length > 72 || !ContainsRuntimeSelfReportQuestionSignal(text)) {
            return false;
        }

        var tokens = CollectLetterDigitTokens(text, maxTokens: 8);
        if (tokens.Count == 0 || tokens.Count > 7) {
            return false;
        }

        return CountRuntimeCueMatches(tokens) > 0 && !LooksLikeConcreteOperationalQuestion(tokens);
    }

    internal static string BuildCompactRuntimeSelfReportInput(
        string userText,
        OpenAITransportKind transport,
        string? model,
        IReadOnlyList<ToolDefinition> toolDefinitions) {
        var normalizedUserText = (userText ?? string.Empty).Trim();
        var builder = new StringBuilder();
        builder.AppendLine("[Runtime self-report facts]");
        builder.AppendLine("ix:runtime-self-report:v1");
        builder.Append("active_model: ").AppendLine(string.IsNullOrWhiteSpace(model) ? "(provider default)" : model.Trim());
        builder.Append("transport: ").AppendLine(DescribeCompactTransport(transport));
        builder.Append("tooling_requested: ").AppendLine(ContainsToolingCue(normalizedUserText) ? "true" : "false");
        builder.Append("ad_tooling: ").AppendLine(HasDomainFamily(toolDefinitions, ToolSelectionMetadata.DomainIntentFamilyAd) ? "available" : "unavailable");
        builder.Append("public_domain_tooling: ").AppendLine(HasDomainFamily(toolDefinitions, ToolSelectionMetadata.DomainIntentFamilyPublic) ? "available" : "unavailable");
        builder.Append("eventlog_tooling: ").AppendLine(HasPack(toolDefinitions, "eventlog") ? "available" : "unavailable");
        builder.Append("filesystem_tooling: ").AppendLine(HasPack(toolDefinitions, "filesystem") || HasPack(toolDefinitions, "fs") ? "available" : "unavailable");
        builder.AppendLine("reply_shape: compact");
        builder.AppendLine("reply_rules:");
        builder.AppendLine("- Answer in 1-2 short human sentences.");
        builder.AppendLine("- Mention the exact active model when the user asks about model or runtime.");
        builder.AppendLine("- Mention tooling only if the user asked about tooling.");
        builder.AppendLine("- Do not use headings, bullet lists, inventories, or capability maps.");
        builder.AppendLine();
        builder.AppendLine("User request:");
        builder.Append(normalizedUserText);
        return builder.ToString();
    }

    private static string DescribeCompactTransport(OpenAITransportKind transport) {
        return transport switch {
            OpenAITransportKind.Native => "native",
            OpenAITransportKind.AppServer => "appserver",
            OpenAITransportKind.CompatibleHttp => "compatible-http",
            OpenAITransportKind.CopilotCli => "copilot-cli",
            _ => transport.ToString().Trim().ToLowerInvariant()
        };
    }

    private static bool HasPack(IReadOnlyList<ToolDefinition> toolDefinitions, string packId) {
        for (var i = 0; i < toolDefinitions.Count; i++) {
            if (ToolSelectionMetadata.TryResolvePackId(toolDefinitions[i], out var resolvedPackId)
                && string.Equals(resolvedPackId, packId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool HasDomainFamily(IReadOnlyList<ToolDefinition> toolDefinitions, string family) {
        for (var i = 0; i < toolDefinitions.Count; i++) {
            if (ToolSelectionMetadata.TryResolveDomainIntentFamily(toolDefinitions[i], out var resolvedFamily)
                && string.Equals(resolvedFamily, family, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsToolingCue(string text) {
        var tokens = CollectLetterDigitTokens(text, maxTokens: 12);
        return CountRuntimeCueMatches(tokens) > 1;
    }

    private static int CountRuntimeCueMatches(IReadOnlyList<string> tokens) {
        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            for (var j = 0; j < RuntimeCueWords.Length; j++) {
                if (string.Equals(tokens[i], RuntimeCueWords[j], StringComparison.OrdinalIgnoreCase)) {
                    matches++;
                    break;
                }
            }
        }

        return matches;
    }

    private static bool LooksLikeConcreteOperationalQuestion(IReadOnlyList<string> tokens) {
        if (tokens.Count < 4 || tokens[0].Length > 3 || tokens[1].Length > 3) {
            return false;
        }

        var concreteTailTokens = 0;
        for (var i = 2; i < tokens.Count; i++) {
            if (tokens[i].Length >= 4) {
                concreteTailTokens++;
                if (concreteTailTokens >= 2) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsRuntimeSelfReportQuestionSignal(string text) {
        return text.IndexOf('?') >= 0
               || text.IndexOf('？') >= 0
               || text.IndexOf('¿') >= 0
               || text.IndexOf('؟') >= 0;
    }

    private static List<string> CollectLetterDigitTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        var tokens = new List<string>(Math.Max(0, Math.Min(maxTokens, 8)));
        if (normalized.Length == 0 || maxTokens <= 0) {
            return tokens;
        }

        var start = -1;
        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsLetterOrDigit(normalized[i])) {
                if (start < 0) {
                    start = i;
                }
            } else if (start >= 0) {
                tokens.Add(normalized[start..i]);
                if (tokens.Count >= maxTokens) {
                    return tokens;
                }

                start = -1;
            }
        }

        if (start >= 0 && tokens.Count < maxTokens) {
            tokens.Add(normalized[start..]);
        }

        return tokens;
    }
}

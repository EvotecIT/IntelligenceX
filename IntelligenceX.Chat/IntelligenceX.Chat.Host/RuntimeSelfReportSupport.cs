using System;
using System.Collections.Generic;
using System.Text;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static class RuntimeSelfReportSupport {
    internal static bool LooksLikeCompactRuntimeSelfReportQuestion(string? userText) {
        return RuntimeSelfReportTurnClassifier.LooksLikeCompactRuntimeIntrospectionQuestion(userText);
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
        builder.Append("user_request_literal: ").AppendLine(EscapePromptLiteral(normalizedUserText));
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
        var normalized = (text ?? string.Empty).Trim();
        return normalized.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("pack", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("plugin", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string EscapePromptLiteral(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "\"\"";
        }

        var builder = new StringBuilder(normalized.Length + 8);
        builder.Append('"');
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            switch (ch) {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                default:
                    builder.Append(char.IsControl(ch) ? ' ' : ch);
                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static class RuntimeSelfReportSupport {
    internal static bool LooksLikeCompactRuntimeSelfReportQuestion(string? userText) {
        return LooksLikeCompactRuntimeSelfReportQuestion(RuntimeSelfReportTurnClassifier.Analyze(userText));
    }

    internal static bool LooksLikeCompactRuntimeSelfReportQuestion(
        RuntimeSelfReportTurnClassifier.RuntimeSelfReportTurnAnalysis runtimeSelfReportAnalysis) {
        return runtimeSelfReportAnalysis.CompactReply;
    }

    internal static string BuildCompactRuntimeSelfReportInput(
        string userText,
        OpenAITransportKind transport,
        string? model,
        IReadOnlyList<ToolDefinition> toolDefinitions) {
        return BuildCompactRuntimeSelfReportInput(
            RuntimeSelfReportTurnClassifier.Analyze(userText),
            transport,
            model,
            toolDefinitions);
    }

    internal static string BuildCompactRuntimeSelfReportInput(
        RuntimeSelfReportTurnClassifier.RuntimeSelfReportTurnAnalysis runtimeSelfReportAnalysis,
        OpenAITransportKind transport,
        string? model,
        IReadOnlyList<ToolDefinition> toolDefinitions) {
        var normalizedUserText = runtimeSelfReportAnalysis.UserRequestLiteral;
        var builder = new StringBuilder();
        builder.AppendLine("[Runtime self-report facts]");
        builder.AppendLine("ix:runtime-self-report:v1");
        builder.Append("active_model: ").AppendLine(string.IsNullOrWhiteSpace(model) ? "(provider default)" : model.Trim());
        builder.Append("transport: ").AppendLine(DescribeCompactTransport(transport));
        builder.Append("detection_source: ").AppendLine(DescribeDetectionSource(runtimeSelfReportAnalysis.DetectionSource));
        var modelRequested = runtimeSelfReportAnalysis.ModelRequested;
        builder.Append("model_requested: ").AppendLine(modelRequested ? "true" : "false");
        var toolingRequested = runtimeSelfReportAnalysis.ToolingRequested;
        builder.Append("tooling_requested: ").AppendLine(toolingRequested ? "true" : "false");
        if (ShouldIncludeCompactAvailabilityInventory(runtimeSelfReportAnalysis.DetectionSource)) {
            builder.Append("available_pack_ids: ").AppendLine(FormatCompactAvailability(CollectAvailablePackIds(toolDefinitions)));
            builder.Append("available_domain_families: ").AppendLine(FormatCompactAvailability(CollectAvailableDomainFamilies(toolDefinitions)));
        } else {
            builder.AppendLine("available_pack_ids: suppressed_for_lexical_fallback");
            builder.AppendLine("available_domain_families: suppressed_for_lexical_fallback");
        }
        builder.AppendLine("reply_shape: compact");
        builder.AppendLine("reply_rules:");
        builder.AppendLine("- Answer in 1-2 short human sentences.");
        builder.AppendLine(modelRequested
            ? "- Mention the exact active model when the user asked about model or runtime."
            : "- Do not mention the active model unless the user asked about model or runtime.");
        builder.AppendLine(toolingRequested
            ? "- Mention tooling because the user explicitly asked about tooling."
            : "- Do not mention tooling unless the user explicitly asked about tooling.");
        if (runtimeSelfReportAnalysis.DetectionSource == RuntimeSelfReportDetectionSource.LexicalFallback) {
            builder.AppendLine("- This request came from lightweight lexical fallback, so answer only the exact runtime or tooling facet already marked above.");
            builder.AppendLine("- Do not expand into extra capability detail, inventory breadth, or speculative explanations beyond the exact requested facet.");
            builder.AppendLine("- Treat pack or domain-family inventory as suppressed unless the user asks for deeper runtime provenance explicitly.");
        } else if (runtimeSelfReportAnalysis.DetectionSource == RuntimeSelfReportDetectionSource.StructuredDirective) {
            builder.AppendLine("- This request was explicitly marked as runtime self-report, so follow the structured scope above without reinterpreting the user text.");
        }

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

    private static string DescribeDetectionSource(RuntimeSelfReportDetectionSource detectionSource) {
        return detectionSource switch {
            RuntimeSelfReportDetectionSource.LexicalFallback => "lexical_fallback",
            RuntimeSelfReportDetectionSource.StructuredDirective => "structured_directive",
            _ => "none"
        };
    }

    private static bool ShouldIncludeCompactAvailabilityInventory(RuntimeSelfReportDetectionSource detectionSource) {
        return detectionSource != RuntimeSelfReportDetectionSource.LexicalFallback;
    }

    private static IReadOnlyList<string> CollectAvailablePackIds(IReadOnlyList<ToolDefinition> toolDefinitions) {
        var packIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolDefinitions.Count; i++) {
            if (ToolSelectionMetadata.TryResolvePackId(toolDefinitions[i], out var resolvedPackId)
                && !string.IsNullOrWhiteSpace(resolvedPackId)) {
                packIds.Add(resolvedPackId.Trim());
            }
        }

        return packIds
            .OrderBy(static packId => packId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> CollectAvailableDomainFamilies(IReadOnlyList<ToolDefinition> toolDefinitions) {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolDefinitions.Count; i++) {
            if (ToolSelectionMetadata.TryResolveDomainIntentFamily(toolDefinitions[i], out var resolvedFamily)
                && !string.IsNullOrWhiteSpace(resolvedFamily)) {
                families.Add(resolvedFamily.Trim());
            }
        }

        return families
            .OrderBy(static family => family, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string FormatCompactAvailability(IReadOnlyList<string> values) {
        return values.Count == 0 ? "(none)" : string.Join(", ", values);
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

using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.Abstractions;

/// <summary>
/// Structured directive describing a runtime self-report request without requiring downstream consumers
/// to infer intent from raw natural-language text.
/// </summary>
public static class RuntimeSelfReportDirective {
    /// <summary>
    /// Structured marker that identifies a runtime self-report directive block.
    /// </summary>
    public const string Marker = "ix:runtime-self-report:v1";

    private const string ReplyShapeKey = "reply_shape:";
    private const string DetectionSourceKey = "detection_source:";
    private const string ModelRequestedKey = "model_requested:";
    private const string ToolingRequestedKey = "tooling_requested:";
    private const string UserRequestLiteralKey = "user_request_literal:";

    /// <summary>
    /// Builds a compact structured directive describing a runtime self-report request.
    /// </summary>
    /// <param name="userRequestLiteral">Original user request text.</param>
    /// <param name="compactReply">Whether the desired reply shape is compact.</param>
    /// <param name="detectionSource">Optional original detection source that produced the directive.</param>
    /// <param name="modelRequested">Optional model-focus flag when already known by the caller.</param>
    /// <param name="toolingRequested">Optional tooling-focus flag when already known by the caller.</param>
    /// <returns>Directive lines suitable for embedding in a prompt envelope.</returns>
    public static IReadOnlyList<string> BuildLines(
        string? userRequestLiteral,
        bool compactReply,
        RuntimeSelfReportDetectionSource? detectionSource = null,
        bool? modelRequested = null,
        bool? toolingRequested = null) {
        var lines = new List<string>(5) {
            Marker,
            compactReply ? "reply_shape: compact" : "reply_shape: default"
        };

        if (detectionSource.HasValue && detectionSource.Value != RuntimeSelfReportDetectionSource.None) {
            lines.Add("detection_source: " + FormatDetectionSource(detectionSource.Value));
        }

        if (modelRequested.HasValue) {
            lines.Add(modelRequested.Value ? "model_requested: true" : "model_requested: false");
        }

        if (toolingRequested.HasValue) {
            lines.Add(toolingRequested.Value ? "tooling_requested: true" : "tooling_requested: false");
        }

        lines.Add("user_request_literal: " + EscapePromptLiteral(userRequestLiteral));
        return lines;
    }

    /// <summary>
    /// Attempts to parse a structured runtime self-report directive from free text or a prompt envelope.
    /// </summary>
    /// <param name="text">Text that may contain the directive block.</param>
    /// <param name="directive">Parsed directive when successful.</param>
    /// <returns><see langword="true"/> when a directive marker was found and parsed.</returns>
    public static bool TryParse(string? text, out ParsedDirective directive) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            directive = default;
            return false;
        }

        var lines = normalized.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++) {
            if (!string.Equals(lines[i].Trim(), Marker, StringComparison.Ordinal)) {
                continue;
            }

            var compactReply = false;
            RuntimeSelfReportDetectionSource? detectionSource = null;
            bool? modelRequested = null;
            bool? toolingRequested = null;
            string? userRequestLiteral = null;

            for (var j = i + 1; j < lines.Length; j++) {
                var line = lines[j].Trim();
                if (line.Length == 0 || line.StartsWith("ix:", StringComparison.Ordinal)) {
                    break;
                }

                if (TryReadDirectiveValue(line, ReplyShapeKey, out var replyShapeValue)) {
                    compactReply = replyShapeValue.AsSpan().Equals("compact".AsSpan(), StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (TryReadDirectiveValue(line, DetectionSourceKey, out var detectionSourceValue)) {
                    detectionSource = ParseDetectionSource(detectionSourceValue);
                    continue;
                }

                if (TryReadDirectiveValue(line, ModelRequestedKey, out var modelRequestedValue)) {
                    modelRequested = modelRequestedValue.AsSpan().Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (TryReadDirectiveValue(line, ToolingRequestedKey, out var toolingRequestedValue)) {
                    toolingRequested = toolingRequestedValue.AsSpan().Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (TryReadDirectiveValue(line, UserRequestLiteralKey, out var userRequestLiteralValue)) {
                    userRequestLiteral = UnescapePromptLiteral(userRequestLiteralValue);
                }
            }

            directive = new ParsedDirective(compactReply, detectionSource, modelRequested, toolingRequested, userRequestLiteral);
            return true;
        }

        directive = default;
        return false;
    }

    private static bool TryReadDirectiveValue(string line, string expectedKey, out string value) {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(expectedKey)) {
            return false;
        }

        var separatorIndex = line.IndexOf(':');
        if (separatorIndex < 0) {
            return false;
        }

        var actualKey = line[..(separatorIndex + 1)];
        if (!actualKey.Equals(expectedKey, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        value = line[(separatorIndex + 1)..].Trim();
        return true;
    }

    /// <summary>
    /// Parsed structured runtime self-report directive fields.
    /// </summary>
    /// <param name="CompactReply">Whether a compact answer shape was requested.</param>
    /// <param name="DetectionSource">Original detection source carried by the directive when known.</param>
    /// <param name="ModelRequested">Whether model/runtime identity was explicitly requested.</param>
    /// <param name="ToolingRequested">Whether tooling details were explicitly requested.</param>
    /// <param name="UserRequestLiteral">Original user request literal carried by the directive.</param>
    public readonly record struct ParsedDirective(
        bool CompactReply,
        RuntimeSelfReportDetectionSource? DetectionSource,
        bool? ModelRequested,
        bool? ToolingRequested,
        string? UserRequestLiteral);

    private static string FormatDetectionSource(RuntimeSelfReportDetectionSource detectionSource) {
        return detectionSource switch {
            RuntimeSelfReportDetectionSource.LexicalFallback => "lexical_fallback",
            RuntimeSelfReportDetectionSource.StructuredDirective => "structured_directive",
            _ => "none"
        };
    }

    private static RuntimeSelfReportDetectionSource? ParseDetectionSource(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.ToLowerInvariant() switch {
            "lexical_fallback" => RuntimeSelfReportDetectionSource.LexicalFallback,
            "structured_directive" => RuntimeSelfReportDetectionSource.StructuredDirective,
            "none" => RuntimeSelfReportDetectionSource.None,
            _ => null
        };
    }

    private static string EscapePromptLiteral(string? text) {
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

    private static string UnescapePromptLiteral(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length >= 2 && normalized[0] == '"' && normalized[^1] == '"') {
            normalized = normalized[1..^1];
        }

        if (normalized.Length == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch != '\\' || i + 1 >= normalized.Length) {
                builder.Append(ch);
                continue;
            }

            i++;
            builder.Append(normalized[i] switch {
                '\\' => '\\',
                '"' => '"',
                'r' => '\r',
                'n' => '\n',
                't' => '\t',
                _ => normalized[i]
            });
        }

        return builder.ToString();
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const int MaxRoutingMetaPromptExposureToolNames = 6;
    private const int MaxRoutingMetaPromptExposureActivityNames = 2;
    private const int MaxRoutingMetaPromptExposureTimelineNames = 1;

    private void ClearToolRoutingInsights() {
        _toolRoutingConfidence.Clear();
        _toolRoutingReason.Clear();
        _toolRoutingScore.Clear();
        lock (_turnDiagnosticsSync) {
            _latestRoutingPromptExposure = null;
            _routingPromptExposureHistory.Clear();
        }

        // Keep explicit per-tool keys so the next options publish clears stale routing state
        // for every visible tool row even if no fresh routing_tool events arrive this turn.
        if (_toolStates.Count == 0) {
            return;
        }

        var toolNames = new List<string>(_toolStates.Keys);
        for (var i = 0; i < toolNames.Count; i++) {
            var name = toolNames[i];
            _toolRoutingConfidence[name] = string.Empty;
            _toolRoutingReason[name] = string.Empty;
        }
    }

    private bool ApplyToolRoutingInsight(ChatStatusMessage status) {
        if (!string.Equals(status.Status, "routing_tool", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var toolName = (status.ToolName ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            return false;
        }

        if (!TryParseRoutingInsightPayload(status.Message, out var confidence, out var reason, out var score)) {
            confidence = "medium";
            reason = "model-selected tool";
            score = null;
        }

        _toolRoutingConfidence[toolName] = confidence;
        _toolRoutingReason[toolName] = reason;
        if (score.HasValue) {
            _toolRoutingScore[toolName] = score.Value;
        } else {
            _toolRoutingScore.Remove(toolName);
        }

        return true;
    }

    private bool ApplyRoutingMetaPromptExposure(ChatStatusMessage status) {
        if (!string.Equals(status.Status, "routing_meta", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (!TryParseRoutingMetaPayload(
                status.Message,
                out var strategy,
                out var selectedToolCount,
                out var totalToolCount,
                out var promptExposureReordered,
                out var promptExposureTopToolNames)) {
            return false;
        }

        var nextSnapshot = new RoutingPromptExposureSnapshot(
            NormalizeRoutingPromptExposureIdentifier(status.RequestId),
            NormalizeRoutingPromptExposureIdentifier(status.ThreadId),
            strategy,
            selectedToolCount,
            totalToolCount,
            promptExposureReordered,
            promptExposureTopToolNames);
        lock (_turnDiagnosticsSync) {
            if (EqualityComparer<RoutingPromptExposureSnapshot?>.Default.Equals(_latestRoutingPromptExposure, nextSnapshot)) {
                return false;
            }

            _latestRoutingPromptExposure = nextSnapshot;
            if (_routingPromptExposureHistory.Count == 0
                || !EqualityComparer<RoutingPromptExposureSnapshot>.Default.Equals(_routingPromptExposureHistory[^1], nextSnapshot)) {
                _routingPromptExposureHistory.Add(nextSnapshot);
                while (_routingPromptExposureHistory.Count > MaxRoutingPromptExposureHistoryEntries) {
                    _routingPromptExposureHistory.RemoveAt(0);
                }
            }
        }

        return true;
    }

    private object? BuildRoutingPromptExposureState() {
        RoutingPromptExposureSnapshot? snapshot;
        lock (_turnDiagnosticsSync) {
            snapshot = _latestRoutingPromptExposure;
        }

        if (snapshot is null) {
            return null;
        }

        return new {
            requestId = snapshot.RequestId,
            threadId = snapshot.ThreadId,
            strategy = snapshot.Strategy,
            selectedToolCount = snapshot.SelectedToolCount,
            totalToolCount = snapshot.TotalToolCount,
            reordered = snapshot.Reordered,
            topToolNames = snapshot.TopToolNames.Length == 0 ? Array.Empty<string>() : (string[])snapshot.TopToolNames.Clone()
        };
    }

    private object[] BuildRoutingPromptExposureHistoryState() {
        RoutingPromptExposureSnapshot[] history;
        lock (_turnDiagnosticsSync) {
            history = _routingPromptExposureHistory.Count == 0
                ? Array.Empty<RoutingPromptExposureSnapshot>()
                : _routingPromptExposureHistory.ToArray();
        }

        if (history.Length == 0) {
            return Array.Empty<object>();
        }

        var items = new object[history.Length];
        for (var i = 0; i < history.Length; i++) {
            var snapshot = history[i];
            items[i] = new {
                requestId = snapshot.RequestId,
                threadId = snapshot.ThreadId,
                strategy = snapshot.Strategy,
                selectedToolCount = snapshot.SelectedToolCount,
                totalToolCount = snapshot.TotalToolCount,
                reordered = snapshot.Reordered,
                topToolNames = snapshot.TopToolNames.Length == 0 ? Array.Empty<string>() : (string[])snapshot.TopToolNames.Clone()
            };
        }

        return items;
    }

    private static string NormalizeRoutingPromptExposureIdentifier(string? value) {
        return (value ?? string.Empty).Trim();
    }

    private static bool TryParseRoutingInsightPayload(string? payload, out string confidence, out string reason)
        => TryParseRoutingInsightPayload(payload, out confidence, out reason, out _);

    private static bool TryParseRoutingInsightPayload(string? payload, out string confidence, out string reason, out double? score) {
        confidence = "medium";
        reason = "model-selected tool";
        score = null;

        var json = (payload ?? string.Empty).Trim();
        if (json.Length == 0 || json[0] != '{' || json.Length > MaxRoutingInsightPayloadChars) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (root.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.ValueKind == JsonValueKind.String) {
                var parsed = (confidenceElement.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                confidence = parsed switch {
                    "high" => "high",
                    "low" => "low",
                    _ => "medium"
                };
            }

            if (root.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String) {
                var parsedReason = (reasonElement.GetString() ?? string.Empty).Trim();
                if (parsedReason.Length > 0) {
                    reason = parsedReason;
                }
            }

            if (root.TryGetProperty("score", out var scoreElement) && scoreElement.ValueKind == JsonValueKind.Number
                && scoreElement.TryGetDouble(out var parsedScore) && !double.IsNaN(parsedScore) && !double.IsInfinity(parsedScore)) {
                score = Math.Clamp(parsedScore, 0d, 9999d);
            }

            return true;
        } catch {
            return false;
        }
    }

    private static bool TryParseRoutingMetaPayload(string? payload, out string strategy, out int selectedToolCount, out int totalToolCount) {
        return TryParseRoutingMetaPayload(
            payload,
            out strategy,
            out selectedToolCount,
            out totalToolCount,
            out _,
            out _);
    }

    private static bool TryParseRoutingMetaPayload(
        string? payload,
        out string strategy,
        out int selectedToolCount,
        out int totalToolCount,
        out bool promptExposureReordered,
        out string[] promptExposureTopToolNames) {
        strategy = "updated";
        selectedToolCount = 0;
        totalToolCount = 0;
        promptExposureReordered = false;
        promptExposureTopToolNames = Array.Empty<string>();

        var json = (payload ?? string.Empty).Trim();
        if (json.Length == 0 || json[0] != '{' || json.Length > MaxRoutingInsightPayloadChars) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions {
                MaxDepth = 8,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            var hasStrategy = false;
            if (root.TryGetProperty("strategy", out var strategyElement) && strategyElement.ValueKind == JsonValueKind.String) {
                var parsed = (strategyElement.GetString() ?? string.Empty).Trim().Replace('_', ' ');
                if (parsed.Length > 0) {
                    strategy = parsed;
                    hasStrategy = true;
                }
            }

            var hasSelectedToolCount = false;
            if (root.TryGetProperty("selectedToolCount", out var selectedElement)
                && TryParseRoutingMetaCount(selectedElement, out var parsedSelected)) {
                selectedToolCount = parsedSelected;
                hasSelectedToolCount = true;
            }

            var hasTotalToolCount = false;
            if (root.TryGetProperty("totalToolCount", out var totalElement)
                && TryParseRoutingMetaCount(totalElement, out var parsedTotal)) {
                totalToolCount = parsedTotal;
                hasTotalToolCount = true;
            }

            if (hasSelectedToolCount && hasTotalToolCount && selectedToolCount > totalToolCount) {
                selectedToolCount = totalToolCount;
            }

            if (root.TryGetProperty("promptExposure", out var promptExposureElement)
                && promptExposureElement.ValueKind == JsonValueKind.Object) {
                if (promptExposureElement.TryGetProperty("reordered", out var reorderedElement)
                    && (reorderedElement.ValueKind == JsonValueKind.True || reorderedElement.ValueKind == JsonValueKind.False)) {
                    promptExposureReordered = reorderedElement.GetBoolean();
                }

                if (promptExposureElement.TryGetProperty("topToolNames", out var topToolNamesElement)
                    && topToolNamesElement.ValueKind == JsonValueKind.Array) {
                    var topToolNames = new List<string>(Math.Min(MaxRoutingMetaPromptExposureToolNames, topToolNamesElement.GetArrayLength()));
                    foreach (var entry in topToolNamesElement.EnumerateArray()) {
                        if (entry.ValueKind != JsonValueKind.String) {
                            continue;
                        }

                        var parsedToolName = (entry.GetString() ?? string.Empty).Trim();
                        if (parsedToolName.Length == 0) {
                            continue;
                        }

                        topToolNames.Add(parsedToolName);
                        if (topToolNames.Count >= MaxRoutingMetaPromptExposureToolNames) {
                            break;
                        }
                    }

                    promptExposureTopToolNames = topToolNames.Count == 0 ? Array.Empty<string>() : topToolNames.ToArray();
                }
            }

            return hasStrategy && hasSelectedToolCount && hasTotalToolCount;
        } catch (JsonException) {
            return false;
        }
    }

    private static string BuildRoutingMetaPromptExposureSuffix(
        bool reordered,
        IReadOnlyList<string>? topToolNames,
        int displayLimit) {
        if (!reordered || topToolNames is not { Count: > 0 } || displayLimit <= 0) {
            return string.Empty;
        }

        var names = topToolNames
            .Select(static name => (name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .Take(displayLimit)
            .ToArray();
        if (names.Length == 0) {
            return string.Empty;
        }

        var remainingCount = Math.Max(0, topToolNames.Count - names.Length);
        return remainingCount > 0
            ? " -> " + string.Join(", ", names) + ", +" + remainingCount.ToString(CultureInfo.InvariantCulture)
            : " -> " + string.Join(", ", names);
    }

    private static bool TryParseRoutingMetaCount(JsonElement element, out int value) {
        value = 0;

        switch (element.ValueKind) {
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var parsedInt)) {
                    value = Math.Max(0, parsedInt);
                    return true;
                }

                if (element.TryGetInt64(out var parsedLong)) {
                    value = ClampRoutingMetaCount(parsedLong);
                    return true;
                }

                if (element.TryGetDecimal(out var parsedDecimal)) {
                    return TryClampRoutingMetaDecimalCount(parsedDecimal, out value);
                }

                return false;
            case JsonValueKind.String:
                var text = (element.GetString() ?? string.Empty).Trim();
                if (text.Length == 0) {
                    return false;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intText)) {
                    value = Math.Max(0, intText);
                    return true;
                }

                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longText)) {
                    value = ClampRoutingMetaCount(longText);
                    return true;
                }

                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                        CultureInfo.InvariantCulture,
                        out var decimalText)) {
                    return TryClampRoutingMetaDecimalCount(decimalText, out value);
                }

                return false;
            default:
                return false;
        }
    }

    private static int ClampRoutingMetaCount(long value) {
        if (value <= 0) {
            return 0;
        }

        return value >= int.MaxValue ? int.MaxValue : (int)value;
    }

    private static bool TryClampRoutingMetaDecimalCount(decimal value, out int normalized) {
        normalized = 0;
        if (value < 0m) {
            return false;
        }

        if (value >= int.MaxValue) {
            normalized = int.MaxValue;
            return true;
        }

        normalized = (int)decimal.Truncate(value);
        return true;
    }

    private string ResolveToolActivityName(string toolName) {
        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "tool";
        }

        if (_toolDisplayNames.TryGetValue(normalized, out var displayName) && !string.IsNullOrWhiteSpace(displayName)) {
            return displayName.Trim();
        }

        return FormatToolDisplayName(normalized);
    }

    private static string FormatDuration(long durationMs) {
        if (durationMs >= 1000) {
            return (durationMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        return durationMs.ToString(CultureInfo.InvariantCulture) + "ms";
    }

    private static string FormatStatusTrace(ChatStatusMessage status) {
        var text = $"status: {status.Status}"
                   + (string.IsNullOrWhiteSpace(status.ToolName) ? string.Empty : $" tool={status.ToolName}")
                   + (status.DurationMs is null ? string.Empty : $" {status.DurationMs}ms");
        if (!string.IsNullOrWhiteSpace(status.Message)) {
            text += $" msg={status.Message}";
        }

        return text;
    }

}

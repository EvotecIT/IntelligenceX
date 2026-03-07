using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Client;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    internal static IReadOnlyList<string> BuildPersonaGuidanceLines(string? personaText) {
        var normalized = (personaText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var role = NormalizePersonaRole(normalized, normalized);
        if (!string.IsNullOrWhiteSpace(role)) {
            lines.Add("Preferred role framing: " + role + ".");
        }

        var traits = CollectPersonaTraits(normalized);
        for (var i = 0; i < traits.Count; i++) {
            var trait = (traits[i] ?? string.Empty).Trim();
            if (trait.Length == 0) {
                continue;
            }

            if (string.Equals(trait, "helpful guidance", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Be proactively useful: reduce user effort, infer sensible next steps, and avoid making the user micromanage the conversation.");
            } else if (string.Equals(trait, "friendly tone", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Sound warm and human instead of dry, stiff, or corporate.");
            } else if (string.Equals(trait, "light humor", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Light humor is allowed when it fits naturally. Keep it subtle, optional, and secondary to usefulness.");
            } else if (string.Equals(trait, "concise outputs", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Prefer compact phrasing and shorter answers by default unless the user clearly wants depth.");
            } else if (string.Equals(trait, "pragmatic guidance", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Favor concrete next steps, practical judgments, and real-world usefulness over abstract theory.");
            } else if (string.Equals(trait, "clear explanations", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("When explanation helps, make it clear and easy to follow instead of compressed or jargon-heavy.");
            } else if (string.Equals(trait, "optimistic tone", StringComparison.OrdinalIgnoreCase)) {
                lines.Add("Keep the tone constructive and steady without sounding fake, evasive, or over-cheerful.");
            }
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private IReadOnlyList<string> BuildRuntimeCapabilityContextLines() {
        var lines = new List<string>();
        var options = BuildChatRequestOptions();
        var selectedModel = options?.Model;
        CountKnownToolStates(out var knownToolCount, out var enabledTools, out var disabledTools);

        var transportLabel = string.Equals(_localProviderTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
            ? "compatible-http"
            : string.Equals(_localProviderTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)
                ? "copilot-cli"
                : "native";
        var modelLabel = string.IsNullOrWhiteSpace(selectedModel) ? "(provider default)" : selectedModel.Trim();
        lines.Add("Runtime transport: " + transportLabel + ", active model for this turn: " + modelLabel);
        lines.Add("Reasoning effort: " + (string.IsNullOrWhiteSpace(_localProviderReasoningEffort) ? "provider default" : _localProviderReasoningEffort)
                  + ", summary: " + (string.IsNullOrWhiteSpace(_localProviderReasoningSummary) ? "provider default" : _localProviderReasoningSummary)
                  + ", verbosity: " + (string.IsNullOrWhiteSpace(_localProviderTextVerbosity) ? "provider default" : _localProviderTextVerbosity)
                  + ", temperature: " + (_localProviderTemperature?.ToString("0.###", CultureInfo.InvariantCulture) ?? "provider default"));
        lines.Add("Reasoning controls support: " + DescribeLocalProviderReasoningSupport(_localProviderTransport, _localProviderBaseUrl));
        lines.Add("Tool availability for this turn: "
                  + DescribeTurnToolAvailability(
                      _localProviderTransport,
                      _localProviderBaseUrl,
                      selectedModel,
                      _availableModels,
                      knownToolCount,
                      enabledTools,
                      disabledTools));
        lines.Add("Configured tool packs: enabled " + enabledTools.ToString(CultureInfo.InvariantCulture)
                  + ", disabled " + disabledTools.ToString(CultureInfo.InvariantCulture));
        AppendWriteToolCapabilityContextLines(lines);
        if (options is not null) {
            lines.Add("Parallel tool execution: " + (options.ParallelTools ? "enabled" : "disabled")
                      + " (" + (options.ParallelToolMode ?? ParallelToolModeAuto) + ")");
            lines.Add("Max tool rounds: " + options.MaxToolRounds.ToString(CultureInfo.InvariantCulture));
            lines.Add("Turn timeout: " + (options.TurnTimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default")
                      + "s; tool timeout: " + (options.ToolTimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default") + "s");
            lines.Add("Plan/execute/review loop: "
                      + (options.PlanExecuteReviewLoop.HasValue ? (options.PlanExecuteReviewLoop.Value ? "enabled" : "disabled") : "default")
                      + "; max review passes: "
                      + (options.MaxReviewPasses?.ToString(CultureInfo.InvariantCulture) ?? "default")
                      + "; model heartbeat: "
                      + (options.ModelHeartbeatSeconds?.ToString(CultureInfo.InvariantCulture) ?? "default") + "s");
        }

        var queuedTurns = GetQueuedTurnCount();
        if (queuedTurns > 0) {
            lines.Add("Queued follow-up turns: " + queuedTurns.ToString(CultureInfo.InvariantCulture));
        }
        lines.Add("Queued turn auto-dispatch: " + (_queueAutoDispatchEnabled ? "enabled" : "paused"));

        lines.Add("Proactive execution mode: " + (_proactiveModeEnabled ? "enabled" : "disabled"));
        lines.Add("Assistant rule: when asked about current runtime/model/tools, answer from these runtime lines and do not infer unavailable capabilities.");
        return lines;
    }

    private void AppendWriteToolCapabilityContextLines(List<string> lines) {
        ArgumentNullException.ThrowIfNull(lines);
        if (_toolWriteCapabilities.Count == 0) {
            return;
        }

        var enabledWriteTools = new List<string>();
        var disabledWriteTools = new List<string>();
        foreach (var pair in _toolWriteCapabilities) {
            if (!pair.Value) {
                continue;
            }

            if (_toolStates.TryGetValue(pair.Key, out var enabled) && enabled) {
                enabledWriteTools.Add(pair.Key);
            } else {
                disabledWriteTools.Add(pair.Key);
            }
        }

        enabledWriteTools.Sort(StringComparer.OrdinalIgnoreCase);
        disabledWriteTools.Sort(StringComparer.OrdinalIgnoreCase);
        lines.Add("Write-capable tools: enabled " + enabledWriteTools.Count.ToString(CultureInfo.InvariantCulture)
                  + ", disabled " + disabledWriteTools.Count.ToString(CultureInfo.InvariantCulture) + ".");
        if (disabledWriteTools.Count > 0) {
            var preview = string.Join(", ", disabledWriteTools.GetRange(0, Math.Min(disabledWriteTools.Count, 8)));
            if (disabledWriteTools.Count > 8) {
                preview += ", ...";
            }
            lines.Add("Disabled write-capable tools: " + preview + ".");
        }
    }

    private void CountKnownToolStates(out int knownToolCount, out int enabledTools, out int disabledTools) {
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _toolDescriptions.Keys) {
            if (!string.IsNullOrWhiteSpace(key)) {
                knownNames.Add(key.Trim());
            }
        }

        if (knownNames.Count == 0) {
            foreach (var key in _toolStates.Keys) {
                if (!string.IsNullOrWhiteSpace(key)) {
                    knownNames.Add(key.Trim());
                }
            }
        }

        knownToolCount = knownNames.Count;
        enabledTools = 0;
        disabledTools = 0;
        if (knownToolCount == 0) {
            return;
        }

        foreach (var toolName in knownNames) {
            if (_toolStates.TryGetValue(toolName, out var enabled) && !enabled) {
                disabledTools++;
            } else {
                enabledTools++;
            }
        }
    }

    internal static string DescribeTurnToolAvailability(string? transport, string? baseUrl, string? selectedModel,
        IReadOnlyList<ModelInfoDto>? availableModels, int knownToolCount, int enabledTools, int disabledTools) {
        if (knownToolCount <= 0) {
            return "unknown (tool catalog is still loading for this session).";
        }

        if (enabledTools <= 0) {
            return "unavailable (all tool packs are disabled by runtime settings).";
        }

        var normalizedTransport = NormalizeLocalProviderTransport(transport);
        if (!string.Equals(normalizedTransport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)) {
            return "available (enabled tools: "
                   + enabledTools.ToString(CultureInfo.InvariantCulture)
                   + ", disabled: "
                   + disabledTools.ToString(CultureInfo.InvariantCulture)
                   + ").";
        }

        if (!IsLocalCompatibleRuntimePreset(DetectCompatibleProviderPreset(baseUrl))) {
            return "available (enabled tools: "
                   + enabledTools.ToString(CultureInfo.InvariantCulture)
                   + "; remote/provider runtime may enforce additional limits).";
        }

        var normalizedModel = (selectedModel ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            return "unknown until a concrete model is selected from the discovered catalog.";
        }

        var modelInfo = FindCatalogModel(availableModels, normalizedModel);
        if (modelInfo is null) {
            return "unknown for model '" + normalizedModel + "' (not present in discovered local catalog).";
        }

        var capabilities = modelInfo.Capabilities ?? Array.Empty<string>();
        if (capabilities.Length == 0) {
            return "unavailable (model '" + normalizedModel + "' does not advertise tool_use capability).";
        }

        for (var i = 0; i < capabilities.Length; i++) {
            if (string.Equals((capabilities[i] ?? string.Empty).Trim(), "tool_use", StringComparison.OrdinalIgnoreCase)) {
                return "available (model '" + normalizedModel + "' advertises tool_use; enabled tools: "
                       + enabledTools.ToString(CultureInfo.InvariantCulture)
                       + ").";
            }
        }

        return "unavailable (model '" + normalizedModel + "' does not advertise tool_use capability).";
    }

    private static ModelInfoDto? FindCatalogModel(IReadOnlyList<ModelInfoDto>? availableModels, string model) {
        if (availableModels is null || availableModels.Count == 0 || string.IsNullOrWhiteSpace(model)) {
            return null;
        }

        for (var i = 0; i < availableModels.Count; i++) {
            var entry = availableModels[i];
            var candidateModel = (entry.Model ?? string.Empty).Trim();
            var candidateId = (entry.Id ?? string.Empty).Trim();
            if (string.Equals(candidateModel, model, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidateId, model, StringComparison.OrdinalIgnoreCase)) {
                return entry;
            }
        }

        return null;
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private sealed partial class ReplSession {
        private const int NoTextFallbackMaxBullets = 3;
        private const int NoTextFallbackSummaryMaxChars = 220;
        private const int NoTextToolOutputRetryPromptMaxEvidenceItems = 18;
        private const int NoTextToolOutputRetryPromptMaxArgumentPairs = 4;
        private const int NoTextToolOutputRetryPromptMaxArgumentValueChars = 64;
        private const int NoTextToolOutputRetryPromptMaxDistinctTargets = 12;

        private static string BuildNoTextReplFallbackText(
            string assistantDraft,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            string? model,
            OpenAITransportKind transport,
            string? baseUrl,
            IReadOnlyList<ToolDefinition>? toolDefinitions = null,
            IReadOnlyList<string>? knownHostTargets = null) {
            var normalizedAssistantDraft = assistantDraft ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedAssistantDraft)
                && !ShouldReplaceAssistantDraftWithToolOutputFallback(normalizedAssistantDraft, toolCalls, toolOutputs)) {
                return normalizedAssistantDraft;
            }

            if (TryBuildToolOutputNoTextFallback(toolCalls, toolOutputs, out var toolOutputFallback)) {
                return toolOutputFallback;
            }

            return BuildNoTextModelWarning(model, transport, baseUrl, toolDefinitions, knownHostTargets);
        }

        internal static string BuildNoTextReplFallbackTextForTesting(
            string assistantDraft,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            string? model,
            OpenAITransportKind transport,
            string? baseUrl,
            IReadOnlyList<ToolDefinition>? toolDefinitions = null,
            IReadOnlyList<string>? knownHostTargets = null) {
            return BuildNoTextReplFallbackText(assistantDraft, toolCalls, toolOutputs, model, transport, baseUrl, toolDefinitions, knownHostTargets);
        }

        internal static string BuildNoTextToolOutputRetryPromptForTesting(
            string userRequest,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs) {
            return BuildNoTextToolOutputRetryPrompt(userRequest, toolCalls, toolOutputs);
        }

        private static bool ShouldPreferDirectToolOutputFallback(
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs) {
            return HasStructuredEventLogOutputEvidence(toolCalls, toolOutputs, out _);
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
            var toolCallByCallId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
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
                    toolCallByCallId[callId] = call;
                }
            }

            if (TryBuildAdEventPlatformFallbackNoTextFallback(toolOutputs, toolNameByCallId, toolCallByCallId, out text)) {
                return true;
            }

            if (TryBuildAdMonitoringNoTextFallback(toolOutputs, toolNameByCallId, toolCallByCallId, out text)) {
                return true;
            }

            if (TryBuildStructuredEventLogNoTextFallback(toolOutputs, toolNameByCallId, toolCallByCallId, out text)) {
                return true;
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

        private static bool TryBuildAdMonitoringNoTextFallback(
            IReadOnlyList<ToolOutput> toolOutputs,
            IReadOnlyDictionary<string, string> toolNameByCallId,
            IReadOnlyDictionary<string, ToolCall> toolCallByCallId,
            out string text) {
            text = string.Empty;
            if (toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            var entries = new List<(string ProbeKind, string Summary, List<string> Targets)>(toolOutputs.Count);
            var distinctTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                if (!toolNameByCallId.TryGetValue(callId, out var toolName)
                    || !string.Equals(toolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }

                toolCallByCallId.TryGetValue(callId, out var toolCall);
                if (!TryBuildAdMonitoringFallbackEntry(output.Output, toolCall, out var entry)) {
                    return false;
                }

                entries.Add(entry);
                for (var targetIndex = 0; targetIndex < entry.Targets.Count; targetIndex++) {
                    distinctTargets.Add(entry.Targets[targetIndex]);
                }
            }

            if (entries.Count == 0 || distinctTargets.Count == 0) {
                return false;
            }

            var builder = new StringBuilder(768);
            builder.AppendLine("Recovered AD monitoring findings from executed tools (model under-reported structured rows):");
            builder.Append("- Coverage: ")
                .Append(distinctTargets.Count.ToString())
                .AppendLine(" distinct DC target(s).");
            for (var i = 0; i < entries.Count; i++) {
                builder.Append("- ")
                    .Append(entries[i].ProbeKind)
                    .Append(": ")
                    .AppendLine(entries[i].Summary);
            }

            text = builder.ToString().TrimEnd();
            return text.Length > 0;
        }

        private static bool TryBuildAdMonitoringFallbackEntry(
            string rawOutput,
            ToolCall? toolCall,
            out (string ProbeKind, string Summary, List<string> Targets) entry) {
            entry = default;
            var raw = (rawOutput ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !TryGetJsonString(root, "probe_kind", out var probeKind)) {
                    return false;
                }

                var expectedTargets = ReadExpectedAdMonitoringTargets(toolCall);
                var targets = new List<string>();
                var rowSummaries = new List<string>();
                string? overallStatus = null;
                string? completedUtc = null;

                if (root.TryGetProperty("probe_result", out var probeResult) && probeResult.ValueKind == JsonValueKind.Object) {
                    if (TryGetJsonString(probeResult, "status", out var probeStatus)) {
                        overallStatus = probeStatus;
                    }

                    if (TryGetJsonString(probeResult, "completed_utc", out var probeCompletedUtc)) {
                        completedUtc = probeCompletedUtc;
                    }

                    if (probeResult.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array) {
                        foreach (var child in children.EnumerateArray()) {
                            if (!TryBuildAdMonitoringRowSummary(child, out var target, out var rowSummary)) {
                                continue;
                            }

                            if (target.Length > 0 && expectedTargets.Count > 0 && !expectedTargets.Contains(target)) {
                                target = string.Empty;
                                rowSummary = string.Empty;
                            }

                            if (target.Length > 0) {
                                targets.Add(target);
                            }

                            if (rowSummary.Length > 0) {
                                rowSummaries.Add(rowSummary);
                            }
                        }
                    }
                }

                if (root.TryGetProperty("result_rows", out var resultRows) && resultRows.ValueKind == JsonValueKind.Array) {
                    foreach (var row in resultRows.EnumerateArray()) {
                        if (!TryBuildAdMonitoringRowSummary(row, out var target, out var rowSummary)) {
                            continue;
                        }

                        if (target.Length == 0 && toolCall?.Arguments is not null
                            && ToolHostTargeting.TryReadHostTargetValues(toolCall.Arguments, out var hostTargets)
                            && hostTargets.Count > 0) {
                            target = NormalizeNoTextCoverageTarget(hostTargets[0]);
                            if (target.Length > 0 && rowSummary.Length > 0 && !rowSummary.StartsWith(target + "=", StringComparison.OrdinalIgnoreCase)) {
                                var separatorIndex = rowSummary.IndexOf('=');
                                rowSummary = separatorIndex >= 0
                                    ? target + rowSummary.Substring(separatorIndex)
                                    : target + "=" + rowSummary;
                            }
                        }

                        if (target.Length > 0 && expectedTargets.Count > 0 && !expectedTargets.Contains(target)) {
                            target = string.Empty;
                            rowSummary = string.Empty;
                        }

                        if (target.Length > 0) {
                            targets.Add(target);
                        }

                        if (rowSummary.Length > 0) {
                            rowSummaries.Add(rowSummary);
                        }

                        if (overallStatus is null && TryGetJsonString(row, "status", out var rowStatus)) {
                            overallStatus = rowStatus;
                        }

                        if (completedUtc is null && TryGetJsonString(row, "completed_utc", out var rowCompletedUtc)) {
                            completedUtc = rowCompletedUtc;
                        }
                    }
                }

                if (targets.Count == 0 && toolCall?.Arguments is not null
                    && ToolHostTargeting.TryReadHostTargetValues(toolCall.Arguments, out var callTargets)
                    && callTargets.Count > 0) {
                    for (var i = 0; i < callTargets.Count; i++) {
                        var target = NormalizeNoTextCoverageTarget(callTargets[i]);
                        if (target.Length > 0) {
                            targets.Add(target);
                        }
                    }
                }

                var dedupedTargets = new List<string>(targets.Count);
                var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < targets.Count; i++) {
                    if (seenTargets.Add(targets[i])) {
                        dedupedTargets.Add(targets[i]);
                    }
                }

                if (expectedTargets.Count > 0) {
                    var filteredTargets = new List<string>(dedupedTargets.Count);
                    for (var i = 0; i < dedupedTargets.Count; i++) {
                        if (expectedTargets.Contains(dedupedTargets[i])) {
                            filteredTargets.Add(dedupedTargets[i]);
                        }
                    }

                    dedupedTargets = filteredTargets;
                }

                if (dedupedTargets.Count == 0) {
                    return false;
                }

                var summaryBuilder = new StringBuilder(256);
                if (!string.IsNullOrWhiteSpace(overallStatus)) {
                    summaryBuilder.Append("overall ")
                        .Append(overallStatus);
                } else {
                    summaryBuilder.Append("structured rows recovered");
                }

                summaryBuilder.Append("; targets ")
                    .Append(dedupedTargets.Count.ToString());

                if (!string.IsNullOrWhiteSpace(completedUtc)) {
                    summaryBuilder.Append("; completed ")
                        .Append(completedUtc);
                }

                if (rowSummaries.Count > 0) {
                    var uniqueSummaries = DeduplicateStringList(rowSummaries);
                    if (expectedTargets.Count > 0) {
                        var filteredSummaries = new List<string>(uniqueSummaries.Count);
                        for (var i = 0; i < uniqueSummaries.Count; i++) {
                            var summary = uniqueSummaries[i];
                            var separatorIndex = summary.IndexOf('=');
                            if (separatorIndex <= 0) {
                                filteredSummaries.Add(summary);
                                continue;
                            }

                            var summaryTarget = summary.Substring(0, separatorIndex).Trim();
                            if (summaryTarget.Length == 0 || expectedTargets.Contains(summaryTarget)) {
                                filteredSummaries.Add(summary);
                            }
                        }

                        uniqueSummaries = filteredSummaries;
                    }

                    summaryBuilder.Append("; ")
                        .Append(string.Join(", ", uniqueSummaries));
                }

                entry = (probeKind, summaryBuilder.ToString(), dedupedTargets);
                return true;
            } catch (JsonException) {
                return false;
            }
        }

        private static bool TryBuildAdMonitoringRowSummary(JsonElement node, out string target, out string summary) {
            target = string.Empty;
            summary = string.Empty;
            if (node.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (TryGetJsonString(node, "target", out var rowTarget)) {
                target = NormalizeNoTextCoverageTarget(rowTarget);
            }

            if (TryGetJsonString(node, "status", out var status) && status.Length > 0) {
                summary = (target.Length > 0 ? target + "=" : string.Empty) + status;
            } else {
                return false;
            }

            if (TryGetJsonString(node, "error", out var error) && error.Length > 0) {
                summary += " (" + TruncateNoTextSummary(error) + ")";
            }

            return true;
        }

        private static bool TryBuildAdEventPlatformFallbackNoTextFallback(
            IReadOnlyList<ToolOutput> toolOutputs,
            IReadOnlyDictionary<string, string> toolNameByCallId,
            IReadOnlyDictionary<string, ToolCall> toolCallByCallId,
            out string text) {
            text = string.Empty;
            if (toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            var blockedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var connectivityByHost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var updateTelemetryByHost = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                if (!toolNameByCallId.TryGetValue(callId, out var toolName)
                    || !toolCallByCallId.TryGetValue(callId, out var toolCall)) {
                    return false;
                }

                if (!TryReadNoTextFallbackHostTarget(toolCall, output.Output, out var hostTarget)) {
                    return false;
                }

                if (hostTarget.Length == 0) {
                    return false;
                }

                if (toolName.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase)) {
                    if (!IsPlatformBlockedEventLogOutputForFallback(output.Output)) {
                        return false;
                    }

                    blockedHosts.Add(hostTarget);
                    continue;
                }

                if (string.Equals(toolName, "system_connectivity_probe", StringComparison.OrdinalIgnoreCase)) {
                    var summary = TryBuildCompactSystemConnectivityFallbackSummary(output.Output, out var compactConnectivitySummary)
                        ? compactConnectivitySummary
                        : BuildToolOutputNoTextSummary(output.Output);
                    if (summary.Length == 0) {
                        summary = "completed successfully.";
                    }

                    connectivityByHost[hostTarget] = summary;
                    continue;
                }

                if (string.Equals(toolName, "system_windows_update_telemetry", StringComparison.OrdinalIgnoreCase)) {
                    var summary = TryBuildCompactWindowsUpdateTelemetryFallbackSummary(output.Output, out var compactUpdateSummary)
                        ? compactUpdateSummary
                        : BuildToolOutputNoTextSummary(output.Output);
                    if (summary.Length == 0) {
                        summary = "completed successfully.";
                    }

                    updateTelemetryByHost[hostTarget] = summary;
                    continue;
                }

                return false;
            }

            if (blockedHosts.Count == 0 || (connectivityByHost.Count == 0 && updateTelemetryByHost.Count == 0)) {
                return false;
            }

            var distinctHosts = new List<string>(blockedHosts.Count + connectivityByHost.Count + updateTelemetryByHost.Count);
            var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var host in blockedHosts) {
                if (seenHosts.Add(host)) {
                    distinctHosts.Add(host);
                }
            }

            foreach (var host in connectivityByHost.Keys) {
                if (seenHosts.Add(host)) {
                    distinctHosts.Add(host);
                }
            }

            foreach (var host in updateTelemetryByHost.Keys) {
                if (seenHosts.Add(host)) {
                    distinctHosts.Add(host);
                }
            }

            var builder = new StringBuilder(768);
            builder.AppendLine("Recovered AD EventLog fallback findings from executed tools (model returned misleading text):");
            builder.Append("- Coverage: ")
                .Append(distinctHosts.Count.ToString())
                .Append(" distinct DC target(s); EventLog blocked on this runtime for ")
                .Append(blockedHosts.Count.ToString())
                .AppendLine(" target(s).");

            for (var i = 0; i < distinctHosts.Count; i++) {
                var host = distinctHosts[i];
                builder.Append("- `")
                    .Append(host)
                    .Append("`:");
                if (blockedHosts.Contains(host)) {
                    builder.Append(" EventLog live read blocked on this runtime.");
                }

                if (connectivityByHost.TryGetValue(host, out var connectivitySummary)) {
                    builder.Append(" Connectivity: ")
                        .Append(connectivitySummary);
                }

                if (updateTelemetryByHost.TryGetValue(host, out var updateSummary)) {
                    builder.Append(" Update telemetry: ")
                        .Append(updateSummary);
                }

                builder.AppendLine();
            }

            text = builder.ToString().TrimEnd();
            return text.Length > 0;
        }

        private static bool TryBuildCompactSystemConnectivityFallbackSummary(string rawOutput, out string summary) {
            summary = string.Empty;
            var raw = (rawOutput ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) {
                    return false;
                }

                var parts = new List<string>(4);
                if (TryGetJsonString(root, "probe_status", out var probeStatus) && probeStatus.Length > 0) {
                    parts.Add("connectivity " + probeStatus.ToLowerInvariant());
                }

                if (root.TryGetProperty("computer_system", out var computerSystem)
                    && computerSystem.ValueKind == JsonValueKind.Object
                    && TryGetJsonString(computerSystem, "domain", out var domain)
                    && domain.Length > 0) {
                    parts.Add("domain " + domain);
                }

                var timeSyncProbeOk = TryReadJsonBoolean(root, "time_sync_probe_succeeded");
                if (timeSyncProbeOk) {
                    parts.Add("time sync ok");
                }

                if (root.TryGetProperty("time_sync", out var timeSync)
                    && timeSync.ValueKind == JsonValueKind.Object
                    && TryReadJsonDouble(timeSync, "time_skew_seconds", out var timeSkewSeconds)) {
                    parts.Add("time skew " + timeSkewSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "s");
                }

                summary = string.Join("; ", parts).Trim();
                return summary.Length > 0;
            } catch (JsonException) {
                return false;
            }
        }

        private static bool TryBuildCompactWindowsUpdateTelemetryFallbackSummary(string rawOutput, out string summary) {
            summary = string.Empty;
            var raw = (rawOutput ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) {
                    return false;
                }

                var parts = new List<string>(4);
                if (root.TryGetProperty("is_pending_reboot", out var pendingReboot)
                    && (pendingReboot.ValueKind == JsonValueKind.True || pendingReboot.ValueKind == JsonValueKind.False)) {
                    parts.Add("pending reboot " + (pendingReboot.ValueKind == JsonValueKind.True ? "yes" : "no"));
                }

                if (TryGetJsonString(root, "coverage_state", out var coverageState) && coverageState.Length > 0) {
                    parts.Add("coverage " + coverageState);
                }

                if (root.TryGetProperty("detection_missing", out var detectionMissing)
                    && (detectionMissing.ValueKind == JsonValueKind.True || detectionMissing.ValueKind == JsonValueKind.False)) {
                    parts.Add("detection missing " + (detectionMissing.ValueKind == JsonValueKind.True ? "yes" : "no"));
                }

                if (TryGetJsonString(root, "wsus_decision", out var wsusDecision) && wsusDecision.Length > 0) {
                    parts.Add("WSUS " + wsusDecision);
                }

                summary = string.Join("; ", parts).Trim();
                return summary.Length > 0;
            } catch (JsonException) {
                return false;
            }
        }

        private static bool TryBuildStructuredEventLogNoTextFallback(
            IReadOnlyList<ToolOutput> toolOutputs,
            IReadOnlyDictionary<string, string> toolNameByCallId,
            IReadOnlyDictionary<string, ToolCall> toolCallByCallId,
            out string text) {
            text = string.Empty;
            if (toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            var entries = new List<(string ToolName, string HostTarget, string Summary, int? Count)>(toolOutputs.Count);
            var usableOutputCount = 0;
            var distinctTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outputsWithRows = 0;
            var outputsWithoutRows = 0;

            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                usableOutputCount++;
                if (!TryBuildStructuredEventLogFallbackEntry(
                        output,
                        toolNameByCallId,
                        toolCallByCallId,
                        out var entry)) {
                    return false;
                }

                entries.Add(entry);
                if (entry.HostTarget.Length > 0) {
                    distinctTargets.Add(entry.HostTarget);
                }

                if (entry.Count.GetValueOrDefault() > 0) {
                    outputsWithRows++;
                } else if (entry.Count.HasValue) {
                    outputsWithoutRows++;
                }
            }

            if (usableOutputCount == 0 || entries.Count != usableOutputCount) {
                return false;
            }

            var builder = new StringBuilder(512);
            builder.AppendLine("Recovered EventLog findings from executed tools (model returned no text):");
            if (distinctTargets.Count > 0) {
                builder.Append("- Coverage: ")
                    .Append(distinctTargets.Count.ToString())
                    .Append(" distinct DC target(s)");
                if (outputsWithRows > 0 || outputsWithoutRows > 0) {
                    builder.Append(" (")
                        .Append(outputsWithRows.ToString())
                        .Append(" returned rows");
                    if (outputsWithoutRows > 0) {
                        builder.Append(", ")
                            .Append(outputsWithoutRows.ToString())
                            .Append(" returned 0 rows");
                    }

                    builder.Append(')');
                }

                builder.AppendLine(".");
            }

            for (var i = 0; i < entries.Count; i++) {
                builder.Append("- `")
                    .Append(entries[i].ToolName)
                    .Append("`: ")
                    .AppendLine(entries[i].Summary);
            }

            text = builder.ToString().TrimEnd();
            return text.Length > 0;
        }

        private static string BuildNoTextToolOutputRetryPrompt(
            string userRequest,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs) {
            var requestText = TruncateNoTextSummary((userRequest ?? string.Empty).Trim());
            var toolMetadataByCallId = new Dictionary<string, (string ToolName, string ArgumentSummary)>(StringComparer.OrdinalIgnoreCase);
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

                    toolMetadataByCallId[callId] = (
                        ToolName: toolName,
                        ArgumentSummary: BuildToolCallArgumentSummary(call.Input));
                }
            }
            var distinctTargetCoverage = BuildDistinctTargetCoverageSummary(toolCalls);

            var evidence = new StringBuilder();
            var appendedCount = 0;
            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                if (appendedCount >= NoTextToolOutputRetryPromptMaxEvidenceItems) {
                    break;
                }

                var summary = BuildToolOutputNoTextSummary(output.Output);
                if (summary.Length == 0) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                var toolName = "tool";
                var argumentSummary = string.Empty;
                if (toolMetadataByCallId.TryGetValue(callId, out var metadata)) {
                    toolName = metadata.ToolName;
                    argumentSummary = metadata.ArgumentSummary;
                }
                evidence.Append("- ")
                    .Append(toolName);
                if (argumentSummary.Length > 0) {
                    evidence.Append(" [")
                        .Append(argumentSummary)
                        .Append("]");
                }

                evidence.Append(": ")
                    .Append(summary)
                    .AppendLine();
                appendedCount++;
            }

            if (evidence.Length == 0) {
                evidence.AppendLine("- Tool outputs were present but no concise summaries were available.");
            }

            return $$"""
                [No-text tool-output recovery]
                Tool execution completed but the assistant draft is empty. Produce the final user-facing answer from the executed tool evidence below.

                User request:
                {{requestText}}

                {{(distinctTargetCoverage.Length == 0 ? string.Empty : "Executed distinct target coverage:\n" + distinctTargetCoverage + "\n\n")}}
                Executed tool evidence:
                {{evidence.ToString().TrimEnd()}}

                Requirements:
                - Use only the executed tool evidence above.
                - Keep the response concise and direct.
                - If executed evidence covers multiple distinct targets, explicitly reflect that coverage instead of collapsing to only the first few targets.
                - Do not call tools again.
                - If evidence is incomplete, state the exact missing evidence briefly.
                Return only the final assistant response text.
                """;
        }

        private static string BuildDistinctTargetCoverageSummary(IReadOnlyList<ToolCall>? toolCalls) {
            var distinctTargets = CollectDistinctCoverageTargets(toolCalls);
            return distinctTargets.Count == 0
                ? string.Empty
                : "- " + string.Join("\n- ", distinctTargets);
        }

        private static List<string> CollectDistinctCoverageTargets(IReadOnlyList<ToolCall>? toolCalls) {
            if (toolCalls is null || toolCalls.Count == 0) {
                return new List<string>(0);
            }

            var distinctTargets = new List<string>(NoTextToolOutputRetryPromptMaxDistinctTargets);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < toolCalls.Count; i++) {
                var call = toolCalls[i];
                if (call?.Arguments is null) {
                    continue;
                }

                if (!ToolHostTargeting.TryReadHostTargetValues(call.Arguments, out var hostTargets) || hostTargets.Count == 0) {
                    continue;
                }

                for (var targetIndex = 0; targetIndex < hostTargets.Count; targetIndex++) {
                    var candidate = NormalizeNoTextCoverageTarget(hostTargets[targetIndex]);
                    if (candidate.Length == 0 || !seen.Add(candidate)) {
                        continue;
                    }

                    distinctTargets.Add(candidate);
                    if (distinctTargets.Count >= NoTextToolOutputRetryPromptMaxDistinctTargets) {
                        return distinctTargets;
                    }
                }
            }

            return distinctTargets;
        }

        private static string BuildToolCallArgumentSummary(string? rawArgumentsJson) {
            var normalized = (rawArgumentsJson ?? string.Empty).Trim();
            if (normalized.Length == 0 || !LooksLikeJsonPayload(normalized)) {
                return string.Empty;
            }

            try {
                using var doc = JsonDocument.Parse(normalized);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                    return string.Empty;
                }

                var pairs = new List<string>(NoTextToolOutputRetryPromptMaxArgumentPairs);
                foreach (var property in doc.RootElement.EnumerateObject()) {
                    if (pairs.Count >= NoTextToolOutputRetryPromptMaxArgumentPairs) {
                        break;
                    }

                    var key = CollapseWhitespace((property.Name ?? string.Empty).Trim());
                    if (key.Length == 0) {
                        continue;
                    }

                    if (!TryFormatCompactArgumentValue(property.Value, out var compactValue)) {
                        continue;
                    }

                    pairs.Add(key + "=" + compactValue);
                }

                if (pairs.Count == 0) {
                    return string.Empty;
                }

                return "args: " + string.Join(", ", pairs);
            } catch (JsonException) {
                return string.Empty;
            }
        }

        private static bool TryFormatCompactArgumentValue(JsonElement value, out string compactValue) {
            compactValue = string.Empty;
            string raw;
            switch (value.ValueKind) {
                case JsonValueKind.String:
                    raw = (value.GetString() ?? string.Empty).Trim();
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    raw = value.ToString();
                    break;
                default:
                    return false;
            }

            if (raw.Length == 0) {
                return false;
            }

            compactValue = TruncateNoTextArgumentValue(raw);
            return compactValue.Length > 0;
        }

        private static string NormalizeNoTextCoverageTarget(string value) {
            var candidate = (value ?? string.Empty).Trim().Trim('"').TrimEnd('.');
            if (candidate.Length == 0 || candidate.Length > 128) {
                return string.Empty;
            }

            return candidate;
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

                if (TryBuildStructuredEventLogSummary(root, out summary)) {
                    return true;
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

        private static bool TryBuildStructuredEventLogSummary(JsonElement root, out string summary) {
            summary = string.Empty;
            if (!LooksLikeStructuredEventLogOutput(root)) {
                return false;
            }

            var logName = string.Empty;
            TryGetJsonString(root, "log_name", out logName);

            var discoveryStatus = default(JsonElement);
            var hasDiscoveryStatus = root.TryGetProperty("discovery_status", out discoveryStatus)
                && discoveryStatus.ValueKind == JsonValueKind.Object;

            var machineName = string.Empty;
            if (hasDiscoveryStatus) {
                TryGetJsonString(discoveryStatus, "machine_name", out machineName);
            }

            if (machineName.Length == 0) {
                machineName = TryReadStructuredEventLogMachineName(root);
            }

            var queryMode = string.Empty;
            if (hasDiscoveryStatus) {
                TryGetJsonString(discoveryStatus, "query_mode", out queryMode);
            }

            var count = TryReadJsonInt32(root, "count");
            if (!count.HasValue && hasDiscoveryStatus) {
                count = TryReadJsonInt32(discoveryStatus, "rows");
            }

            var truncated = TryReadJsonBoolean(root, "truncated");
            if (!truncated && hasDiscoveryStatus) {
                truncated = TryReadJsonBoolean(discoveryStatus, "truncated");
            }

            var builder = new StringBuilder(160);
            builder.Append(logName.Length == 0 ? "EventLog" : logName + " EventLog");
            if (queryMode.Length > 0) {
                builder.Append(' ')
                    .Append(queryMode.Replace('_', ' '));
            }

            if (machineName.Length > 0) {
                builder.Append(" for `")
                    .Append(machineName)
                    .Append('`');
            }

            if (count.HasValue) {
                builder.Append(" returned ")
                    .Append(count.Value.ToString())
                    .Append(" row(s)");
            } else {
                builder.Append(" returned structured rows");
            }

            if (truncated) {
                builder.Append("; preview truncated");
            }

            builder.Append('.');
            summary = builder.ToString();
            return true;
        }

        private static bool TryBuildStructuredEventLogFallbackEntry(
            ToolOutput output,
            IReadOnlyDictionary<string, string> toolNameByCallId,
            IReadOnlyDictionary<string, ToolCall> toolCallByCallId,
            out (string ToolName, string HostTarget, string Summary, int? Count) entry) {
            entry = default;

            var raw = (output.Output ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !LooksLikeStructuredEventLogOutput(root)) {
                    return false;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                var toolName = toolNameByCallId.TryGetValue(callId, out var knownToolName)
                    ? knownToolName
                    : "tool";
                toolCallByCallId.TryGetValue(callId, out var toolCall);

                if (!TryBuildStructuredEventLogSummary(root, out var summary)) {
                    return false;
                }

                var hostTarget = string.Empty;
                if (root.TryGetProperty("discovery_status", out var discoveryStatus)
                    && discoveryStatus.ValueKind == JsonValueKind.Object) {
                    TryGetJsonString(discoveryStatus, "machine_name", out hostTarget);
                }

                if (hostTarget.Length == 0) {
                    hostTarget = TryReadStructuredEventLogMachineName(root);
                }

                if (hostTarget.Length == 0 && toolCall?.Arguments is not null
                    && ToolHostTargeting.TryReadHostTargetValues(toolCall.Arguments, out var hostTargets)
                    && hostTargets.Count > 0) {
                    hostTarget = NormalizeNoTextCoverageTarget(hostTargets[0]);
                }

                if (hostTarget.Length > 0 && summary.IndexOf(hostTarget, StringComparison.OrdinalIgnoreCase) < 0) {
                    var rowSuffixIndex = summary.IndexOf(" returned ", StringComparison.OrdinalIgnoreCase);
                    if (rowSuffixIndex >= 0) {
                        summary = summary.Insert(rowSuffixIndex, " for `" + hostTarget + "`");
                    }
                }

                var count = TryReadJsonInt32(root, "count");
                if (!count.HasValue
                    && root.TryGetProperty("discovery_status", out discoveryStatus)
                    && discoveryStatus.ValueKind == JsonValueKind.Object) {
                    count = TryReadJsonInt32(discoveryStatus, "rows");
                }

                entry = (toolName, hostTarget, summary, count);
                return true;
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

        private static bool LooksLikeStructuredEventLogOutput(JsonElement root) {
            if (!root.TryGetProperty("log_name", out _)) {
                return false;
            }

            return root.TryGetProperty("events", out _)
                || root.TryGetProperty("events_view", out _)
                || root.TryGetProperty("discovery_status", out _);
        }

        private static string TryReadStructuredEventLogMachineName(JsonElement root) {
            if (TryReadStructuredEventLogMachineNameFromArray(root, "events_view", out var machineName)
                || TryReadStructuredEventLogMachineNameFromArray(root, "events", out machineName)) {
                return machineName;
            }

            return string.Empty;
        }

        private static bool TryReadStructuredEventLogMachineNameFromArray(JsonElement root, string key, out string machineName) {
            machineName = string.Empty;
            if (!root.TryGetProperty(key, out var rows) || rows.ValueKind != JsonValueKind.Array) {
                return false;
            }

            foreach (var row in rows.EnumerateArray()) {
                if (row.ValueKind != JsonValueKind.Object || !TryGetJsonString(row, "machine_name", out machineName)) {
                    continue;
                }

                return machineName.Length > 0;
            }

            machineName = string.Empty;
            return false;
        }

        private static int? TryReadJsonInt32(JsonElement node, string key) {
            if (!node.TryGetProperty(key, out var property)) {
                return null;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number)) {
                return number;
            }

            return null;
        }

        private static bool TryReadJsonDouble(JsonElement node, string key, out double value) {
            value = 0;
            if (!node.TryGetProperty(key, out var property) || property.ValueKind != JsonValueKind.Number) {
                return false;
            }

            return property.TryGetDouble(out value);
        }

        private static bool TryReadJsonBoolean(JsonElement node, string key) {
            if (!node.TryGetProperty(key, out var property)) {
                return false;
            }

            return property.ValueKind switch {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => false
            };
        }

        private static bool ShouldReplaceAssistantDraftWithToolOutputFallback(
            string assistantDraft,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs) {
            var normalizedDraft = (assistantDraft ?? string.Empty).Trim();
            if (normalizedDraft.Length == 0 || toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            if (HasAdEventPlatformFallbackEvidence(toolCalls, toolOutputs, out var fallbackTargets)) {
                for (var i = 0; i < fallbackTargets.Count; i++) {
                    if (normalizedDraft.IndexOf(fallbackTargets[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                        return false;
                    }
                }

                return normalizedDraft.IndexOf("Missing input once", StringComparison.OrdinalIgnoreCase) >= 0
                       || normalizedDraft.IndexOf("machine_name", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (HasAdMonitoringStructuredRowEvidence(toolCalls, toolOutputs)) {
                if (!HasAdMonitoringVisibleRowEvidenceInDraft(normalizedDraft, toolCalls, toolOutputs)) {
                    return true;
                }

                if (ClaimsAdMonitoringRowsAreMissing(normalizedDraft)) {
                    return true;
                }
            }

            if (!HasStructuredEventLogOutputEvidence(toolCalls, toolOutputs, out var distinctTargets)) {
                return false;
            }

            for (var i = 0; i < distinctTargets.Count; i++) {
                if (normalizedDraft.IndexOf(distinctTargets[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                    return false;
                }
            }

            return normalizedDraft.IndexOf("machine_name", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ClaimsAdMonitoringRowsAreMissing(string normalizedDraft) {
            if (normalizedDraft.Length == 0) {
                return false;
            }

            if (normalizedDraft.IndexOf("row values were not provided", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("result rows were not provided", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("row contents are absent", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("actual row values were not provided", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("row values were absent", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("per-dc result rows", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("per-dc statuses", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("per-dc latencies", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedDraft.IndexOf("per-dc judgment", StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            return normalizedDraft.IndexOf("no visible `status`", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalizedDraft.IndexOf("no visible status", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalizedDraft.IndexOf("no visible `completed_utc`", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalizedDraft.IndexOf("no visible completed_utc", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalizedDraft.IndexOf("no detailed results were returned", StringComparison.OrdinalIgnoreCase) >= 0
                   || normalizedDraft.IndexOf("did not include the per-dc result rows", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasAdEventPlatformFallbackEvidence(
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            out List<string> distinctTargets) {
            distinctTargets = CollectDistinctCoverageTargets(toolCalls);
            if (toolOutputs is null || toolOutputs.Count == 0 || distinctTargets.Count == 0) {
                return false;
            }

            var hasPlatformBlockedEventLog = false;
            var hasSystemFallbackOutput = false;
            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                var toolName = string.Empty;
                for (var callIndex = 0; callIndex < toolCalls.Count; callIndex++) {
                    var call = toolCalls[callIndex];
                    if (call is null || !string.Equals(call.CallId, callId, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    toolName = (call.Name ?? string.Empty).Trim();
                    break;
                }

                if (toolName.Length == 0) {
                    continue;
                }

                if (toolName.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase)
                    && IsPlatformBlockedEventLogOutputForFallback(output.Output)) {
                    hasPlatformBlockedEventLog = true;
                    continue;
                }

                if (string.Equals(toolName, "system_connectivity_probe", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(toolName, "system_windows_update_telemetry", StringComparison.OrdinalIgnoreCase)) {
                    hasSystemFallbackOutput = true;
                }
            }

            return hasPlatformBlockedEventLog && hasSystemFallbackOutput;
        }

        private static bool HasAdMonitoringStructuredRowEvidence(
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs) {
            if (toolCalls is null || toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            var toolNameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < toolCalls.Count; i++) {
                var call = toolCalls[i];
                if (call is null) {
                    continue;
                }

                var callId = (call.CallId ?? string.Empty).Trim();
                var toolName = (call.Name ?? string.Empty).Trim();
                if (callId.Length > 0 && toolName.Length > 0) {
                    toolNameByCallId[callId] = toolName;
                }
            }

            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                if (!toolNameByCallId.TryGetValue(callId, out var toolName)
                    || !string.Equals(toolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }

                var raw = (output.Output ?? string.Empty).Trim();
                if (!LooksLikeJsonPayload(raw)) {
                    return false;
                }

                try {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) {
                        return false;
                    }

                    if (root.TryGetProperty("probe_result", out var probeResult)
                        && probeResult.ValueKind == JsonValueKind.Object
                        && probeResult.TryGetProperty("children", out var children)
                        && children.ValueKind == JsonValueKind.Array
                        && children.GetArrayLength() > 0) {
                        continue;
                    }

                    if (root.TryGetProperty("result_rows", out var resultRows)
                        && resultRows.ValueKind == JsonValueKind.Array
                        && resultRows.GetArrayLength() > 0) {
                        continue;
                    }

                    return false;
                } catch (JsonException) {
                    return false;
                }
            }

            return true;
        }

        private static bool DraftMentionsAnyCoverageTarget(string normalizedDraft, IReadOnlyList<string> coverageTargets) {
            if (normalizedDraft.Length == 0 || coverageTargets is null || coverageTargets.Count == 0) {
                return false;
            }

            for (var i = 0; i < coverageTargets.Count; i++) {
                if (normalizedDraft.IndexOf(coverageTargets[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAdMonitoringVisibleRowEvidenceInDraft(
            string normalizedDraft,
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs) {
            if (!TryCollectAdMonitoringStructuredTokens(toolCalls, toolOutputs, out var distinctTargets, out var statusTokens, out var completedUtcValues)) {
                return false;
            }

            if (!DraftMentionsAnyCoverageTarget(normalizedDraft, distinctTargets)) {
                return false;
            }

            for (var i = 0; i < completedUtcValues.Count; i++) {
                if (normalizedDraft.IndexOf(completedUtcValues[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }

            for (var i = 0; i < statusTokens.Count; i++) {
                if (ContainsStandaloneInsensitive(normalizedDraft, statusTokens[i])) {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCollectAdMonitoringStructuredTokens(
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            out List<string> distinctTargets,
            out List<string> statusTokens,
            out List<string> completedUtcValues) {
            distinctTargets = new List<string>();
            statusTokens = new List<string>();
            completedUtcValues = new List<string>();
            if (toolCalls is null || toolOutputs is null || toolOutputs.Count == 0) {
                return false;
            }

            var toolNameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var toolCallByCallId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
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
                toolCallByCallId[callId] = call;
            }

            var distinctTargetSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var statusSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var completedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                var callId = (output.CallId ?? string.Empty).Trim();
                if (!toolNameByCallId.TryGetValue(callId, out var toolName)
                    || !string.Equals(toolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
                    return false;
                }

                var raw = (output.Output ?? string.Empty).Trim();
                if (!LooksLikeJsonPayload(raw)) {
                    return false;
                }

                toolCallByCallId.TryGetValue(callId, out var toolCall);
                var expectedTargets = ReadExpectedAdMonitoringTargets(toolCall);

                try {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) {
                        return false;
                    }

                    if (root.TryGetProperty("probe_result", out var probeResult) && probeResult.ValueKind == JsonValueKind.Object) {
                        if (TryGetJsonString(probeResult, "status", out var probeStatus) && probeStatus.Length > 0) {
                            statusSet.Add(probeStatus);
                        }

                        if (TryGetJsonString(probeResult, "completed_utc", out var probeCompletedUtc) && probeCompletedUtc.Length > 0) {
                            completedSet.Add(probeCompletedUtc);
                        }

                        if (probeResult.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array) {
                            foreach (var child in children.EnumerateArray()) {
                                if (TryGetJsonString(child, "target", out var childTarget)) {
                                    var normalizedTarget = NormalizeNoTextCoverageTarget(childTarget);
                                    if (normalizedTarget.Length > 0
                                        && (expectedTargets.Count == 0 || expectedTargets.Contains(normalizedTarget))) {
                                        distinctTargetSet.Add(normalizedTarget);
                                    }
                                }

                                if (TryGetJsonString(child, "status", out var childStatus) && childStatus.Length > 0) {
                                    statusSet.Add(childStatus);
                                }

                                if (TryGetJsonString(child, "completed_utc", out var childCompletedUtc) && childCompletedUtc.Length > 0) {
                                    completedSet.Add(childCompletedUtc);
                                }
                            }
                        }
                    }

                    if (root.TryGetProperty("result_rows", out var resultRows) && resultRows.ValueKind == JsonValueKind.Array) {
                        foreach (var row in resultRows.EnumerateArray()) {
                            var normalizedTarget = string.Empty;
                            if (TryGetJsonString(row, "target", out var rowTarget)) {
                                normalizedTarget = NormalizeNoTextCoverageTarget(rowTarget);
                            } else if (toolCall?.Arguments is not null
                                && ToolHostTargeting.TryReadHostTargetValues(toolCall.Arguments, out var hostTargets)
                                && hostTargets.Count > 0) {
                                normalizedTarget = NormalizeNoTextCoverageTarget(hostTargets[0]);
                            }

                            if (normalizedTarget.Length > 0
                                && (expectedTargets.Count == 0 || expectedTargets.Contains(normalizedTarget))) {
                                distinctTargetSet.Add(normalizedTarget);
                            }

                            if (TryGetJsonString(row, "status", out var rowStatus) && rowStatus.Length > 0) {
                                statusSet.Add(rowStatus);
                            }

                            if (TryGetJsonString(row, "completed_utc", out var rowCompletedUtc) && rowCompletedUtc.Length > 0) {
                                completedSet.Add(rowCompletedUtc);
                            }
                        }
                    }
                } catch (JsonException) {
                    return false;
                }
            }

            distinctTargets.AddRange(distinctTargetSet);
            statusTokens.AddRange(statusSet);
            completedUtcValues.AddRange(completedSet);
            return distinctTargets.Count > 0 && (statusTokens.Count > 0 || completedUtcValues.Count > 0);
        }

        private static List<string> DeduplicateStringList(IReadOnlyList<string> values) {
            var result = new List<string>();
            if (values is null || values.Count == 0) {
                return result;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < values.Count; i++) {
                var value = (values[i] ?? string.Empty).Trim();
                if (value.Length == 0 || !seen.Add(value)) {
                    continue;
                }

                result.Add(value);
            }

            return result;
        }

        private static HashSet<string> ReadExpectedAdMonitoringTargets(ToolCall? toolCall) {
            var expectedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rawArguments = toolCall?.Arguments;
            if (rawArguments is null) {
                return expectedTargets;
            }

            if (TryReadToolInputValuesByKey(rawArguments, "targets", out var explicitTargets)) {
                for (var i = 0; i < explicitTargets.Count; i++) {
                    var normalizedTarget = NormalizeNoTextCoverageTarget(explicitTargets[i]);
                    if (normalizedTarget.Length > 0) {
                        expectedTargets.Add(normalizedTarget);
                    }
                }
            }

            if (TryReadToolInputValuesByKey(rawArguments, "target", out var singleTargets)) {
                for (var i = 0; i < singleTargets.Count; i++) {
                    var normalizedTarget = NormalizeNoTextCoverageTarget(singleTargets[i]);
                    if (normalizedTarget.Length > 0) {
                        expectedTargets.Add(normalizedTarget);
                    }
                }
            }

            if (expectedTargets.Count > 0) {
                return expectedTargets;
            }

            if (ToolHostTargeting.TryReadHostTargetValues(rawArguments, out var hostTargets) && hostTargets.Count > 0) {
                for (var i = 0; i < hostTargets.Count; i++) {
                    var normalizedTarget = NormalizeNoTextCoverageTarget(hostTargets[i]);
                    if (normalizedTarget.Length > 0) {
                        expectedTargets.Add(normalizedTarget);
                    }
                }
            }

            return expectedTargets;
        }

        private static bool ContainsStandaloneInsensitive(string text, string token) {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) {
                return false;
            }

            var searchStart = 0;
            while (searchStart < text.Length) {
                var index = text.IndexOf(token, searchStart, StringComparison.OrdinalIgnoreCase);
                if (index < 0) {
                    return false;
                }

                var beforeIndex = index - 1;
                var afterIndex = index + token.Length;
                var beforeIsWord = beforeIndex >= 0 && (char.IsLetterOrDigit(text[beforeIndex]) || text[beforeIndex] == '_');
                var afterIsWord = afterIndex < text.Length && (char.IsLetterOrDigit(text[afterIndex]) || text[afterIndex] == '_');
                if (!beforeIsWord && !afterIsWord) {
                    return true;
                }

                searchStart = index + token.Length;
            }

            return false;
        }

        private static bool TryReadNoTextFallbackHostTarget(
            ToolCall toolCall,
            string rawOutput,
            out string hostTarget) {
            hostTarget = string.Empty;
            if (toolCall?.Arguments is not null
                && ToolHostTargeting.TryReadHostTargetValues(toolCall.Arguments, out var hostTargets)
                && hostTargets.Count > 0) {
                hostTarget = NormalizeNoTextCoverageTarget(hostTargets[0]);
                if (hostTarget.Length > 0) {
                    return true;
                }
            }

            var toolName = (toolCall?.Name ?? string.Empty).Trim();
            if (toolName.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase)) {
                hostTarget = TryReadEventLogHostTargetFromOutput(rawOutput);
                return hostTarget.Length > 0;
            }

            hostTarget = TryReadComputerNameFromOutput(rawOutput);
            return hostTarget.Length > 0;
        }

        private static string TryReadEventLogHostTargetFromOutput(string rawOutput) {
            var raw = (rawOutput ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return string.Empty;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) {
                    return string.Empty;
                }

                if (TryGetJsonString(root, "machine_name", out var machineName)) {
                    return NormalizeNoTextCoverageTarget(machineName);
                }

                if (root.TryGetProperty("discovery_status", out var discoveryStatus)
                    && discoveryStatus.ValueKind == JsonValueKind.Object
                    && TryGetJsonString(discoveryStatus, "machine_name", out machineName)) {
                    return NormalizeNoTextCoverageTarget(machineName);
                }

                return string.Empty;
            } catch (JsonException) {
                return string.Empty;
            }
        }

        private static string TryReadComputerNameFromOutput(string rawOutput) {
            var raw = (rawOutput ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return string.Empty;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) {
                    return string.Empty;
                }

                if (TryGetJsonString(root, "computer_name", out var computerName)) {
                    return NormalizeNoTextCoverageTarget(computerName);
                }

                return string.Empty;
            } catch (JsonException) {
                return string.Empty;
            }
        }

        private static bool IsPlatformBlockedEventLogOutputForFallback(string rawOutput) {
            var raw = (rawOutput ?? string.Empty).Trim();
            if (!LooksLikeJsonPayload(raw)) {
                return false;
            }

            try {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) {
                    return false;
                }

                if (TryReadPlatformBlockedCode(root, "error_code")
                    || (root.TryGetProperty("failure", out var failure)
                        && failure.ValueKind == JsonValueKind.Object
                        && TryReadPlatformBlockedCode(failure, "code"))) {
                    return true;
                }

                if (TryReadPlatformBlockedMessage(root, "error")
                    || (root.TryGetProperty("failure", out failure)
                        && failure.ValueKind == JsonValueKind.Object
                        && TryReadPlatformBlockedMessage(failure, "message"))) {
                    return true;
                }

                return false;
            } catch (JsonException) {
                return false;
            }
        }

        private static bool HasStructuredEventLogOutputEvidence(
            IReadOnlyList<ToolCall> toolCalls,
            IReadOnlyList<ToolOutput> toolOutputs,
            out List<string> distinctTargets) {
            distinctTargets = CollectDistinctCoverageTargets(toolCalls);
            if (toolOutputs is null || toolOutputs.Count == 0 || distinctTargets.Count == 0) {
                return false;
            }

            var structuredEventLogOutputs = 0;
            for (var i = 0; i < toolOutputs.Count; i++) {
                var output = toolOutputs[i];
                if (output is null) {
                    continue;
                }

                var raw = (output.Output ?? string.Empty).Trim();
                if (!LooksLikeJsonPayload(raw)) {
                    continue;
                }

                try {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object
                        && LooksLikeStructuredEventLogOutput(doc.RootElement)) {
                        structuredEventLogOutputs++;
                    }
                } catch (JsonException) {
                    return false;
                }
            }

            return structuredEventLogOutputs > 0;
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

        private static string TruncateNoTextArgumentValue(string text) {
            var normalized = CollapseWhitespace((text ?? string.Empty).Trim());
            if (normalized.Length <= NoTextToolOutputRetryPromptMaxArgumentValueChars) {
                return normalized;
            }

            return normalized.Substring(0, NoTextToolOutputRetryPromptMaxArgumentValueChars - 3).TrimEnd() + "...";
        }

        private static string BuildNoTextModelWarning(
            string? model,
            OpenAITransportKind transport,
            string? baseUrl,
            IReadOnlyList<ToolDefinition>? toolDefinitions = null,
            IReadOnlyList<string>? knownHostTargets = null) {
            var normalizedModel = (model ?? string.Empty).Trim();
            if (normalizedModel.Length == 0) {
                normalizedModel = "unknown";
            }

            var executionWarning = BuildToolExecutionAvailabilityWarningText(
                toolDefinitions: toolDefinitions,
                toolPatterns: null,
                knownHostTargets: knownHostTargets);
            var knownHostHint = BuildKnownHostTargetHint(knownHostTargets);
            var executionWarningBlock = string.IsNullOrWhiteSpace(executionWarning)
                ? string.Empty
                : "\nTooling: " + executionWarning.Trim();
            var knownHostBlock = string.IsNullOrWhiteSpace(knownHostHint)
                ? string.Empty
                : "\n" + knownHostHint.Trim();

            if (transport == OpenAITransportKind.CompatibleHttp) {
                var endpoint = string.IsNullOrWhiteSpace(baseUrl) ? "configured endpoint" : baseUrl!.Trim();
                return "[warning] No response text was produced by the runtime.\n\n"
                       + "Model: " + normalizedModel + "\n"
                       + "Endpoint: " + endpoint + executionWarningBlock + knownHostBlock + "\n\n"
                       + "Try a different model, then run Refresh Models and retry.";
            }

            return "[warning] No response text was produced by the model.\n\n"
                   + "Model: " + normalizedModel + executionWarningBlock + knownHostBlock + "\n\n"
                   + "Retry the turn, or choose a different model.";
        }
    }
}

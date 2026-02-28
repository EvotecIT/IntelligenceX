using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private async Task<IReadOnlyList<ToolDefinition>> TrySelectToolsViaModelPlannerAsync(IntelligenceXClient client, string activeThreadId, string userRequest,
        IReadOnlyList<ToolDefinition> definitions, int limit, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(activeThreadId)
            || string.IsNullOrWhiteSpace(userRequest)
            || definitions.Count == 0
            || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        IReadOnlyList<ToolDefinition> selected = Array.Empty<ToolDefinition>();
        Exception? plannerFailure = null;
        Exception? restoreFailure = null;
        try {
            var plannerPrompt = BuildModelPlannerPrompt(userRequest, definitions, limit);
            if (plannerPrompt.Length == 0) {
                return Array.Empty<ToolDefinition>();
            }

            var plannerOptions = new ChatOptions {
                Model = _options.Model,
                Tools = null,
                ToolChoice = ToolChoice.None,
                ParallelToolCalls = false,
                Temperature = 0,
                ReasoningEffort = ReasoningEffort.Minimal,
                ReasoningSummary = ReasoningSummary.Off,
                TextVerbosity = TextVerbosity.Low,
                Instructions = """
                    You are a semantic tool router.
                    Select the most relevant tools for the user request from the provided catalog.
                    Return strict JSON only with this shape:
                    {"tool_names":["tool_a","tool_b"]}
                    Rules:
                    - Use only tool names present in the provided list.
                    - Prefer precision over recall.
                    - Return at most the requested max count.
                    - Do not add commentary or markdown.
                    """
            };

            _ = await EnsurePlannerThreadAsync(client, activeThreadId, plannerOptions.Model, cancellationToken).ConfigureAwait(false);

            var turn = await client.ChatAsync(ChatInput.FromText(plannerPrompt), plannerOptions, cancellationToken).ConfigureAwait(false);
            var plannerText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
            selected = ParsePlannerSelectedDefinitions(plannerText, definitions, limit);
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            plannerFailure = ex;
        } finally {
            try {
                await client.UseThreadAsync(activeThreadId, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) {
                restoreFailure = ex;
                ForgetPlannerThreadContext(activeThreadId);
            }
        }

        if (restoreFailure is not null) {
            Trace.TraceWarning(
                $"Tool planner failed to restore active thread '{activeThreadId}': {restoreFailure.GetType().Name}: {restoreFailure.Message}");
            return Array.Empty<ToolDefinition>();
        }

        if (plannerFailure is not null) {
            Trace.TraceWarning($"Tool planner selection failed: {plannerFailure.GetType().Name}: {plannerFailure.Message}");
            return Array.Empty<ToolDefinition>();
        }

        return selected;
    }

    private async Task<ThreadInfo> EnsurePlannerThreadAsync(IntelligenceXClient client, string activeThreadId, string? model,
        CancellationToken cancellationToken) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            throw new ArgumentException("activeThreadId is required.", nameof(activeThreadId));
        }

        if (TryResolvePlannerThreadContext(normalizedActiveThreadId, out var plannerThreadId)) {
            try {
                return await client.UseThreadAsync(plannerThreadId, cancellationToken).ConfigureAwait(false);
            } catch {
                ForgetPlannerThreadContext(normalizedActiveThreadId);
            }
        }

        var plannerThread = await client.StartNewThreadAsync(
                model: model,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        RememberPlannerThreadContext(normalizedActiveThreadId, plannerThread.Id, DateTime.UtcNow.Ticks);

        // StartNewThreadAsync should set the current thread, but make the thread switch explicit to avoid
        // accidental planner prompts polluting the active conversation thread if transport semantics change.
        try {
            return await client.UseThreadAsync(plannerThread.Id, cancellationToken).ConfigureAwait(false);
        } catch {
            return plannerThread;
        }
    }

    private void RememberPlannerThreadContext(string activeThreadId, string plannerThreadId, long seenUtcTicks) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        var normalizedPlannerThreadId = (plannerThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0 || normalizedPlannerThreadId.Length == 0) {
            return;
        }

        var ticks = seenUtcTicks > 0 ? seenUtcTicks : DateTime.UtcNow.Ticks;
        lock (_toolRoutingContextLock) {
            _plannerThreadIdByActiveThreadId[normalizedActiveThreadId] = normalizedPlannerThreadId;
            _plannerThreadSeenUtcTicksByActiveThreadId[normalizedActiveThreadId] = ticks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistPlannerThreadContextSnapshot(normalizedActiveThreadId, normalizedPlannerThreadId, ticks);
    }

    private void ForgetPlannerThreadContext(string activeThreadId) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            return;
        }

        lock (_toolRoutingContextLock) {
            _plannerThreadIdByActiveThreadId.Remove(normalizedActiveThreadId);
            _plannerThreadSeenUtcTicksByActiveThreadId.Remove(normalizedActiveThreadId);
        }
        RemovePlannerThreadContextSnapshot(normalizedActiveThreadId);
    }

    private bool TryResolvePlannerThreadContext(string activeThreadId, out string plannerThreadId) {
        plannerThreadId = string.Empty;
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            return false;
        }

        var nowUtc = DateTime.UtcNow;
        var nowTicks = nowUtc.Ticks;
        string? trackedPlannerThreadId = null;
        var evictSnapshot = false;

        lock (_toolRoutingContextLock) {
            if (_plannerThreadIdByActiveThreadId.TryGetValue(normalizedActiveThreadId, out var cachedPlannerThreadId)
                && !string.IsNullOrWhiteSpace(cachedPlannerThreadId)) {
                var cached = cachedPlannerThreadId.Trim();
                if (_plannerThreadSeenUtcTicksByActiveThreadId.TryGetValue(normalizedActiveThreadId, out var trackedTicks)
                    && TryGetUtcDateTimeFromTicks(trackedTicks, out var trackedSeenUtc)
                    && nowUtc - trackedSeenUtc <= PlannerThreadContextMaxAge) {
                    trackedPlannerThreadId = cached;
                } else {
                    _plannerThreadIdByActiveThreadId.Remove(normalizedActiveThreadId);
                    _plannerThreadSeenUtcTicksByActiveThreadId.Remove(normalizedActiveThreadId);
                    evictSnapshot = true;
                }
            }
        }

        if (evictSnapshot) {
            RemovePlannerThreadContextSnapshot(normalizedActiveThreadId);
        }

        if (string.IsNullOrWhiteSpace(trackedPlannerThreadId)) {
            if (!TryLoadPlannerThreadContextSnapshot(normalizedActiveThreadId, out var persistedPlannerThreadId, out _)) {
                return false;
            }

            trackedPlannerThreadId = persistedPlannerThreadId;
        }

        plannerThreadId = trackedPlannerThreadId.Trim();
        if (plannerThreadId.Length == 0) {
            RemovePlannerThreadContextSnapshot(normalizedActiveThreadId);
            return false;
        }

        lock (_toolRoutingContextLock) {
            _plannerThreadIdByActiveThreadId[normalizedActiveThreadId] = plannerThreadId;
            _plannerThreadSeenUtcTicksByActiveThreadId[normalizedActiveThreadId] = nowTicks;
            TrimWeightedRoutingContextsNoLock();
        }
        PersistPlannerThreadContextSnapshot(normalizedActiveThreadId, plannerThreadId, nowTicks);
        return true;
    }

    private IReadOnlyList<ToolDefinition> EnsureMinimumToolSelection(string userRequest, IReadOnlyList<ToolDefinition> allDefinitions,
        IReadOnlyList<ToolDefinition> initialSelected, int limit) {
        if (allDefinitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var selected = new List<ToolDefinition>(Math.Min(limit, allDefinitions.Count));
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < initialSelected.Count && selected.Count < limit; i++) {
            var definition = initialSelected[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name) || !selectedNames.Add(definition.Name)) {
                continue;
            }
            selected.Add(definition);
        }

        var minSelection = Math.Min(allDefinitions.Count, Math.Max(8, Math.Min(limit, 12)));
        if (selected.Count >= minSelection) {
            return selected;
        }

        var rankedFallback = new List<(ToolDefinition Definition, double Score)>(allDefinitions.Count);
        for (var i = 0; i < allDefinitions.Count; i++) {
            var definition = allDefinitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name) || selectedNames.Contains(definition.Name)) {
                continue;
            }

            var score = 0d;
            if (!string.IsNullOrWhiteSpace(userRequest)
                && userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0) {
                score += 6d;
            }

            score += ReadToolRoutingAdjustment(definition.Name);
            rankedFallback.Add((definition, score));
        }

        rankedFallback.Sort(static (left, right) => {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }
            return StringComparer.OrdinalIgnoreCase.Compare(left.Definition.Name, right.Definition.Name);
        });

        for (var i = 0; i < rankedFallback.Count && selected.Count < minSelection; i++) {
            var definition = rankedFallback[i].Definition;
            if (selectedNames.Add(definition.Name)) {
                selected.Add(definition);
            }
        }

        return selected.Count == 0 ? allDefinitions : selected;
    }

    private static List<ToolRoutingInsight> BuildModelRoutingInsights(IReadOnlyList<ToolDefinition> selectedDefs, int plannedCount) {
        var list = new List<ToolRoutingInsight>(Math.Min(12, selectedDefs.Count));
        if (selectedDefs.Count == 0) {
            return list;
        }

        for (var i = 0; i < selectedDefs.Count && i < 12; i++) {
            var name = (selectedDefs[i].Name ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            var fromPlanner = i < plannedCount;
            var confidence = i < 3 ? "high" : i < 8 ? "medium" : "low";
            var reason = fromPlanner
                ? "semantic planner selection"
                : "semantic planner backfill with routing history";
            var score = Math.Max(0.2d, 1d - (i * 0.06d));
            list.Add(new ToolRoutingInsight(
                ToolName: name,
                Confidence: confidence,
                Score: Math.Round(score, 3),
                Reason: reason,
                Strategy: ToolRoutingInsightStrategy.SemanticPlanner));
        }

        return list;
    }

    private static string BuildModelPlannerPrompt(string userRequest, IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (definitions.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(capacity: Math.Min(64_000, 4000 + (definitions.Count * 120)));
        sb.AppendLine("Select tools for the following user request.");
        sb.AppendLine("User request:");
        sb.AppendLine(userRequest.Trim());
        sb.AppendLine();
        sb.AppendLine($"Return at most {Math.Max(1, limit)} tool names.");
        sb.AppendLine("Available tools:");

        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var name = definition.Name.Trim();
            var description = (definition.Description ?? string.Empty).Trim();
            if (description.Length > 220) {
                description = description[..220].TrimEnd();
            }
            var schemaArguments = ExtractToolSchemaPropertyNames(definition, maxCount: 8, out var hasTableViewProjection);
            var requiredArguments = ExtractToolSchemaRequiredNames(definition, maxCount: 4);
            var category = ResolvePlannerCategory(definition);
            var packHint = ResolvePlannerPackHint(definition, category);
            var domainIntentFamily = ResolveDomainIntentFamily(definition);
            var plannerTags = ExtractPlannerTags(definition, maxCount: 4);
            sb.Append(i + 1).Append(". ").Append(name);
            if (description.Length > 0) {
                sb.Append(" :: ").Append(description);
            }
            if (category.Length > 0) {
                sb.Append(" | category: ").Append(category);
            }
            if (packHint.Length > 0) {
                sb.Append(" | pack: ").Append(packHint);
            }
            if (domainIntentFamily.Length > 0) {
                sb.Append(" | family: ").Append(domainIntentFamily);
            }
            if (plannerTags.Length > 0) {
                sb.Append(" | tags: ").Append(string.Join(", ", plannerTags));
            }
            if (requiredArguments.Length > 0) {
                sb.Append(" | required: ").Append(string.Join(", ", requiredArguments));
            }
            if (schemaArguments.Length > 0) {
                sb.Append(" | args: ").Append(string.Join(", ", schemaArguments));
            }
            if (hasTableViewProjection) {
                sb.Append(" | traits: table_view_projection");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string ResolvePlannerCategory(ToolDefinition definition) {
        var category = (definition.Category ?? string.Empty).Trim();
        if (category.Length > 0) {
            return category;
        }

        return (ToolSelectionMetadata.Enrich(definition, toolType: null).Category ?? string.Empty).Trim();
    }

    private static string ResolvePlannerPackHint(ToolDefinition definition, string category) {
        for (var i = 0; i < definition.Tags.Count; i++) {
            var tag = (definition.Tags[i] ?? string.Empty).Trim();
            if (!ToolRoutingTaxonomy.TryGetTagKeyValue(tag, out var tagKey, out var tagValue)
                || !string.Equals(tagKey, "pack", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var normalizedTaggedPack = NormalizePackId(tagValue);
            if (normalizedTaggedPack.Length > 0) {
                return normalizedTaggedPack;
            }
        }

        var toolName = (definition.Name ?? string.Empty).Trim();
        if (TryResolvePackHintFromToolNamePrefix(toolName, out var packHintFromPrefix)) {
            return packHintFromPrefix;
        }

        if (PackIdMatches(category, "active_directory")) {
            return "active_directory";
        }

        if (PackIdMatches(category, "eventlog")) {
            return "eventlog";
        }

        if (PackIdMatches(category, "system")) {
            return "system";
        }

        if (PackIdMatches(category, "testimox")) {
            return "testimox";
        }

        if (PackIdMatches(category, "domaindetective")) {
            return "domaindetective";
        }

        if (PackIdMatches(category, "dnsclientx")) {
            return "dnsclientx";
        }

        return string.Empty;
    }

    private static bool TryResolvePackHintFromToolNamePrefix(string toolName, out string packHint) {
        packHint = string.Empty;
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return false;
        }

        if (normalizedToolName.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("active_directory_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("adplayground_", StringComparison.OrdinalIgnoreCase)) {
            packHint = "active_directory";
            return true;
        }

        if (normalizedToolName.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("event_log_", StringComparison.OrdinalIgnoreCase)) {
            packHint = "eventlog";
            return true;
        }

        if (normalizedToolName.StartsWith("system_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("computerx_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("wsl_", StringComparison.OrdinalIgnoreCase)) {
            packHint = "system";
            return true;
        }

        if (normalizedToolName.StartsWith("testimox_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("testimo_x_", StringComparison.OrdinalIgnoreCase)) {
            packHint = "testimox";
            return true;
        }

        if (normalizedToolName.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("domain_detective_", StringComparison.OrdinalIgnoreCase)) {
            packHint = "domaindetective";
            return true;
        }

        if (normalizedToolName.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase)
            || normalizedToolName.StartsWith("dns_client_x_", StringComparison.OrdinalIgnoreCase)) {
            packHint = "dnsclientx";
            return true;
        }

        return false;
    }

    private static string[] ExtractPlannerTags(ToolDefinition definition, int maxCount) {
        if (definition.Tags.Count == 0 || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var tags = new List<string>(Math.Min(maxCount, definition.Tags.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Tags.Count && tags.Count < maxCount; i++) {
            var tag = (definition.Tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0 || !seen.Add(tag)) {
                continue;
            }

            tags.Add(tag);
        }

        return tags.Count == 0 ? Array.Empty<string>() : tags.ToArray();
    }

    private static IReadOnlyList<ToolDefinition> ParsePlannerSelectedDefinitions(string plannerText, IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (string.IsNullOrWhiteSpace(plannerText) || definitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var byName = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || string.IsNullOrWhiteSpace(definition.Name)) {
                continue;
            }

            var name = definition.Name.Trim();
            if (!byName.ContainsKey(name)) {
                byName.Add(name, definition);
            }
        }

        var selected = new List<ToolDefinition>(Math.Min(limit, byName.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = ExtractPlannerToolNames(plannerText);
        for (var i = 0; i < names.Count && selected.Count < limit; i++) {
            var requestedName = names[i];
            if (string.IsNullOrWhiteSpace(requestedName)) {
                continue;
            }

            var normalized = requestedName.Trim();
            if (!byName.TryGetValue(normalized, out var definition) || !seen.Add(definition.Name)) {
                continue;
            }

            selected.Add(definition);
        }

        return selected;
    }

    private static List<string> ExtractPlannerToolNames(string plannerText) {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var candidates = BuildPlannerJsonCandidates(plannerText);
        for (var i = 0; i < candidates.Count; i++) {
            if (!TryExtractPlannerNamesFromJson(candidates[i], names, seen)) {
                continue;
            }

            if (names.Count > 0) {
                return names;
            }
        }

        // Fallback for non-JSON planner replies: collect tool-like identifiers.
        var matches = Regex.Matches(plannerText, @"\b[a-z][a-z0-9]*(?:_[a-z0-9]+)+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < matches.Count; i++) {
            var value = (matches[i].Value ?? string.Empty).Trim();
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }
            names.Add(value);
        }

        return names;
    }

    private static List<string> BuildPlannerJsonCandidates(string plannerText) {
        var list = new List<string>();
        var normalized = (plannerText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return list;
        }

        list.Add(normalized);

        var fencedMatches = Regex.Matches(
            normalized,
            "```(?:json)?\\s*(?<json>[\\s\\S]*?)```",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < fencedMatches.Count; i++) {
            var captured = fencedMatches[i].Groups["json"].Value.Trim();
            if (captured.Length > 0) {
                list.Add(captured);
            }
        }

        AppendJsonEnvelopeCandidate(normalized, '{', '}', list);
        AppendJsonEnvelopeCandidate(normalized, '[', ']', list);
        return list;
    }

    private static void AppendJsonEnvelopeCandidate(string text, char startChar, char endChar, ICollection<string> target) {
        var start = text.IndexOf(startChar);
        var end = text.LastIndexOf(endChar);
        if (start < 0 || end <= start) {
            return;
        }

        var candidate = text.Substring(start, (end - start) + 1).Trim();
        if (candidate.Length > 1) {
            target.Add(candidate);
        }
    }

    private static bool TryExtractPlannerNamesFromJson(string candidate, ICollection<string> names, ISet<string> seen) {
        if (string.IsNullOrWhiteSpace(candidate)) {
            return false;
        }

        JsonValue? parsed;
        try {
            parsed = JsonLite.Parse(candidate);
        } catch {
            return false;
        }

        if (parsed is null) {
            return false;
        }

        var extracted = 0;
        var rootObj = parsed.AsObject();
        if (rootObj is not null) {
            extracted += AppendPlannerNamesFromObject(rootObj, names, seen);
        } else {
            var rootArray = parsed.AsArray();
            if (rootArray is not null) {
                extracted += AppendPlannerNamesFromArray(rootArray, names, seen);
            }
        }

        return extracted > 0;
    }

    private static int AppendPlannerNamesFromObject(JsonObject obj, ICollection<string> names, ISet<string> seen) {
        var added = 0;
        added += AppendPlannerNamesFromArray(obj.GetArray("tool_names"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("tools"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("selected"), names, seen);
        added += AppendPlannerNamesFromArray(obj.GetArray("recommended"), names, seen);

        var resultObj = obj.GetObject("result");
        if (resultObj is not null) {
            added += AppendPlannerNamesFromObject(resultObj, names, seen);
        }

        return added;
    }

    private static int AppendPlannerNamesFromArray(JsonArray? array, ICollection<string> names, ISet<string> seen) {
        if (array is null || array.Count == 0) {
            return 0;
        }

        var added = 0;
        for (var i = 0; i < array.Count; i++) {
            var item = array[i];
            var asName = item.AsString();
            if (TryAddPlannerName(asName, names, seen)) {
                added++;
                continue;
            }

            var asObj = item.AsObject();
            if (asObj is null) {
                continue;
            }

            if (TryAddPlannerName(asObj.GetString("name"), names, seen)) {
                added++;
            }
            if (TryAddPlannerName(asObj.GetString("tool"), names, seen)) {
                added++;
            }
            if (TryAddPlannerName(asObj.GetString("tool_name"), names, seen)) {
                added++;
            }
        }

        return added;
    }

    private static bool TryAddPlannerName(string? value, ICollection<string> names, ISet<string> seen) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0 || !seen.Add(normalized)) {
            return false;
        }

        names.Add(normalized);
        return true;
    }

    private static bool ShouldSkipWeightedRouting(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return true;
        }

        // For explicit action selections, keep all tools available so execution can proceed immediately.
        if (LooksLikeActionSelectionPayload(normalized)) {
            return true;
        }

        // Keep follow-up turns unconstrained: short continuation prompts (for example "1" or other compact follow-up text)
        // should have immediate access to the full toolset.
        if (LooksLikeContinuationFollowUp(normalized)) {
            return true;
        }

        return false;
    }

    private static bool LooksLikeContinuationFollowUp(string userRequest) {
        return LooksLikeFollowUpShape(userRequest, ContinuationFollowUpQuestionCharLimit);
    }

    private sealed class ToolRoutingStats {
        public int Invocations { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public long LastUsedUtcTicks { get; set; }
        public long LastSuccessUtcTicks { get; set; }
    }

    private readonly record struct ToolScore(
        ToolDefinition Definition,
        double Score,
        bool DirectNameMatch,
        int TokenHits,
        double Adjustment);

    private enum ToolRoutingInsightStrategy {
        Unknown = 0,
        WeightedHeuristic = 1,
        ContinuationSubset = 2,
        SemanticPlanner = 3
    }

    private readonly record struct ToolRoutingInsight(
        string ToolName,
        string Confidence,
        double Score,
        string Reason,
        ToolRoutingInsightStrategy Strategy) {
        public ToolRoutingInsight(string ToolName, string Confidence, double Score, string Reason)
            : this(ToolName, Confidence, Score, Reason, ToolRoutingInsightStrategy.Unknown) {
        }
    }

    private readonly record struct ToolRetryProfile(
        int MaxAttempts,
        int DelayBaseMs,
        bool RetryOnTimeout,
        bool RetryOnTransport);

    internal void RememberPlannerThreadContextForTesting(string activeThreadId, string plannerThreadId, long seenUtcTicks) {
        RememberPlannerThreadContext(activeThreadId, plannerThreadId, seenUtcTicks);
    }

    internal bool TryResolvePlannerThreadContextForTesting(string activeThreadId, out string plannerThreadId) {
        return TryResolvePlannerThreadContext(activeThreadId, out plannerThreadId);
    }

}

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
using IntelligenceX.Chat.App.Markdown;
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
    private const int SafeDefaultMaxToolRounds = 8;
    private const bool SafeDefaultParallelTools = true;
    private const int SafeDefaultTurnTimeoutSeconds = 180;
    private const int SafeDefaultToolTimeoutSeconds = 60;

    private void ReplaceLastAssistantText(string text) {
        ReplaceLastAssistantText(GetActiveConversation(), text);
    }

    private static void ReplaceLastAssistantText(ConversationRuntime conversation, string text) {
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            if (string.Equals(conversation.Messages[i].Role, "Assistant", StringComparison.Ordinal)) {
                conversation.Messages[i] = ("Assistant", text, conversation.Messages[i].Time);
                return;
            }
        }
    }

    private void UpdateToolCatalog(ToolDefinitionDto[] tools) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools) {
            if (string.IsNullOrWhiteSpace(tool.Name)) {
                continue;
            }

            var name = tool.Name.Trim();
            seen.Add(name);
            _toolDescriptions[name] = tool.Description ?? string.Empty;
            _toolDisplayNames[name] = string.IsNullOrWhiteSpace(tool.DisplayName) ? FormatToolDisplayName(name) : tool.DisplayName.Trim();
            _toolCategories[name] = string.IsNullOrWhiteSpace(tool.Category) ? InferToolCategory(name) : tool.Category.Trim();
            if (!string.IsNullOrWhiteSpace(tool.PackId)) {
                _toolPackIds[name] = tool.PackId.Trim();
            } else {
                _toolPackIds.Remove(name);
            }
            if (!string.IsNullOrWhiteSpace(tool.PackName)) {
                _toolPackNames[name] = ResolvePackDisplayName(tool.PackId, tool.PackName);
            } else {
                _toolPackNames.Remove(name);
            }
            _toolTags[name] = NormalizeTags(tool.Tags);
            _toolParameters[name] = tool.Parameters is { Length: > 0 } parameters
                ? parameters
                : Array.Empty<ToolParameterDto>();
            if (!_toolStates.ContainsKey(name)) {
                _toolStates[name] = true;
            }
        }

        var existing = new List<string>(_toolStates.Keys);
        foreach (var toolName in existing) {
            if (!seen.Contains(toolName)) {
                _toolStates.Remove(toolName);
                _toolDescriptions.Remove(toolName);
                _toolDisplayNames.Remove(toolName);
                _toolCategories.Remove(toolName);
                _toolPackIds.Remove(toolName);
                _toolPackNames.Remove(toolName);
                _toolTags.Remove(toolName);
                _toolParameters.Remove(toolName);
                _toolRoutingConfidence.Remove(toolName);
                _toolRoutingReason.Remove(toolName);
                _toolRoutingScore.Remove(toolName);
            }
        }
    }

    private void SetToolEnabled(string toolName, bool enabled) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return;
        }

        var key = toolName.Trim();
        if (!_toolStates.ContainsKey(key)) {
            _toolStates[key] = true;
        }
        _toolStates[key] = enabled;
    }

    private bool SetToolPackEnabled(string packId, bool enabled) {
        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        var changed = false;
        var names = new List<string>(_toolStates.Keys);
        for (var i = 0; i < names.Count; i++) {
            var toolName = names[i];
            var toolPackId = ResolveToolPackId(toolName);
            if (!string.Equals(NormalizePackId(toolPackId), normalizedPackId, StringComparison.Ordinal)) {
                continue;
            }

            if (_toolStates[toolName] == enabled) {
                continue;
            }

            _toolStates[toolName] = enabled;
            changed = true;
        }

        return changed;
    }

    private string ResolveToolPackId(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return "other";
        }

        if (_toolPackIds.TryGetValue(toolName, out var explicitPackId) && !string.IsNullOrWhiteSpace(explicitPackId)) {
            return explicitPackId.Trim();
        }

        if (_toolCategories.TryGetValue(toolName, out var category) && !string.IsNullOrWhiteSpace(category)) {
            var fromCategory = MapCategoryToPackId(category);
            if (!string.IsNullOrWhiteSpace(fromCategory)) {
                return fromCategory;
            }
        }

        var lower = toolName.Trim().ToLowerInvariant();
        return lower switch {
            _ when lower.StartsWith("ad_", StringComparison.Ordinal) => "ad",
            _ when lower.StartsWith("eventlog_", StringComparison.Ordinal) => "eventlog",
            _ when lower.StartsWith("system_", StringComparison.Ordinal) => "system",
            _ when lower.StartsWith("wsl_", StringComparison.Ordinal) => "system",
            _ when lower.StartsWith("fs_", StringComparison.Ordinal) => "fs",
            _ when lower.StartsWith("email_", StringComparison.Ordinal) => "email",
            _ when lower.StartsWith("testimox_", StringComparison.Ordinal) => "testimox",
            _ when lower.StartsWith("reviewer_setup_", StringComparison.Ordinal) => "reviewer-setup",
            _ when lower.StartsWith("export_", StringComparison.Ordinal) => "export",
            _ => "other"
        };
    }

    private static string MapCategoryToPackId(string category) {
        return NormalizePackId(category) switch {
            "ad" => "ad",
            "active-directory" => "ad",
            "eventlog" => "eventlog",
            "event-log" => "eventlog",
            "fs" => "fs",
            "file-system" => "fs",
            "system" => "system",
            "email" => "email",
            "testimox" => "testimox",
            "reviewer-setup" => "reviewer-setup",
            "export" => "export",
            _ => string.Empty
        };
    }

    private static string NormalizePackId(string? packId) {
        var normalized = (packId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized
            .Replace("_", "-", StringComparison.Ordinal)
            .Replace(".", "-", StringComparison.Ordinal)
            .Replace(" ", "-", StringComparison.Ordinal);

        if (normalized.StartsWith("ix-", StringComparison.Ordinal)) {
            normalized = normalized[3..];
        } else if (normalized.StartsWith("intelligencex-", StringComparison.Ordinal)) {
            normalized = normalized["intelligencex-".Length..];
        }

        return normalized switch {
            "active-directory" => "ad",
            "activedirectory" => "ad",
            "adplayground" => "ad",
            "computerx" => "system",
            "event-log" => "eventlog",
            "filesystem" => "fs",
            "file-system" => "fs",
            "reviewersetup" => "reviewer-setup",
            "reviewer-setup" => "reviewer-setup",
            _ => normalized
        };
    }

    private async Task SetTimeModeAsync(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }

        var normalized = value.Trim();
        _timestampMode = ResolveTimestampMode(normalized);
        _timestampFormat = ResolveTimestampFormat(normalized);
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SetThemePresetAsync(string value) {
        var normalizedTheme = NormalizeTheme(value) ?? "default";
        if (string.Equals(_themePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_appState.ThemePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(_sessionThemeOverride)) {
            return;
        }

        _sessionThemeOverride = null;
        _themePreset = normalizedTheme;
        _appState.ThemePreset = normalizedTheme;
        await ApplyThemeFromStateAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private ChatRequestOptions? BuildChatRequestOptions() {
        var disabled = new List<string>();
        if (_toolStates.Count > 0) {
            foreach (var pair in _toolStates) {
                if (!pair.Value) {
                    disabled.Add(pair.Key);
                }
            }
        }
        if (disabled.Count > 0) {
            disabled.Sort(StringComparer.OrdinalIgnoreCase);
        }

        var effectiveMaxToolRounds = _autonomyMaxToolRounds
            ?? NormalizeAutonomyInt(_sessionPolicy?.MaxToolRounds, min: 1, max: 64)
            ?? SafeDefaultMaxToolRounds;

        var effectiveParallelTools = _autonomyParallelTools
            ?? _sessionPolicy?.ParallelTools
            ?? SafeDefaultParallelTools;

        var effectiveTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds
            ?? NormalizePositiveTimeout(_sessionPolicy?.TurnTimeoutSeconds)
            ?? SafeDefaultTurnTimeoutSeconds;

        var effectiveToolTimeoutSeconds = _autonomyToolTimeoutSeconds
            ?? NormalizePositiveTimeout(_sessionPolicy?.ToolTimeoutSeconds)
            ?? SafeDefaultToolTimeoutSeconds;

        return new ChatRequestOptions {
            DisabledTools = disabled.Count == 0 ? null : disabled.ToArray(),
            MaxToolRounds = effectiveMaxToolRounds,
            ParallelTools = effectiveParallelTools,
            TurnTimeoutSeconds = effectiveTurnTimeoutSeconds,
            ToolTimeoutSeconds = effectiveToolTimeoutSeconds,
            WeightedToolRouting = _autonomyWeightedToolRouting,
            MaxCandidateTools = _autonomyMaxCandidateTools
        };
    }

    private async Task SetAutonomyOverridesAsync(string? maxRounds, string? parallelMode, string? turnTimeout, string? toolTimeout,
        string? weightedRouting, string? maxCandidates) {
        _autonomyMaxToolRounds = ParseAutonomyInt(maxRounds, min: 1, max: 64);
        _autonomyParallelTools = ParseAutonomyParallelMode(parallelMode);
        _autonomyTurnTimeoutSeconds = ParseAutonomyInt(turnTimeout, min: 0, max: 3600);
        _autonomyToolTimeoutSeconds = ParseAutonomyInt(toolTimeout, min: 0, max: 3600);
        _autonomyWeightedToolRouting = ParseAutonomyParallelMode(weightedRouting);
        _autonomyMaxCandidateTools = ParseAutonomyInt(maxCandidates, min: 0, max: 64);

        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ResetAutonomyOverridesAsync() {
        _autonomyMaxToolRounds = null;
        _autonomyParallelTools = null;
        _autonomyTurnTimeoutSeconds = null;
        _autonomyToolTimeoutSeconds = null;
        _autonomyWeightedToolRouting = null;
        _autonomyMaxCandidateTools = null;

        _appState.AutonomyMaxToolRounds = null;
        _appState.AutonomyParallelTools = null;
        _appState.AutonomyTurnTimeoutSeconds = null;
        _appState.AutonomyToolTimeoutSeconds = null;
        _appState.AutonomyWeightedToolRouting = null;
        _appState.AutonomyMaxCandidateTools = null;

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private static int? ParseAutonomyInt(string? raw, int min, int max) {
        var text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return null;
        }

        if (parsed < min || parsed > max) {
            return null;
        }

        return parsed;
    }

    private static bool? ParseAutonomyParallelMode(string? raw) {
        var text = (raw ?? string.Empty).Trim();
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return null;
    }

    private static int? NormalizePositiveTimeout(int? value) {
        if (!value.HasValue || value.Value <= 0) {
            return null;
        }

        return value.Value;
    }

    private List<string> BuildMissingOnboardingFields() {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(GetEffectiveUserName())) {
            missing.Add("userName");
        }
        if (string.IsNullOrWhiteSpace(GetEffectiveAssistantPersona())) {
            missing.Add("assistantPersona");
        }
        var effectiveTheme = GetEffectiveThemePreset();
        if (string.IsNullOrWhiteSpace(effectiveTheme)
            || (!_appState.OnboardingCompleted && string.Equals(effectiveTheme, "default", StringComparison.OrdinalIgnoreCase))) {
            missing.Add("themePreset");
        }
        return missing;
    }

    private string BuildKickoffRequestText(IReadOnlyList<string> missingFields) {
        return PromptMarkdownBuilder.BuildKickoffRequest(missingFields);
    }

    private string BuildRequestTextForService(string userText) {
        var activeConversation = GetActiveConversation();
        var effectivePersona = GetEffectiveAssistantPersona();
        var effectiveName = GetEffectiveUserName();
        var onboardingInProgress = !_appState.OnboardingCompleted;
        IReadOnlyList<string> missingFields = onboardingInProgress ? BuildMissingOnboardingFields() : Array.Empty<string>();
        var localContextLines = BuildLocalContextFallbackLines(activeConversation, userText);
        var memoryContextLines = BuildPersistentMemoryContextLines(userText);
        return PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: effectiveName,
            effectivePersona: effectivePersona,
            onboardingInProgress: onboardingInProgress,
            missingOnboardingFields: missingFields,
            includeLiveProfileUpdates: MightContainProfileUpdateCue(userText),
            executionBehaviorPrompt: PromptAssets.GetExecutionBehaviorPrompt(),
            localContextLines: localContextLines,
            persistentMemoryLines: memoryContextLines,
            persistentMemoryPrompt: _persistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty);
    }

    private static IReadOnlyList<string> BuildLocalContextFallbackLines(ConversationRuntime conversation, string userText) {
        ArgumentNullException.ThrowIfNull(conversation);

        // Prefer local-history fallback when no remote thread exists or the
        // user asks context-dependent follow-ups ("check this", "same", etc.).
        var needsFallback = string.IsNullOrWhiteSpace(conversation.ThreadId)
                            || LooksLikeContextDependentFollowUp(userText);
        if (!needsFallback) {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var remaining = 6;
        for (var i = conversation.Messages.Count - 1; i >= 0 && remaining > 0; i--) {
            var message = conversation.Messages[i];
            if (string.IsNullOrWhiteSpace(message.Text)) {
                continue;
            }

            if (string.Equals(message.Role, "Tools", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "System", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase)
                && string.Equals(message.Text.Trim(), (userText ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var compact = CompactMessageForContext(message.Text);
            if (compact.Length == 0) {
                continue;
            }

            var role = string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                ? "Assistant"
                : "User";
            lines.Add(role + ": " + compact);
            remaining--;
        }

        lines.Reverse();
        return lines;
    }

    private IReadOnlyList<string> BuildPersistentMemoryContextLines(string userText) {
        if (!_persistentMemoryEnabled) {
            return Array.Empty<string>();
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        _appState.MemoryFacts = facts;
        if (facts.Count == 0) {
            return Array.Empty<string>();
        }

        var lowerText = (userText ?? string.Empty).Trim().ToLowerInvariant();
        if (lowerText.Length == 0) {
            return BuildPersistentMemoryLinesForEmptyQuery(facts);
        }

        var userTokens = TokenizeMemorySemanticText(lowerText);
        var scoredFacts = new List<ScoredMemoryFact>(facts.Count);

        foreach (var fact in facts) {
            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            var normalizedFactText = text.ToLowerInvariant();
            var score = fact.Weight * 1.4d;
            var semanticHits = 0;

            if (lowerText.Length > 0
                && (lowerText.Contains(normalizedFactText, StringComparison.Ordinal)
                    || normalizedFactText.Contains(lowerText, StringComparison.Ordinal))) {
                score += 2.25d;
                semanticHits += 2;
            }

            var factTokens = TokenizeMemorySemanticText(normalizedFactText);
            var factTokenOverlap = CountTokenOverlap(userTokens, factTokens);
            if (factTokenOverlap > 0) {
                score += Math.Min(5d, factTokenOverlap * 1.35d);
                semanticHits += factTokenOverlap;
            }

            var tags = fact.Tags ?? Array.Empty<string>();
            for (var i = 0; i < tags.Length; i++) {
                var tag = (tags[i] ?? string.Empty).Trim().ToLowerInvariant();
                if (tag.Length == 0) {
                    continue;
                }

                if (lowerText.Length > 0 && lowerText.Contains(tag, StringComparison.Ordinal)) {
                    score += 1.1d;
                    semanticHits++;
                }

                var tagTokens = TokenizeMemorySemanticText(tag);
                var tagTokenOverlap = CountTokenOverlap(userTokens, tagTokens);
                if (tagTokenOverlap > 0) {
                    score += Math.Min(2.5d, tagTokenOverlap * 0.75d);
                    semanticHits += tagTokenOverlap;
                }
            }

            var updatedUtc = EnsureUtc(fact.UpdatedUtc == default ? DateTime.UtcNow : fact.UpdatedUtc);
            var ageHours = (DateTime.UtcNow - updatedUtc).TotalHours;
            if (ageHours <= 24d) {
                score += 0.9d;
            } else if (ageHours <= 72d) {
                score += 0.55d;
            } else if (ageHours <= 168d) {
                score += 0.2d;
            }

            scoredFacts.Add(new ScoredMemoryFact(text, fact.Weight, updatedUtc, score, semanticHits));
        }

        if (scoredFacts.Count == 0) {
            return Array.Empty<string>();
        }

        scoredFacts.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            var weightCompare = b.Weight.CompareTo(a.Weight);
            if (weightCompare != 0) {
                return weightCompare;
            }

            return b.UpdatedUtc.CompareTo(a.UpdatedUtc);
        });

        var lines = new List<string>(Math.Min(10, scoredFacts.Count));
        for (var i = 0; i < scoredFacts.Count && lines.Count < 10; i++) {
            var fact = scoredFacts[i];
            var include = i < 2
                          || fact.Score >= 3.2d
                          || fact.SemanticHits >= 2
                          || (fact.SemanticHits >= 1 && fact.Score >= 2.5d);
            if (!include) {
                continue;
            }

            lines.Add(fact.Text);
        }

        if (lines.Count == 0) {
            var fallbackCount = Math.Min(3, scoredFacts.Count);
            for (var i = 0; i < fallbackCount; i++) {
                lines.Add(scoredFacts[i].Text);
            }
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private static IReadOnlyList<string> BuildPersistentMemoryLinesForEmptyQuery(List<ChatMemoryFactState> facts) {
        if (facts.Count == 0) {
            return Array.Empty<string>();
        }

        var ordered = new List<ChatMemoryFactState>(facts);
        ordered.Sort(static (a, b) => {
            var weightCompare = b.Weight.CompareTo(a.Weight);
            if (weightCompare != 0) {
                return weightCompare;
            }

            return b.UpdatedUtc.CompareTo(a.UpdatedUtc);
        });

        var lines = new List<string>(8);
        for (var i = 0; i < ordered.Count && lines.Count < 8; i++) {
            var text = (ordered[i].Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            if (ordered[i].Weight >= 3 || lines.Count < 3) {
                lines.Add(text);
            }
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private static HashSet<string> TokenizeMemorySemanticText(string text) {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) {
            return tokens;
        }

        var parts = Regex.Split(text, @"[^a-z0-9_]+");
        for (var i = 0; i < parts.Length; i++) {
            var token = (parts[i] ?? string.Empty).Trim().ToLowerInvariant();
            if (token.Length < 3 || MemoryTokenStopWords.Contains(token)) {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static int CountTokenOverlap(IReadOnlySet<string> left, IReadOnlySet<string> right) {
        if (left.Count == 0 || right.Count == 0) {
            return 0;
        }

        var overlap = 0;
        if (left.Count <= right.Count) {
            foreach (var token in left) {
                if (right.Contains(token)) {
                    overlap++;
                }
            }

            return overlap;
        }

        foreach (var token in right) {
            if (left.Contains(token)) {
                overlap++;
            }
        }

        return overlap;
    }

    private static readonly HashSet<string> MemoryTokenStopWords = new(StringComparer.OrdinalIgnoreCase) {
        "the", "and", "with", "from", "that", "this", "for", "you", "your", "have", "show", "give", "list",
        "check", "please", "about", "into", "just", "today", "need", "want", "when", "what", "where", "then",
        "them", "they", "their", "there", "after", "before", "will", "should", "would", "could", "also", "been",
        "being", "while", "does", "did", "done", "using", "used", "more", "same", "again"
    };

    private readonly record struct ScoredMemoryFact(
        string Text,
        int Weight,
        DateTime UpdatedUtc,
        double Score,
        int SemanticHits);

    private static List<ChatMemoryFactState> NormalizeMemoryFacts(List<ChatMemoryFactState>? facts) {
        if (facts is null || facts.Count == 0) {
            return new List<ChatMemoryFactState>();
        }

        var normalized = new List<ChatMemoryFactState>(facts.Count);
        var seenByFact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts) {
            if (fact is null) {
                continue;
            }

            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            if (!seenByFact.Add(text)) {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(fact.Id) ? Guid.NewGuid().ToString("N") : fact.Id.Trim();
            var weight = Math.Clamp(fact.Weight, 1, 5);
            var tags = NormalizeMemoryTags(fact.Tags);
            var updatedUtc = fact.UpdatedUtc == default ? DateTime.UtcNow : EnsureUtc(fact.UpdatedUtc);

            normalized.Add(new ChatMemoryFactState {
                Id = id,
                Fact = text,
                Weight = weight,
                Tags = tags,
                UpdatedUtc = updatedUtc
            });
        }

        normalized.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        if (normalized.Count > 120) {
            normalized.RemoveRange(120, normalized.Count - 120);
        }

        return normalized;
    }

    private static string[] NormalizeMemoryTags(string[]? tags) {
        if (tags is null || tags.Length == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(tags.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tags.Length; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0 || tag.Length > 40) {
                continue;
            }

            if (seen.Add(tag)) {
                list.Add(tag);
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }

    private async Task SetPersistentMemoryEnabledAsync(bool enabled) {
        _persistentMemoryEnabled = enabled;
        _appState.PersistentMemoryEnabled = enabled;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task AddMemoryFactAsync(string? factText, int weight = 3, string[]? tags = null) {
        var text = (factText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return;
        }

        if (!_persistentMemoryEnabled) {
            _persistentMemoryEnabled = true;
            _appState.PersistentMemoryEnabled = true;
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        var changed = UpsertMemoryFact(facts, text, weight, tags);
        if (!changed) {
            return;
        }

        _appState.MemoryFacts = facts;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task RemoveMemoryFactAsync(string? memoryId) {
        var id = (memoryId ?? string.Empty).Trim();
        if (id.Length == 0) {
            return;
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        var removed = facts.RemoveAll(fact => string.Equals(fact.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0) {
            return;
        }

        _appState.MemoryFacts = facts;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ClearPersistentMemoryAsync() {
        if (_appState.MemoryFacts.Count == 0) {
            return;
        }

        _appState.MemoryFacts = new List<ChatMemoryFactState>();
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task<bool> ApplyMemoryUpdateAsync(AssistantMemoryUpdate update) {
        if (!_persistentMemoryEnabled) {
            return false;
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        var changed = false;

        if (update.DeleteFacts is { Count: > 0 }) {
            for (var i = 0; i < update.DeleteFacts.Count; i++) {
                var candidate = (update.DeleteFacts[i] ?? string.Empty).Trim();
                if (candidate.Length == 0) {
                    continue;
                }

                var removed = facts.RemoveAll(fact =>
                    string.Equals(fact.Id, candidate, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fact.Fact, candidate, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) {
                    changed = true;
                }
            }
        }

        if (update.Upserts is { Count: > 0 }) {
            for (var i = 0; i < update.Upserts.Count; i++) {
                var upsert = update.Upserts[i];
                if (upsert is null || string.IsNullOrWhiteSpace(upsert.Fact)) {
                    continue;
                }

                if (UpsertMemoryFact(facts, upsert.Fact, upsert.Weight, upsert.Tags)) {
                    changed = true;
                }
            }
        }

        if (!changed) {
            return false;
        }

        _appState.MemoryFacts = facts;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
        return true;
    }

    private static bool UpsertMemoryFact(List<ChatMemoryFactState> facts, string? factText, int weight, string[]? tags) {
        var text = (factText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        var normalizedTags = NormalizeMemoryTags(tags);
        var clampedWeight = Math.Clamp(weight, 1, 5);
        var now = DateTime.UtcNow;

        for (var i = 0; i < facts.Count; i++) {
            if (!string.Equals(facts[i].Fact, text, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            facts[i].Weight = clampedWeight;
            facts[i].Tags = normalizedTags;
            facts[i].UpdatedUtc = now;
            return true;
        }

        facts.Add(new ChatMemoryFactState {
            Id = Guid.NewGuid().ToString("N"),
            Fact = text,
            Weight = clampedWeight,
            Tags = normalizedTags,
            UpdatedUtc = now
        });

        if (facts.Count > 120) {
            facts.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            facts.RemoveRange(120, facts.Count - 120);
        }

        return true;
    }

    private static bool LooksLikeContextDependentFollowUp(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        return text.Contains("check this", StringComparison.OrdinalIgnoreCase)
               || text.Contains("check that", StringComparison.OrdinalIgnoreCase)
               || text.Contains("that one", StringComparison.OrdinalIgnoreCase)
               || text.Contains("same", StringComparison.OrdinalIgnoreCase)
               || text.Contains("again", StringComparison.OrdinalIgnoreCase)
               || text.Contains("as above", StringComparison.OrdinalIgnoreCase)
               || text.Contains("this please", StringComparison.OrdinalIgnoreCase)
               || text.Equals("ok?", StringComparison.OrdinalIgnoreCase);
    }

    private static string CompactMessageForContext(string text) {
        var normalized = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length > 220) {
            normalized = normalized[..220].TrimEnd() + "...";
        }

        return normalized;
    }

    private static bool MightContainProfileUpdateCue(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (UserNameIntentRegex.IsMatch(normalized)
            || ThemeIntentRegex.IsMatch(normalized)
            || ThemeUseIntentRegex.IsMatch(normalized)
            || PersonaIntentRegex.IsMatch(normalized)
            || PersonaUseIntentRegex.IsMatch(normalized)) {
            return true;
        }

        return normalized.Contains("you can be", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("be more", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("analyst", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("concise", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("optimistic", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("funny", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("humor", StringComparison.OrdinalIgnoreCase);
    }

    private async Task ApplyUserProfileIntentAsync(string userText) {
        if (TryExtractMemoryIntent(userText, out var memoryFact)) {
            await AddMemoryFactAsync(memoryFact, weight: 3, tags: new[] { "user-intent" }).ConfigureAwait(false);
        }

        var intent = ParseUserProfileIntent(userText);
        if (!intent.HasUserName && !intent.HasAssistantPersona && !intent.HasThemePreset) {
            return;
        }

        if (intent.Scope == ProfileUpdateScope.Unspecified) {
            return;
        }

        var update = new OnboardingProfileUpdate {
            Scope = intent.Scope,
            HasUserName = intent.HasUserName,
            UserName = intent.UserName,
            HasAssistantPersona = intent.HasAssistantPersona,
            AssistantPersona = intent.AssistantPersona,
            HasThemePreset = intent.HasThemePreset,
            ThemePreset = intent.ThemePreset
        };

        _ = await ApplyProfileUpdateAsync(update, autoCompleteOnboardingForProfileScope: true).ConfigureAwait(false);
    }

    private static bool TryExtractMemoryIntent(string userText, out string? memoryFact) {
        memoryFact = null;
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (TryExtractMemoryFactFromRegex(MemoryRememberIntentRegex, normalized, out memoryFact)) {
            return true;
        }

        if (TryExtractMemoryFactFromRegex(MemoryFutureIntentRegex, normalized, out memoryFact)) {
            return true;
        }

        return false;
    }

    private static bool TryExtractMemoryFactFromRegex(Regex regex, string text, out string? memoryFact) {
        memoryFact = null;
        var match = regex.Match(text);
        if (!match.Success) {
            return false;
        }

        var group = match.Groups["value"];
        if (!group.Success) {
            return false;
        }

        var candidate = group.Value.Trim().Trim('.', '!', '?', ';', ':');
        if (candidate.Length < 6) {
            return false;
        }

        if (candidate.StartsWith("to ", StringComparison.OrdinalIgnoreCase)) {
            // Avoid storing imperative tasks accidentally.
            return false;
        }

        if (candidate.Length > 220) {
            candidate = candidate[..220].TrimEnd();
        }

        memoryFact = candidate;
        return true;
    }

    private UserProfileIntent ParseUserProfileIntent(string userText) {
        var intent = new UserProfileIntent();
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return intent;
        }

        if (TryMatchValue(UserNameIntentRegex, normalized, out var name)) {
            intent.UserName = name;
            intent.HasUserName = true;
        }

        if (TryMatchValue(ThemeIntentRegex, normalized, out var theme) || TryMatchValue(ThemeUseIntentRegex, normalized, out theme)) {
            intent.ThemePreset = theme;
            intent.HasThemePreset = true;
        }

        string? persona = null;
        if (TryMatchValue(PersonaIntentRegex, normalized, out var explicitPersona)) {
            persona = explicitPersona;
        } else if (TryMatchValue(PersonaUseIntentRegex, normalized, out var usePersona)) {
            persona = usePersona;
        } else {
            persona = TryBuildPersonaFromToneHints(normalized);
        }

        if (!string.IsNullOrWhiteSpace(persona)) {
            intent.AssistantPersona = persona;
            intent.HasAssistantPersona = true;
        }

        intent.Scope = DetectProfileUpdateScope(normalized);
        return intent;
    }

    private static bool TryMatchValue(Regex regex, string input, out string? value) {
        value = null;
        var match = regex.Match(input);
        if (!match.Success) {
            return false;
        }

        var group = match.Groups["value"];
        if (!group.Success) {
            return false;
        }

        value = group.Value.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private string? TryBuildPersonaFromToneHints(string text) {
        if (!LooksLikePersonaPreferenceText(text)) {
            return null;
        }

        var role = "assistant";
        if (text.Contains("security analyst", StringComparison.OrdinalIgnoreCase)) {
            role = "security analyst";
        } else if (text.Contains("analyst", StringComparison.OrdinalIgnoreCase)) {
            role = "analyst";
        } else if (text.Contains("ad engineer", StringComparison.OrdinalIgnoreCase)) {
            role = "AD engineer";
        } else if (text.Contains("engineer", StringComparison.OrdinalIgnoreCase)) {
            role = "engineer";
        } else if (!string.IsNullOrWhiteSpace(GetEffectiveAssistantPersona())) {
            role = NormalizePersonaRole(GetEffectiveAssistantPersona()!, GetEffectiveAssistantPersona()!);
        }

        var traits = CollectPersonaTraits(text);
        if (traits.Count == 0) {
            return null;
        }

        return role + " with " + JoinTraits(traits) + ".";
    }

    private string? GetEffectiveUserName() {
        return !string.IsNullOrWhiteSpace(_sessionUserNameOverride) ? _sessionUserNameOverride : _appState.UserName;
    }

    private string? GetEffectiveAssistantPersona() {
        return !string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride) ? _sessionAssistantPersonaOverride : _appState.AssistantPersona;
    }

    private string GetEffectiveThemePreset() {
        return NormalizeTheme(_sessionThemeOverride) ?? _themePreset;
    }

    private static ProfileUpdateScope DetectProfileUpdateScope(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return ProfileUpdateScope.Unspecified;
        }

        var hasSession = SessionScopeIntentRegex.IsMatch(normalized);
        var hasProfile = ProfileScopeIntentRegex.IsMatch(normalized);
        if (hasSession && !hasProfile) {
            return ProfileUpdateScope.Session;
        }

        if (hasProfile && !hasSession) {
            return ProfileUpdateScope.Profile;
        }

        if (hasSession && hasProfile) {
            var sessionIndex = normalized.IndexOf("session", StringComparison.OrdinalIgnoreCase);
            var profileIndex = normalized.IndexOf("default", StringComparison.OrdinalIgnoreCase);
            if (profileIndex < 0) {
                profileIndex = normalized.IndexOf("save", StringComparison.OrdinalIgnoreCase);
            }
            if (profileIndex >= 0 && (sessionIndex < 0 || profileIndex > sessionIndex)) {
                return ProfileUpdateScope.Profile;
            }
            return ProfileUpdateScope.Session;
        }

        return ProfileUpdateScope.Unspecified;
    }

    private static ProfileUpdateScope ParseProfileUpdateScope(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return ProfileUpdateScope.Unspecified;
        }

        if (normalized.Equals("session", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temporary", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("temp", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Session;
        }

        if (normalized.Equals("profile", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("default", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("saved", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("persistent", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("permanent", StringComparison.OrdinalIgnoreCase)) {
            return ProfileUpdateScope.Profile;
        }

        return ProfileUpdateScope.Unspecified;
    }

    private async Task<bool> ApplyProfileUpdateAsync(OnboardingProfileUpdate update, bool autoCompleteOnboardingForProfileScope) {
        var scope = update.Scope == ProfileUpdateScope.Unspecified
            ? ProfileUpdateScope.Profile
            : update.Scope;
        var persistProfile = scope == ProfileUpdateScope.Profile;
        var changed = false;
        var effectiveThemeBefore = GetEffectiveThemePreset();

        if (update.HasUserName) {
            var nextName = NormalizeUserNameValue(update.UserName);
            if (persistProfile) {
                if (!string.Equals(_appState.UserName, nextName, StringComparison.Ordinal)) {
                    _appState.UserName = nextName;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(_sessionUserNameOverride)) {
                    _sessionUserNameOverride = null;
                    changed = true;
                }
            } else if (!string.Equals(_sessionUserNameOverride, nextName, StringComparison.Ordinal)) {
                _sessionUserNameOverride = nextName;
                changed = true;
            }
        }

        if (update.HasAssistantPersona) {
            var nextPersona = NormalizeAssistantPersonaValue(update.AssistantPersona);
            if (persistProfile) {
                if (!string.Equals(_appState.AssistantPersona, nextPersona, StringComparison.Ordinal)) {
                    _appState.AssistantPersona = nextPersona;
                    changed = true;
                }
                if (!string.IsNullOrWhiteSpace(_sessionAssistantPersonaOverride)) {
                    _sessionAssistantPersonaOverride = null;
                    changed = true;
                }
            } else if (!string.Equals(_sessionAssistantPersonaOverride, nextPersona, StringComparison.Ordinal)) {
                _sessionAssistantPersonaOverride = nextPersona;
                changed = true;
            }
        }

        if (update.HasThemePreset) {
            var normalizedTheme = NormalizeTheme(update.ThemePreset);
            if (!string.IsNullOrWhiteSpace(normalizedTheme)) {
                if (persistProfile) {
                    if (!string.Equals(_appState.ThemePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)) {
                        _appState.ThemePreset = normalizedTheme;
                        changed = true;
                    }
                    if (!string.Equals(_themePreset, normalizedTheme, StringComparison.OrdinalIgnoreCase)) {
                        _themePreset = normalizedTheme;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(_sessionThemeOverride)) {
                        _sessionThemeOverride = null;
                        changed = true;
                    }
                } else if (!string.Equals(_sessionThemeOverride, normalizedTheme, StringComparison.OrdinalIgnoreCase)) {
                    _sessionThemeOverride = normalizedTheme;
                    changed = true;
                }
            }
        }

        if (persistProfile && update.HasOnboardingCompleted && _appState.OnboardingCompleted != update.OnboardingCompleted) {
            _appState.OnboardingCompleted = update.OnboardingCompleted;
            changed = true;
        }

        if (persistProfile && autoCompleteOnboardingForProfileScope && !_appState.OnboardingCompleted && BuildMissingOnboardingFields().Count == 0) {
            _appState.OnboardingCompleted = true;
            changed = true;
        }

        if (!changed) {
            return false;
        }

        var effectiveThemeAfter = GetEffectiveThemePreset();
        if (!string.Equals(effectiveThemeBefore, effectiveThemeAfter, StringComparison.OrdinalIgnoreCase)) {
            await ApplyThemeFromStateAsync().ConfigureAwait(false);
        }

        await PublishOptionsStateAsync().ConfigureAwait(false);
        if (persistProfile) {
            await PersistAppStateAsync().ConfigureAwait(false);
        }

        return true;
    }

}

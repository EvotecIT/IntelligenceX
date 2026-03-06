using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Builds markdown prompt envelopes sent to the service/model.
/// </summary>
internal static class PromptMarkdownBuilder {
    private readonly record struct ConversationTurnMode(
        string Id,
        string AmbiguousTarget,
        bool RequiresEnvelope) {
        internal bool IsLowContextShortTurn => string.Equals(Id, "low_context_short_turn", StringComparison.Ordinal);
        internal bool IsCompactAnswerToRecentQuestion => string.Equals(Id, "compact_answer_to_recent_question", StringComparison.Ordinal);
        internal bool IsLightPostAnswerReply => string.Equals(Id, "light_post_answer_reply", StringComparison.Ordinal);
        internal bool IsContextualFollowUp => string.Equals(Id, "contextual_follow_up", StringComparison.Ordinal);
        internal bool IsAmbiguousScopeTarget => string.Equals(Id, "ambiguous_scope_target", StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the kickoff request used for first-run conversational start.
    /// </summary>
    /// <param name="missingFields">Missing onboarding profile fields.</param>
    /// <returns>Markdown prompt text.</returns>
    public static string BuildKickoffRequest(IReadOnlyList<string> missingFields) {
        return new MarkdownComposer()
            .Raw(PromptAssets.GetKickoffPreludePrompt())
            .BlankLine()
            .Raw(OnboardingModelProtocol.BuildGuidanceText(missingFields))
            .Build();
    }

    /// <summary>
    /// Builds a request envelope with profile/onboarding context for the model.
    /// </summary>
    /// <param name="userText">Raw user text.</param>
    /// <param name="effectiveName">Effective user name for session/profile.</param>
    /// <param name="effectivePersona">Effective assistant persona for session/profile.</param>
    /// <param name="onboardingInProgress">Whether onboarding is currently in progress.</param>
    /// <param name="missingOnboardingFields">Current missing onboarding fields.</param>
    /// <param name="includeLiveProfileUpdates">Whether live-profile guidance should be included.</param>
    /// <param name="executionBehaviorPrompt">Execution behavior prompt fragment.</param>
    /// <param name="localContextLines">Optional compact local context fallback lines.</param>
    /// <param name="conversationStyleLines">Optional recent conversation style guidance lines.</param>
    /// <param name="continuationStateLines">Optional continuation-state guidance from the latest assistant turn.</param>
    /// <param name="recentAssistantAnswerWasSubstantive">Whether the latest assistant answer appears substantive enough to support acknowledgement-style replies.</param>
    /// <param name="recentAssistantAskedQuestion">Whether the latest assistant turn appears to ask the user a question.</param>
    /// <param name="persistentMemoryLines">Optional persistent memory facts.</param>
    /// <param name="persistentMemoryPrompt">Optional persistent memory protocol guidance.</param>
    /// <param name="runtimeCapabilityLines">Optional runtime capability handshake lines.</param>
    /// <param name="proactiveExecutionEnabled">Optional proactive execution mode guidance.</param>
    /// <returns>Request text to send to service/model.</returns>
    public static string BuildServiceRequest(
        string userText,
        string? effectiveName,
        string? effectivePersona,
        bool onboardingInProgress,
        IReadOnlyList<string> missingOnboardingFields,
        bool includeLiveProfileUpdates,
        string executionBehaviorPrompt,
        IReadOnlyList<string>? localContextLines = null,
        IReadOnlyList<string>? conversationStyleLines = null,
        IReadOnlyList<string>? continuationStateLines = null,
        bool recentAssistantAnswerWasSubstantive = false,
        bool recentAssistantAskedQuestion = false,
        IReadOnlyList<string>? persistentMemoryLines = null,
        string? persistentMemoryPrompt = null,
        IReadOnlyList<string>? runtimeCapabilityLines = null,
        bool? proactiveExecutionEnabled = null) {
        var hasName = !string.IsNullOrWhiteSpace(effectiveName);
        var hasPersona = !string.IsNullOrWhiteSpace(effectivePersona);
        var conversationTurnMode = ResolveConversationTurnMode(
            userText,
            localContextLines,
            recentAssistantAnswerWasSubstantive,
            recentAssistantAskedQuestion);
        var hasSupplementalContext = includeLiveProfileUpdates
                                     || !string.IsNullOrWhiteSpace(executionBehaviorPrompt)
                                     || !string.IsNullOrWhiteSpace(persistentMemoryPrompt)
                                     || (localContextLines is { Count: > 0 })
                                     || (conversationStyleLines is { Count: > 0 })
                                     || (continuationStateLines is { Count: > 0 })
                                     || (persistentMemoryLines is { Count: > 0 })
                                     || (runtimeCapabilityLines is { Count: > 0 })
                                     || proactiveExecutionEnabled.HasValue
                                     || conversationTurnMode.RequiresEnvelope;

        if (!hasName && !hasPersona && !onboardingInProgress && !hasSupplementalContext) {
            return userText;
        }

        var markdown = new MarkdownComposer();

        if (conversationTurnMode.RequiresEnvelope) {
            markdown
                .Paragraph("[Conversation mode]")
                .Bullet("Mode: " + conversationTurnMode.Id);
            if (conversationTurnMode.IsLowContextShortTurn) {
                markdown
                    .Bullet("Respond like a real person first; do not front-load menus, onboarding, or scope taxonomies.")
                    .Bullet("If this is just a greeting or light opener, greet back naturally and wait for the concrete task.")
                    .Bullet("If the user seems to want help but the request is still vague, ask at most one short natural follow-up.")
                    .Bullet("If the exchange already feels complete, a brief natural close is enough; do not force next-step suggestions.")
                    .Bullet("Avoid generic closing filler when no concrete next action is needed.");
            } else if (conversationTurnMode.IsCompactAnswerToRecentQuestion) {
                markdown
                    .Bullet("This looks like a short reply to a recent assistant question.")
                    .Bullet("Treat it as a likely answer or confirmation tied to the recent context, not as a brand-new vague request.")
                    .Bullet("Continue from the pending question or action without asking the user to restate the prior context.")
                    .Bullet("Ask again only if the short reply still leaves the requested choice unresolved.");
            } else if (conversationTurnMode.IsLightPostAnswerReply) {
                markdown
                    .Bullet("This looks like a short low-information reply after a recent assistant answer.")
                    .Bullet("If the user is only acknowledging or lightly closing, respond briefly and stop cleanly.")
                    .Bullet("Do not reopen the conversation with menus, filler closers, or generic next-step suggestions unless new work is clearly implied.");
            } else if (conversationTurnMode.IsContextualFollowUp) {
                markdown
                    .Bullet("Treat this as a continuation of the recent conversation unless the latest context clearly conflicts.")
                    .Bullet("Do not ask the user to restate information that is already present in recent context or memory.")
                    .Bullet("If a short clarification is still required, ask one human follow-up grounded in the recent context.");
            } else if (conversationTurnMode.IsAmbiguousScopeTarget) {
                markdown
                    .Bullet("Infer the most likely scope from context first; ask only if ambiguity remains.")
                    .Bullet("If you need to clarify, mention the concrete target `" + conversationTurnMode.AmbiguousTarget + "` and ask one human question.")
                    .Bullet("Do not expose internal routing tokens or action IDs in normal conversation.");
            }

            markdown.BlankLine();
        }

        if (continuationStateLines is { Count: > 0 }) {
            markdown
                .Paragraph("[Continuation state]")
                .Bullet("Use this to continue the live thread naturally instead of resetting context.")
                .BlankLine();

            for (var i = 0; i < continuationStateLines.Count; i++) {
                var line = continuationStateLines[i];
                if (!string.IsNullOrWhiteSpace(line)) {
                    markdown.Bullet(line.Trim());
                }
            }

            markdown.BlankLine();
        }

        if (conversationStyleLines is { Count: > 0 }) {
            markdown
                .Paragraph("[Conversation style]")
                .Bullet("Blend the selected persona with the user's recent style; mirror pacing, directness, and response shape without losing competence.")
                .BlankLine();

            for (var i = 0; i < conversationStyleLines.Count; i++) {
                var line = conversationStyleLines[i];
                if (!string.IsNullOrWhiteSpace(line)) {
                    markdown.Bullet(line.Trim());
                }
            }

            markdown.BlankLine();
        }

        if (hasName || hasPersona) {
            markdown.Paragraph("[Session profile context]");
            if (hasName) {
                markdown.Bullet("User name: " + effectiveName!.Trim());
            }
            if (hasPersona) {
                markdown.Bullet("Assistant persona: " + effectivePersona!.Trim());
            }
            markdown
                .Bullet("Apply this persona/tone while remaining precise and operational.")
                .Bullet("If the user's style is sharper, warmer, or more terse than the persona, adapt the delivery while preserving the persona's core role.")
                .Bullet("Keep responses natural and conversational; avoid robotic template phrasing.")
                .BlankLine();
        }

        if (onboardingInProgress) {
            markdown
                .Paragraph("[Onboarding context]")
                .Bullet("Profile setup is still in progress.")
                .Raw(OnboardingModelProtocol.BuildGuidanceText(missingOnboardingFields))
                .BlankLine();
        }

        if (includeLiveProfileUpdates) {
            markdown
                .Paragraph("[Live profile updates]")
                .Raw(OnboardingModelProtocol.BuildLiveUpdateGuidanceText())
                .BlankLine();
        }

        if (runtimeCapabilityLines is { Count: > 0 }) {
            markdown
                .Paragraph("[Runtime capability handshake]")
                .Bullet("Treat these as the live runtime/tool limits for this turn.")
                .BlankLine();

            for (var i = 0; i < runtimeCapabilityLines.Count; i++) {
                var line = runtimeCapabilityLines[i];
                if (!string.IsNullOrWhiteSpace(line)) {
                    markdown.Bullet(line.Trim());
                }
            }

            markdown.BlankLine();
        }

        if (!string.IsNullOrWhiteSpace(executionBehaviorPrompt)) {
            markdown.Raw(executionBehaviorPrompt).BlankLine();
        }

        if (proactiveExecutionEnabled.HasValue) {
            markdown
                .Paragraph("[Proactive execution mode]")
                .Raw("ix:proactive-mode:v1");
            if (proactiveExecutionEnabled.Value) {
                markdown
                    .Raw("enabled: true")
                    .Bullet("Proactively run relevant read-only checks when they can discover issues without extra user prompts.")
                    .Bullet("Surface detected risks and propose concrete next fixes before waiting for more direction.");
            } else {
                markdown
                    .Raw("enabled: false")
                    .Bullet("Stay strictly scoped to the explicit user request.")
                    .Bullet("Do not expand into additional diagnostics unless the user asks.");
            }
            markdown.BlankLine();
        }

        if (!string.IsNullOrWhiteSpace(persistentMemoryPrompt)) {
            markdown.Raw(persistentMemoryPrompt.Trim()).BlankLine();
        }

        if (persistentMemoryLines is { Count: > 0 }) {
            markdown
                .Paragraph("[Persistent memory]")
                .Bullet("Use these as durable hints when relevant for this request.")
                .BlankLine();

            for (var i = 0; i < persistentMemoryLines.Count; i++) {
                var line = persistentMemoryLines[i];
                if (!string.IsNullOrWhiteSpace(line)) {
                    markdown.Bullet(line.Trim());
                }
            }

            markdown.BlankLine();
        }

        if (localContextLines is { Count: > 0 }) {
            markdown
                .Paragraph("[Local transcript context fallback]")
                .Bullet("Use this only as supplementary context for this turn.")
                .BlankLine();

            for (var i = 0; i < localContextLines.Count; i++) {
                var line = localContextLines[i];
                if (!string.IsNullOrWhiteSpace(line)) {
                    markdown.Bullet(line.Trim());
                }
            }

            markdown.BlankLine();
        }

        markdown
            .Paragraph("User request:")
            .Paragraph(userText ?? string.Empty);

        return markdown.Build();
    }

    private static ConversationTurnMode ResolveConversationTurnMode(
        string? userText,
        IReadOnlyList<string>? localContextLines,
        bool recentAssistantAnswerWasSubstantive,
        bool recentAssistantAskedQuestion) {
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return new ConversationTurnMode("direct_task", string.Empty, RequiresEnvelope: false);
        }

        if (TryExtractSingleDomainLikeToken(normalized, out var ambiguousTarget)) {
            return new ConversationTurnMode("ambiguous_scope_target", ambiguousTarget, RequiresEnvelope: true);
        }

        if (ConversationTurnShapeClassifier.LooksLikeLowContextShortTurn(normalized)) {
            if (recentAssistantAskedQuestion && HasLatestAssistantContextLine(localContextLines)) {
                return new ConversationTurnMode("compact_answer_to_recent_question", string.Empty, RequiresEnvelope: true);
            }

            if (recentAssistantAnswerWasSubstantive && HasLatestAssistantContextLine(localContextLines)) {
                return new ConversationTurnMode("light_post_answer_reply", string.Empty, RequiresEnvelope: true);
            }

            return new ConversationTurnMode("low_context_short_turn", string.Empty, RequiresEnvelope: true);
        }

        if (localContextLines is { Count: > 0 }
            && ConversationTurnShapeClassifier.LooksLikeContextDependentFollowUp(normalized)) {
            return new ConversationTurnMode("contextual_follow_up", string.Empty, RequiresEnvelope: true);
        }

        return new ConversationTurnMode("direct_task", string.Empty, RequiresEnvelope: false);
    }

    private static bool HasLatestAssistantContextLine(IReadOnlyList<string>? localContextLines) {
        if (localContextLines is not { Count: > 0 }) {
            return false;
        }

        for (var i = localContextLines.Count - 1; i >= 0; i--) {
            var line = (localContextLines[i] ?? string.Empty).Trim();
            if (line.Length == 0) {
                continue;
            }

            return line.StartsWith("Assistant:", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool TryExtractSingleDomainLikeToken(string text, out string domain) {
        domain = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokenStart = -1;
        for (var i = 0; i <= normalized.Length; i++) {
            var tokenCharacter = false;
            if (i < normalized.Length) {
                var ch = normalized[i];
                tokenCharacter = char.IsLetterOrDigit(ch) || ch is '.' or '-';
            }

            if (tokenCharacter) {
                if (tokenStart < 0) {
                    tokenStart = i;
                }

                continue;
            }

            if (tokenStart < 0) {
                continue;
            }

            var candidateStart = tokenStart;
            var candidateEnd = i;
            while (candidateStart < candidateEnd && normalized[candidateStart] == '.') {
                candidateStart++;
            }

            while (candidateEnd > candidateStart && normalized[candidateEnd - 1] == '.') {
                candidateEnd--;
            }

            var candidate = normalized.Substring(candidateStart, candidateEnd - candidateStart);
            tokenStart = -1;
            if (TouchesEmailMarker(normalized, candidateStart, candidateEnd)
                || !IsLikelyDomainToken(candidate)
                || !seen.Add(candidate)) {
                continue;
            }

            matches.Add(candidate);
            if (matches.Count > 1) {
                return false;
            }
        }

        if (matches.Count != 1) {
            return false;
        }

        domain = matches[0];
        return true;
    }

    private static bool TouchesEmailMarker(string? text, int start, int endExclusive) {
        var normalized = text ?? string.Empty;
        if (start < 0 || endExclusive <= start || endExclusive > normalized.Length) {
            return false;
        }

        return HasEmailMarkerNearBoundary(normalized, start - 1, step: -1)
               || HasEmailMarkerNearBoundary(normalized, endExclusive, step: 1);
    }

    private static bool HasEmailMarkerNearBoundary(string text, int index, int step) {
        for (var i = index; i >= 0 && i < text.Length; i += step) {
            var ch = text[i];
            if (ch == '@') {
                return true;
            }

            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch)) {
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool IsLikelyDomainToken(string token) {
        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length < 4
            || normalized.Length > 255
            || normalized.StartsWith(".", StringComparison.Ordinal)
            || normalized.EndsWith(".", StringComparison.Ordinal)
            || normalized.Contains("..", StringComparison.Ordinal)) {
            return false;
        }

        var labels = normalized.Split('.');
        if (labels.Length < 2) {
            return false;
        }

        var hasLetter = false;
        for (var i = 0; i < labels.Length; i++) {
            var label = labels[i];
            if (label.Length is < 1 or > 63
                || label.StartsWith("-", StringComparison.Ordinal)
                || label.EndsWith("-", StringComparison.Ordinal)) {
                return false;
            }

            for (var j = 0; j < label.Length; j++) {
                var ch = label[j];
                if (!(char.IsLetterOrDigit(ch) || ch == '-')) {
                    return false;
                }

                if (char.IsLetter(ch)) {
                    hasLetter = true;
                }
            }
        }

        return hasLetter;
    }
}

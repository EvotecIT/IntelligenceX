using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Builds markdown prompt envelopes sent to the service/model.
/// </summary>
internal static class PromptMarkdownBuilder {
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
    /// <returns>Request text to send to service/model.</returns>
    public static string BuildServiceRequest(
        string userText,
        string? effectiveName,
        string? effectivePersona,
        bool onboardingInProgress,
        IReadOnlyList<string> missingOnboardingFields,
        bool includeLiveProfileUpdates,
        string executionBehaviorPrompt,
        IReadOnlyList<string>? localContextLines = null) {
        var hasName = !string.IsNullOrWhiteSpace(effectiveName);
        var hasPersona = !string.IsNullOrWhiteSpace(effectivePersona);

        if (!hasName && !hasPersona && !onboardingInProgress) {
            return userText;
        }

        var markdown = new MarkdownComposer();

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

        if (!string.IsNullOrWhiteSpace(executionBehaviorPrompt)) {
            markdown.Raw(executionBehaviorPrompt).BlankLine();
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
}

using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Unit tests for onboarding prompt helper rules.
/// </summary>
public sealed class OnboardingPromptRulesTests {
    /// <summary>
    /// Ensures name prompt detection works for expected onboarding text.
    /// </summary>
    [Fact]
    public void IsAskNamePromptText_ReturnsTrueForPromptVariant() {
        var text = "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)";
        Assert.True(OnboardingPromptRules.IsAskNamePromptText(text));
    }

    /// <summary>
    /// Ensures theme prompt detection works for expected onboarding text.
    /// </summary>
    [Fact]
    public void IsAskThemePromptText_ReturnsTrueForPromptVariant() {
        var text = "Great. Pick a theme: default, emerald, or rose.";
        Assert.True(OnboardingPromptRules.IsAskThemePromptText(text));
    }

    /// <summary>
    /// Ensures older duplicate name prompts are removed while keeping the latest prompt.
    /// </summary>
    [Fact]
    public void PruneDuplicateAskNamePrompts_RemovesOlderPrompt() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now),
            ("User", "hello", now.AddSeconds(1)),
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now.AddSeconds(2))
        };

        var changed = OnboardingPromptRules.PruneDuplicateAskNamePrompts(messages);

        Assert.True(changed);
        var remaining = messages.Where(static m => OnboardingPromptRules.IsAskNamePromptText(m.Text)).ToList();
        Assert.Single(remaining);
        Assert.Equal(now.AddSeconds(2), remaining[0].Time);
    }

    /// <summary>
    /// Ensures repeated onboarding intro prompts before first user message are deduplicated.
    /// </summary>
    [Fact]
    public void PruneDuplicateAssistantLeadPrompts_RemovesOlderAssistantDuplicate() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now),
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now.AddSeconds(1))
        };

        var changed = OnboardingPromptRules.PruneDuplicateAssistantLeadPrompts(messages);

        Assert.True(changed);
        Assert.Single(messages);
        Assert.Equal(now.AddSeconds(1), messages[0].Time);
    }

    /// <summary>
    /// Ensures lead-prompt dedupe does not remove later prompts after the user has replied.
    /// </summary>
    [Fact]
    public void PruneDuplicateAssistantLeadPrompts_DoesNotTouchPromptsAfterUserReply() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now),
            ("User", "Przemek", now.AddSeconds(1)),
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now.AddSeconds(2))
        };

        var changed = OnboardingPromptRules.PruneDuplicateAssistantLeadPrompts(messages);

        Assert.False(changed);
        var remaining = messages.Where(static m => OnboardingPromptRules.IsAskNamePromptText(m.Text)).ToList();
        Assert.Equal(2, remaining.Count);
    }

    /// <summary>
    /// Ensures equivalent onboarding lead prompts are detected when already present.
    /// </summary>
    [Fact]
    public void HasEquivalentOnboardingIntroPrompt_ReturnsTrueForMatchingPrompt() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now)
        };

        var exists = OnboardingPromptRules.HasEquivalentOnboardingIntroPrompt(
            messages,
            "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)");

        Assert.True(exists);
    }

    /// <summary>
    /// Ensures non-onboarding text does not falsely match onboarding intro prompts.
    /// </summary>
    [Fact]
    public void HasEquivalentOnboardingIntroPrompt_ReturnsFalseForNonOnboardingText() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Hi. Let's set this up quickly. What should I call you? (type skip to keep defaults)", now)
        };

        var exists = OnboardingPromptRules.HasEquivalentOnboardingIntroPrompt(messages, "Can you list top 5 AD users?");

        Assert.False(exists);
    }

    /// <summary>
    /// Ensures user-message detection works for mixed transcripts.
    /// </summary>
    [Fact]
    public void HasAnyUserMessage_ReturnsTrueWhenUserExists() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Welcome", now),
            ("User", "Hi", now.AddSeconds(1))
        };

        Assert.True(OnboardingPromptRules.HasAnyUserMessage(messages));
    }

    /// <summary>
    /// Ensures assistant-message equivalence ignores punctuation/case noise.
    /// </summary>
    [Fact]
    public void HasEquivalentAssistantMessage_ReturnsTrueForNormalizedMatch() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("Assistant", "Hi. Let's set this up quickly!", now)
        };

        var exists = OnboardingPromptRules.HasEquivalentAssistantMessage(
            messages,
            "hi lets set this up quickly");

        Assert.True(exists);
    }

    /// <summary>
    /// Ensures assistant-message equivalence is role-specific and ignores user duplicates.
    /// </summary>
    [Fact]
    public void HasEquivalentAssistantMessage_IgnoresUserMessages() {
        var now = DateTime.UtcNow;
        var messages = new List<(string Role, string Text, DateTime Time)> {
            ("User", "Hi there", now)
        };

        var exists = OnboardingPromptRules.HasEquivalentAssistantMessage(messages, "Hi there");

        Assert.False(exists);
    }
}

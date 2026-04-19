using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal ReviewSettings CloneWithProviderOverride(ReviewProvider provider, string model,
        ReasoningEffort? reasoningEffort, string? agentProfile = null,
        ReviewAgentProfileSettings? resolvedAgentProfile = null) {
        var cloneSource = resolvedAgentProfile is not null || !string.IsNullOrWhiteSpace(agentProfile)
            ? AgentProfileBaseline ?? this
            : this;
        var clone = cloneSource.CloneWithoutAgentProfileBaseline();
        if (resolvedAgentProfile is not null) {
            clone.ApplyAgentProfile(resolvedAgentProfile, captureBaseline: false);
        } else if (!string.IsNullOrWhiteSpace(agentProfile)) {
            clone.ApplyAgentProfile(agentProfile);
        }
        clone.Provider = provider;
        clone.Model = model;
        clone.ReasoningEffort = reasoningEffort;
        return clone;
    }
}

using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal ReviewSettings CloneWithProviderOverride(ReviewProvider provider, string model,
        ReasoningEffort? reasoningEffort, string? agentProfile = null,
        ReviewAgentProfileSettings? resolvedAgentProfile = null) {
        var clone = (ReviewSettings)MemberwiseClone();
        if (resolvedAgentProfile is not null) {
            clone.ApplyAgentProfile(resolvedAgentProfile);
        } else if (!string.IsNullOrWhiteSpace(agentProfile)) {
            clone.ApplyAgentProfile(agentProfile);
        }
        clone.Provider = provider;
        clone.Model = model;
        clone.ReasoningEffort = reasoningEffort;
        return clone;
    }
}

using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal ReviewSettings CloneWithProviderOverride(ReviewProvider provider, string model,
        ReasoningEffort? reasoningEffort, string? agentProfile = null) {
        var clone = (ReviewSettings)MemberwiseClone();
        if (!string.IsNullOrWhiteSpace(agentProfile)) {
            clone.ApplyAgentProfile(agentProfile);
        }
        clone.Provider = provider;
        clone.Model = model;
        clone.ReasoningEffort = reasoningEffort;
        return clone;
    }
}

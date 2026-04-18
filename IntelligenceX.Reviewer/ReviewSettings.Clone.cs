using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Reviewer;

internal sealed partial class ReviewSettings {
    internal ReviewSettings CloneWithProviderOverride(ReviewProvider provider, string model,
        ReasoningEffort? reasoningEffort) {
        var clone = (ReviewSettings)MemberwiseClone();
        clone.Provider = provider;
        clone.Model = model;
        clone.ReasoningEffort = reasoningEffort;
        return clone;
    }
}

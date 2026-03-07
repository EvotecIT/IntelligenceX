using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for language-light turn-shape detection used by prompt gating.
/// </summary>
public sealed class ConversationTurnShapeClassifierTests {
    /// <summary>
    /// Ensures natural "what can you do" asks are recognized as assistant-capability questions.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsTrueForNaturalCapabilityAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("What can you do for me today?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures concrete operational asks are not mistaken for assistant-capability questions.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForConcreteOperationalAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("Can you check AD replication health?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures explicit model/tool self-report asks are recognized as runtime introspection questions.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForRuntimeSelfReportAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What model and tools are you using right now?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures regular troubleshooting questions do not trigger runtime introspection handling.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForRegularTaskQuestion() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("Which DC is failing replication?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures declarative runtime-cue text does not accidentally trigger runtime introspection handling.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForDeclarativeRuntimeCueText() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("This model is wrong for the job");

        Assert.False(result);
    }
}

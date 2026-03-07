using IntelligenceX.Chat.Abstractions;
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
    /// Ensures broader capability asks still work when the trailing tokens are not tiny filler words.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsTrueForBroadCapabilityAskWithoutShortTail() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("What capabilities do you have available?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures broad non-English capability asks can still enter capability-question mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsTrueForNonEnglishCapabilityAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("Co mozesz zrobic dla mnie?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures single-token non-segmented-script capability asks still enter capability-question mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsTrueForSingleTokenNonSegmentedCapabilityAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("\u4f60\u80fd\u505a\u4ec0\u4e48\uff1f");

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
    /// Ensures short concrete asks are not widened into generic capability questions.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForShortConcreteTaskAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("Can you check logs?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures relaxed broad-question gating does not widen short concrete asks with filler words into capability mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForConcreteTaskAskWithArticle() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("Can you check the event logs?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures ordinary short generic questions do not drift into capability-question mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForShortGenericQuestion() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("What is this?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures the shared broad-question helper rejects very short generic questions at the exact boundary.
    /// </summary>
    [Fact]
    public void LooksLikeBroadGenericQuestionShape_ReturnsFalseForThreeTokenGenericQuestion() {
        var tokens = new[] { "What", "is", "this" };
        var result = ConversationTurnShapeClassifier.LooksLikeBroadGenericQuestionShape("What is this?", tokens);

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
    /// Ensures explicit single-cue runtime questions still enter runtime-introspection mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForSingleCueRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What model are you using?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures compact enterprise-style runtime asks with slashes and acronyms still enter runtime-introspection mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForSlashQualifiedRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What model/tools for DNS/AD?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures dotted scope qualifiers do not block runtime introspection when the user is still asking about model/tooling.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForDomainQualifiedRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What model/tools for ad.evotec.xyz?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures compact runtime self-report asks remain valid when users include colon punctuation in a natural meta question.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForColonQualifiedRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What model: gpt-5?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures compact runtime self-report asks remain valid when runtime cues use common underscore token styles.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForUnderscoreQualifiedRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What model_name are you using?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures compact runtime self-report asks remain valid when runtime cues are wrapped in inline backticks.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForBacktickQualifiedRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What `model` are you using?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures compact runtime self-report asks can be tightened into a shorter answer mode.
    /// </summary>
    [Fact]
    public void LooksLikeCompactAssistantRuntimeIntrospectionQuestion_ReturnsTrueForCompactQualifiedAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeCompactAssistantRuntimeIntrospectionQuestion("What model/tools for DNS/AD?");

        Assert.True(result);
    }

    /// <summary>
    /// Ensures broader runtime inventory asks keep the normal runtime-introspection mode.
    /// </summary>
    [Fact]
    public void LooksLikeCompactAssistantRuntimeIntrospectionQuestion_ReturnsFalseForBroaderRuntimeAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeCompactAssistantRuntimeIntrospectionQuestion("What model and tools are you using right now?");

        Assert.False(result);
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

    /// <summary>
    /// Ensures concrete task turns that mention tool words do not trigger runtime self-report mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForOperationalToolTaskQuestion() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("Can you use the event viewer tool to check errors?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures troubleshooting requests with slash-qualified technical scope are not widened into runtime self-report mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForOperationalSlashQualifiedTaskQuestion() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("Can you use the DNS/AD tool output to check replication errors?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures the shared broad-question helper can stay generic for runtime inventory asks when acronym qualifiers are allowed.
    /// </summary>
    [Fact]
    public void LooksLikeBroadGenericQuestionShape_ReturnsTrueForAcronymQualifiedQuestionWhenAllowed() {
        var tokens = new[] { "What", "model", "tools", "for", "DNS", "AD" };
        var result = ConversationTurnShapeClassifier.LooksLikeBroadGenericQuestionShape(
            "What model/tools for DNS/AD?",
            tokens,
            allowUppercaseAcronyms: true);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures app-side runtime introspection routing stays aligned with the shared classifier used by host and app.
    /// </summary>
    [Theory]
    [InlineData("What model/tools for DNS/AD?")]
    [InlineData("What model/tools for ad.evotec.xyz?")]
    [InlineData("What model: gpt-5?")]
    [InlineData("What model_name are you using?")]
    [InlineData("What `model` are you using?")]
    [InlineData("What model are you using?")]
    [InlineData("Can you use the DNS/AD tool output to check replication errors?")]
    [InlineData("This model is wrong for the job")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_StaysAlignedWithSharedRuntimeClassifier(string userText) {
        var appResult = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);
        var sharedResult = RuntimeSelfReportTurnClassifier.LooksLikeRuntimeIntrospectionQuestion(userText);

        Assert.Equal(sharedResult, appResult);
    }

    /// <summary>
    /// Ensures compact runtime introspection routing stays aligned with the shared classifier used by host and app.
    /// </summary>
    [Theory]
    [InlineData("What model/tools for DNS/AD?")]
    [InlineData("What model/tools for ad.evotec.xyz?")]
    [InlineData("What model and tools are you using right now?")]
    [InlineData("Can you use the DNS/AD tool output to check replication errors?")]
    public void LooksLikeCompactAssistantRuntimeIntrospectionQuestion_StaysAlignedWithSharedRuntimeClassifier(string userText) {
        var appResult = ConversationTurnShapeClassifier.LooksLikeCompactAssistantRuntimeIntrospectionQuestion(userText);
        var sharedResult = RuntimeSelfReportTurnClassifier.LooksLikeCompactRuntimeIntrospectionQuestion(userText);

        Assert.Equal(sharedResult, appResult);
    }
}

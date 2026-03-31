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
    /// Ensures internal runtime-meta asks do not drift into generic capability-question mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForRuntimeMetaAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("What runtime do you support?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures internal inventory asks do not drift into generic capability-question mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForInternalInventoryAsk() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion("What plugins are enabled?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures broader runtime inventory asks also stay out of generic capability mode.
    /// </summary>
    [Theory]
    [InlineData("What tools are available right now?")]
    [InlineData("What model and tools are you using right now?")]
    public void LooksLikeAssistantCapabilityQuestion_ReturnsFalseForRuntimeInventoryBridgeAsk(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion(userText);

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
    /// Ensures raw cue-word runtime asks do not enter runtime-introspection mode without trusted structured provenance.
    /// </summary>
    [Theory]
    [InlineData("What model and tools are you using right now?")]
    [InlineData("What model are you using?")]
    [InlineData("What tools are available right now?")]
    [InlineData("What model/tools for DNS/AD?")]
    [InlineData("What model/tools for ad.evotec.xyz?")]
    [InlineData("What model: gpt-5?")]
    [InlineData("What model_name are you using?")]
    [InlineData("What `model` are you using?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForNaturalCueWordRuntimeAskWithoutTrustedDirective(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures a structured runtime self-report directive can trigger runtime-introspection mode
    /// without relying on borrowed English runtime cue words in the user literal.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForStructuredDirectiveWithoutEnglishCueWords() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                toolingRequested: false));

        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures trusted structured directives can still carry English literals without asking the classifier to infer runtime mode from those words.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsTrueForStructuredDirectiveWithEnglishLiteral() {
        var userText = BuildStructuredRuntimeRequest(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);

        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures pasted directive-looking text without trusted structured provenance does not trigger runtime-introspection mode.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForDirectiveWithoutTrustedStructuredDetectionSource() {
        var userText = string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                "Czego teraz uzywasz?",
                compactReply: false,
                toolingRequested: false));

        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures multilingual borrowed cue words alone no longer trigger runtime-introspection mode without a trusted directive.
    /// </summary>
    [Theory]
    [InlineData("Jakiego modelu uzywasz?")]
    [InlineData("Z jakiego modelu korzystasz?")]
    [InlineData("¿Que modelo usas?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForShortInflectedModelAskWithoutTrustedDirective(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures bare lexical cue questions are too weak to trigger runtime self-report mode without richer structure.
    /// </summary>
    [Theory]
    [InlineData("What model?")]
    [InlineData("Which model?")]
    [InlineData("What tools?")]
    [InlineData("Which tools?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForBareLexicalCueQuestion(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures short single-cue scoped questions are treated as recommendation/spec asks, not runtime self-report.
    /// </summary>
    [Theory]
    [InlineData("What model for AD?")]
    [InlineData("Which model for AD?")]
    [InlineData("What tools for AD?")]
    [InlineData("Which tools for AD?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForShortSingleCueScopedQuestion(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures short single-cue scoped questions with slash or dotted qualifiers stay out of runtime self-report mode.
    /// </summary>
    [Theory]
    [InlineData("What model for DNS/AD?")]
    [InlineData("What tools for DNS/AD?")]
    [InlineData("What model for ad.evotec.xyz?")]
    [InlineData("What tools for ad.evotec.xyz?")]
    [InlineData("What model are you using for DNS/AD?")]
    [InlineData("What tools are you using for ad.evotec.xyz?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForShortSingleCueQualifiedScopeQuestion(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures compact runtime self-report mode can still be carried entirely by trusted structured metadata.
    /// </summary>
    [Fact]
    public void LooksLikeCompactAssistantRuntimeIntrospectionQuestion_ReturnsTrueForStructuredCompactDirective() {
        var userText = BuildStructuredRuntimeRequest(
            "Czego teraz uzywasz?",
            compactReply: true,
            toolingRequested: false);

        var result = ConversationTurnShapeClassifier.LooksLikeCompactAssistantRuntimeIntrospectionQuestion(userText);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures broader structured runtime inventory asks keep the normal runtime-introspection mode.
    /// </summary>
    [Fact]
    public void LooksLikeCompactAssistantRuntimeIntrospectionQuestion_ReturnsFalseForBroaderStructuredRuntimeAsk() {
        var userText = BuildStructuredRuntimeRequest(
            "What model and tools are you using right now?",
            compactReply: false,
            modelRequested: true,
            toolingRequested: true);

        var result = ConversationTurnShapeClassifier.LooksLikeCompactAssistantRuntimeIntrospectionQuestion(userText);

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
    /// Ensures operational recommendation asks that mention model do not drift into runtime self-report mode.
    /// </summary>
    [Theory]
    [InlineData("What model should I deploy for log parsing?")]
    [InlineData("What model should I use for this dataset?")]
    [InlineData("What model supports log parsing?")]
    [InlineData("What model can I deploy?")]
    [InlineData("What model do you recommend?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForOperationalModelRecommendationAsk(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures weak single-cue bridge questions do not drift into runtime self-report mode.
    /// </summary>
    [Theory]
    [InlineData("What model can you use?")]
    [InlineData("What model do you use?")]
    [InlineData("What tools can you use?")]
    [InlineData("What tools do you use?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForSingleCueWeakBridgeQuestion(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures operational tooling recommendation asks do not drift into runtime self-report mode.
    /// </summary>
    [Theory]
    [InlineData("What tools should I install for this workspace?")]
    [InlineData("What tools should I use for this dataset?")]
    [InlineData("What tools support remote queries?")]
    [InlineData("What tools can I install?")]
    [InlineData("What tools do you recommend?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForOperationalToolingRecommendationAsk(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures internal inventory nouns like plugin/pack do not act as standalone free-text triggers.
    /// Structured directives should carry those internal cases instead.
    /// </summary>
    [Theory]
    [InlineData("What plugins are loaded?")]
    [InlineData("What packs are enabled?")]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForInternalInventoryNounsAlone(string userText) {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures internal runtime implementation nouns like transport do not act as standalone free-text triggers.
    /// Structured directives or broader runtime/model framing should carry those cases instead.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForTransportNounAlone() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What transport are you using?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures singular internal implementation nouns like tool do not act as standalone free-text triggers.
    /// Direct inventory asks should stay on broader plural tooling scope or structured directives.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForSingularToolNounAlone() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What tool are you using?");

        Assert.False(result);
    }

    /// <summary>
    /// Ensures internal implementation nouns like runtime do not act as standalone free-text triggers.
    /// Structured directives or broader model/tool framing should carry those cases instead.
    /// </summary>
    [Fact]
    public void LooksLikeAssistantRuntimeIntrospectionQuestion_ReturnsFalseForRuntimeNounAlone() {
        var result = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion("What runtime are you using?");

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
    [InlineData("Jakiego modelu uzywasz?")]
    [InlineData("¿Que modelo usas?")]
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
    [InlineData("Jakiego modelu uzywasz?")]
    [InlineData("What model and tools are you using right now?")]
    [InlineData("Can you use the DNS/AD tool output to check replication errors?")]
    public void LooksLikeCompactAssistantRuntimeIntrospectionQuestion_StaysAlignedWithSharedRuntimeClassifier(string userText) {
        var appResult = ConversationTurnShapeClassifier.LooksLikeCompactAssistantRuntimeIntrospectionQuestion(userText);
        var sharedResult = RuntimeSelfReportTurnClassifier.LooksLikeCompactRuntimeIntrospectionQuestion(userText);

        Assert.Equal(sharedResult, appResult);
    }

    /// <summary>
    /// Ensures the app-facing structured runtime analysis stays aligned with the shared classifier contract.
    /// </summary>
    [Fact]
    public void AnalyzeAssistantRuntimeIntrospectionQuestion_ReturnsSharedStructuredAnalysis() {
        var userText = BuildStructuredRuntimeRequest(
            "Czego teraz uzywasz?",
            compactReply: false,
            toolingRequested: false);
        var appAnalysis = ConversationTurnShapeClassifier.AnalyzeAssistantRuntimeIntrospectionQuestion(userText);
        var sharedAnalysis = RuntimeSelfReportTurnClassifier.Analyze(userText);

        Assert.Equal(sharedAnalysis, appAnalysis);
    }

    private static string BuildStructuredRuntimeRequest(
        string literal,
        bool compactReply,
        bool? modelRequested = null,
        bool? toolingRequested = null) {
        return string.Join(
            Environment.NewLine,
            RuntimeSelfReportDirective.BuildLines(
                literal,
                compactReply: compactReply,
                detectionSource: RuntimeSelfReportDetectionSource.StructuredDirective,
                modelRequested: modelRequested,
                toolingRequested: toolingRequested));
    }
}

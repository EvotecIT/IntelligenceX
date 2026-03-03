using System;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForLongContextualFollowUpWhenContinuationSubsetUsed() {
        var userRequest = "Please proceed with the failed logon report on ADO Security and include a concise summary of top impacted accounts.";
        var assistantDraft = "I can run the failed logon report on ADO Security and include a concise summary of top impacted accounts.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForLongContextualFollowUpWithoutContinuationSubset() {
        var userRequest = "Please proceed with the failed logon report on ADO Security and include a concise summary of top impacted accounts.";
        var assistantDraft = "I can run the failed logon report on ADO Security and include a concise summary of top impacted accounts.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForLongContextualFollowUpInPolishWithoutContinuationSubset() {
        var userRequest = "Prosze kontynuowac raport nieudanych logowan w ADO Security i dodaj krotkie podsumowanie najbardziej dotknietych kont.";
        var assistantDraft = "Moge kontynuowac raport nieudanych logowan w ADO Security i dodac krotkie podsumowanie najbardziej dotknietych kont.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForLocalizedContinuationTailWithoutEnglishMarker() {
        var userRequest = """
                          Prosze przeanalizowac stan replikacji katalogu dla wszystkich kontrolerow domeny i przygotowac porownanie opoznien wraz z krotkim podsumowaniem najwazniejszych roznic miedzy lokalizacjami i serwerami.
                          Dalszy krok: uruchom teraz
                          """;
        var assistantDraft = "Uruchom teraz porownanie opoznien replikacji i za chwile zwroce podsumowanie roznic.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }
}

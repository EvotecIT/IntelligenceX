using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ExpandContinuationUserRequest_FreshTopLevelGreetingDoesNotReusePriorIntent() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-fresh-top-level";

        session.RememberUserIntentForTesting(threadId, "Please run AD LDAP and DNS MX checks together now.");

        var expanded = session.ExpandContinuationUserRequestForTesting(threadId, "hello");

        Assert.Equal("hello", expanded);
    }

    [Fact]
    public void RememberUserIntentForTesting_FreshTopLevelIntentClearsStalePendingChoiceContexts() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-clear-stale-choice-context";

        session.RememberPendingActionsForTesting(
            threadId,
            """
            1. AD domain
            2. Public domain
            """);
        session.RememberPendingDomainIntentClarificationRequestForTesting(threadId);
        session.SetPreferredDomainIntentFamilyForTesting(threadId, "ad_domain");

        session.RememberUserIntentForTesting(threadId, "hello");

        Assert.False(session.HasFreshPendingActionsContextForTesting(threadId));
        Assert.False(session.TryResolvePendingDomainIntentClarificationSelectionForTesting(threadId, "1", out _));
        Assert.Null(session.GetPreferredDomainIntentFamilyForTesting(threadId));
    }

    [Theory]
    [InlineData("hello", false, false)]
    [InlineData("continue", false, true)]
    [InlineData("go ahead", false, true)]
    [InlineData("继续", false, true)]
    public void ResolveFollowUpTurnClassification_UsesMoreConservativeCompactContinuationShape(
        string userRequest,
        bool expectedContinuationFollowUpTurn,
        bool expectedCompactFollowUpTurn) {
        var result = ChatServiceSession.ResolveFollowUpTurnClassificationForTesting(
            continuationContractDetected: false,
            hasStructuredContinuationContext: true,
            userRequest: userRequest,
            routedUserRequest: userRequest);

        Assert.Equal(expectedContinuationFollowUpTurn, result.ContinuationFollowUpTurn);
        Assert.Equal(expectedCompactFollowUpTurn, result.CompactFollowUpTurn);
    }

    [Theory]
    [InlineData("please rerun those checks now")]
    [InlineData("can't you recheck?")]
    [InlineData("sprawdz jeszcze raz")]
    public void LooksLikeLiveRefreshFollowUp_RecognizesExplicitFreshExecutionRequests(string userRequest) {
        Assert.True(ChatServiceSession.LooksLikeLiveRefreshFollowUpForTesting(userRequest));
    }

    [Theory]
    [InlineData("what does eventlog_evtx_query do?")]
    [InlineData("hello")]
    [InlineData("show me a table")]
    [InlineData("please rerun eventlog_evtx_query for this host")]
    public void LooksLikeLiveRefreshFollowUp_DoesNotMisclassifyNonRefreshRequests(string userRequest) {
        Assert.False(ChatServiceSession.LooksLikeLiveRefreshFollowUpForTesting(userRequest));
    }

    [Fact]
    public void ResolveLiveRefreshFollowUpTurn_RecognizesImperativeRerunWithoutToolNameAsFreshExecutionFollowUp() {
        var liveRefreshFollowUpTurn = ChatServiceSession.ResolveLiveRefreshFollowUpTurnForTesting(
            hasStructuredContinuationContext: false,
            hasFreshThreadToolEvidence: true,
            userRequest: "please rerun those checks now");

        Assert.True(liveRefreshFollowUpTurn);
    }
}

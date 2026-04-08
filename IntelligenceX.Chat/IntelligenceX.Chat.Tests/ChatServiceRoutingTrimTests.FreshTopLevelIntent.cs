using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
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
    [InlineData("run now")]
    [InlineData("check certs")]
    [InlineData("what about RAM?")]
    public void LooksLikeLiveRefreshFollowUp_UsesStructuredExecutionIntentFromWorkingMemory(string userRequest) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-structured-live-refresh";
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Continue the current AD health investigation.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: replication baseline is complete." },
            priorAnswerPlanUserGoal: "Refresh the follow-up diagnostics.",
            priorAnswerPlanUnresolvedNow: "Run the remaining cert and RAM checks.",
            priorAnswerPlanRequiresLiveExecution: true,
            priorAnswerPlanMissingLiveEvidence: "cert status and memory usage",
            priorAnswerPlanPreferredPackIds: new[] { "active_directory", "system" },
            priorAnswerPlanPreferredToolNames: new[] { "ad_ldap_diagnostics", "system_hardware_summary" });

        Assert.True(session.LooksLikeLiveRefreshFollowUpForTesting(threadId, userRequest));
    }

    [Theory]
    [InlineData("what does eventlog_evtx_query do?")]
    [InlineData("hello")]
    [InlineData("show me a table")]
    [InlineData("thanks!")]
    public void LooksLikeLiveRefreshFollowUp_DoesNotMisclassifyNonRefreshRequestsWithoutStructuredSignals(string userRequest) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        Assert.False(session.LooksLikeLiveRefreshFollowUpForTesting("thread-no-live-refresh", userRequest));
    }

    [Fact]
    public void ResolveLiveRefreshFollowUpTurn_RecognizesStructuredLiveExecutionFollowUp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-live-refresh-turn";
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Continue the same host diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_forest_discover" },
            recentEvidenceSnippets: new[] { "ad_forest_discover: DC inventory is available." },
            priorAnswerPlanUserGoal: "Run the next host diagnostics.",
            priorAnswerPlanUnresolvedNow: "Check disk and RAM on the discovered DC.",
            priorAnswerPlanRequiresLiveExecution: true,
            priorAnswerPlanMissingLiveEvidence: "remote disk and memory state",
            priorAnswerPlanPreferredPackIds: new[] { "system" },
            priorAnswerPlanPreferredToolNames: new[] { "system_logical_disks_list", "system_hardware_summary" });

        var liveRefreshFollowUpTurn = session.ResolveLiveRefreshFollowUpTurnForTesting(
            threadId: threadId,
            hasStructuredContinuationContext: false,
            hasFreshThreadToolEvidence: true,
            userRequest: "run now");

        Assert.True(liveRefreshFollowUpTurn);
    }

    [Fact]
    public void LooksLikeLiveRefreshFollowUp_DoesNotTreatMetaHonestyTurnAsRefreshRequest() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-meta-honesty-turn";
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Continue the same host diagnostics.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "system_metrics_summary" },
            recentEvidenceSnippets: new[] { "system_metrics_summary: RAM usage was collected for the discovered DCs." },
            priorAnswerPlanUserGoal: "Refresh the next remote host checks.",
            priorAnswerPlanUnresolvedNow: "Re-run disk and RAM checks on the same hosts.",
            priorAnswerPlanRequiresLiveExecution: true,
            priorAnswerPlanMissingLiveEvidence: "fresh disk and RAM state",
            priorAnswerPlanPreferredPackIds: new[] { "system" },
            priorAnswerPlanPreferredToolNames: new[] { "system_logical_disks_list", "system_metrics_summary" });

        var result = session.LooksLikeLiveRefreshFollowUpForTesting(
            threadId,
            "If no live tools run in a turn, explain capability honestly without claiming you refreshed anything.");

        Assert.False(result);
    }

    [Fact]
    public void LooksLikeLiveRefreshFollowUp_DoesNotTreatArtifactOnlyTopologyFollowUpAsFreshExecution() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-artifact-only-topology";
        session.RememberThreadToolEvidenceForTesting(
            threadId,
            toolCalls: new[] {
                new ToolCallDto {
                    CallId = "call-1",
                    Name = "ad_replication_summary",
                    ArgumentsJson = "{}"
                }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-1",
                    Ok = true,
                    Output = "{\"summary_view\":[{\"server\":\"AD0\"}]}",
                    SummaryMarkdown = "Replication summary is ready."
                }
            },
            mutatingToolHintsByName: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        session.RememberWorkingMemoryCheckpointForTesting(
            threadId: threadId,
            intentAnchor: "Continue the same replication review.",
            domainIntentFamily: "ad_domain",
            recentToolNames: new[] { "ad_replication_summary" },
            recentEvidenceSnippets: new[] { "ad_replication_summary: Replication summary is ready." },
            priorAnswerPlanUserGoal: "Show the topology from the current replication evidence.",
            priorAnswerPlanPrimaryArtifact: "table");

        var result = session.LooksLikeLiveRefreshFollowUpForTesting(
            threadId,
            "Pokaz to na wykresie topologii replikacji.");

        Assert.False(result);
    }
}

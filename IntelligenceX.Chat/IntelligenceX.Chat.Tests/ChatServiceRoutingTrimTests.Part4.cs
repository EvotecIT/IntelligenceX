using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Ensures bounded routing caches evict oldest/uninitialized entries first.
/// </summary>
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
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForLongContextualFollowUpWhenDraftIsCapabilityBlockerWithoutContinuationSubset() {
        var userRequest = "Please proceed with the failed logon report on ADO Security and include a concise summary of top impacted accounts.";
        var assistantDraft = "I can't query remote ADO live logs directly without machine access.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForGratitudeFollowUps() {
        var userRequest = "thanks";
        var assistantDraft = "You're welcome.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

}

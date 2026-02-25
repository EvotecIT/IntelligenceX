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
    private const int MaxTrackedToolRoutingStats = 512;
    private const int MaxTrackedWeightedRoutingContexts = 256;
    private static readonly MethodInfo LooksLikeContinuationFollowUpMethod =
        typeof(ChatServiceSession).GetMethod("LooksLikeContinuationFollowUp", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("LooksLikeContinuationFollowUp not found.");
    private static readonly MethodInfo LooksLikeCompactFollowUpMethod =
        typeof(ChatServiceSession).GetMethod("LooksLikeCompactFollowUp", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("LooksLikeCompactFollowUp not found.");
    private static readonly MethodInfo CountLetterDigitTokensMethod =
        typeof(ChatServiceSession).GetMethod("CountLetterDigitTokens", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CountLetterDigitTokens not found.");
    private static readonly MethodInfo ShouldSkipWeightedRoutingMethod =
        typeof(ChatServiceSession).GetMethod("ShouldSkipWeightedRouting", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldSkipWeightedRouting not found.");
    private static readonly MethodInfo ShouldAttemptToolExecutionNudgeMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptToolExecutionNudge", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptToolExecutionNudge not found.");
    private static readonly MethodInfo EvaluateToolExecutionNudgeDecisionMethod =
        typeof(ChatServiceSession).GetMethod("EvaluateToolExecutionNudgeDecision", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("EvaluateToolExecutionNudgeDecision not found.");
    private static readonly MethodInfo ShouldEnforceExecuteOrExplainContractMethod =
        typeof(ChatServiceSession).GetMethod("ShouldEnforceExecuteOrExplainContract", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldEnforceExecuteOrExplainContract not found.");
    private static readonly MethodInfo ShouldAttemptNoToolExecutionWatchdogMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptNoToolExecutionWatchdog", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptNoToolExecutionWatchdog not found.");
    private static readonly MethodInfo ShouldSuppressLocalToolRecoveryRetriesMethod =
        typeof(ChatServiceSession).GetMethod("ShouldSuppressLocalToolRecoveryRetries", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldSuppressLocalToolRecoveryRetries not found.");
    private static readonly MethodInfo ShouldForceExecutionContractBlockerAtFinalizeMethod =
        typeof(ChatServiceSession).GetMethod("ShouldForceExecutionContractBlockerAtFinalize", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldForceExecutionContractBlockerAtFinalize not found.");
    private static readonly MethodInfo ShouldAttemptContinuationSubsetEscapeMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptContinuationSubsetEscape", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptContinuationSubsetEscape not found.");
    private static readonly MethodInfo ShouldAttemptToolReceiptCorrectionMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptToolReceiptCorrection", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptToolReceiptCorrection not found.");
    private static readonly MethodInfo BuildToolExecutionNudgePromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolExecutionNudgePrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolExecutionNudgePrompt not found.");
    private static readonly MethodInfo BuildNoToolExecutionWatchdogPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildNoToolExecutionWatchdogPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildNoToolExecutionWatchdogPrompt not found.");
    private static readonly MethodInfo BuildExecutionContractBlockerTextMethod =
        typeof(ChatServiceSession).GetMethod("BuildExecutionContractBlockerText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildExecutionContractBlockerText not found.");
    private static readonly MethodInfo BuildExecutionContractEscapePromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildExecutionContractEscapePrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildExecutionContractEscapePrompt not found.");
    private static readonly MethodInfo BuildContinuationSubsetEscapePromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildContinuationSubsetEscapePrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildContinuationSubsetEscapePrompt not found.");
    private static readonly MethodInfo TryBuildStructuredNextActionRetryPromptMethod =
        typeof(ChatServiceSession).GetMethod("TryBuildStructuredNextActionRetryPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryBuildStructuredNextActionRetryPrompt not found.");
    private static readonly MethodInfo TryBuildHostStructuredNextActionToolCallMethod =
        typeof(ChatServiceSession).GetMethod("TryBuildHostStructuredNextActionToolCall", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryBuildHostStructuredNextActionToolCall not found.");
    private static readonly MethodInfo ShouldAttemptToolProgressRecoveryMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAttemptToolProgressRecovery", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAttemptToolProgressRecovery not found.");
    private static readonly MethodInfo ShouldAllowHostStructuredNextActionReplayMethod =
        typeof(ChatServiceSession).GetMethod("ShouldAllowHostStructuredNextActionReplay", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldAllowHostStructuredNextActionReplay not found.");
    private static readonly MethodInfo SupportsSyntheticHostReplayItemsMethod =
        typeof(ChatServiceSession).GetMethod("SupportsSyntheticHostReplayItems", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("SupportsSyntheticHostReplayItems not found.");
    private static readonly MethodInfo BuildHostReplayReviewInputMethod =
        typeof(ChatServiceSession).GetMethod("BuildHostReplayReviewInput", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildHostReplayReviewInput not found.");
    private static readonly MethodInfo ResolveToolOutputCallIdMethod =
        typeof(ChatServiceSession).GetMethod("ResolveToolOutputCallId", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveToolOutputCallId not found.");
    private static readonly MethodInfo BuildToolRoundReplayInputMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoundReplayInput", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoundReplayInput not found.");
    private static readonly MethodInfo BuildToolRoundReplayInputWithBudgetMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoundReplayInputWithBudget", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoundReplayInputWithBudget not found.");
    private static readonly MethodInfo ResolveContextAwareReplayOutputCharBudgetsMethod =
        typeof(ChatServiceSession).GetMethod("ResolveContextAwareReplayOutputCharBudgets", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveContextAwareReplayOutputCharBudgets not found.");
    private static readonly MethodInfo BuildReplayOutputCompactionStatusMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildReplayOutputCompactionStatusMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildReplayOutputCompactionStatusMessage not found.");
    private static readonly MethodInfo BuildNativeHostReplayReviewPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildNativeHostReplayReviewPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildNativeHostReplayReviewPrompt not found.");
    private static readonly MethodInfo ShouldTriggerNoResultPhaseLoopWatchdogMethod =
        typeof(ChatServiceSession).GetMethod("ShouldTriggerNoResultPhaseLoopWatchdog", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldTriggerNoResultPhaseLoopWatchdog not found.");
    private static readonly MethodInfo ShouldEmitInterimResultSnapshotMethod =
        typeof(ChatServiceSession).GetMethod("ShouldEmitInterimResultSnapshot", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldEmitInterimResultSnapshot not found.");
    private static readonly MethodInfo RebuildPackCapabilityFallbackContractsMethod =
        typeof(ChatServiceSession).GetMethod("RebuildPackCapabilityFallbackContracts", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RebuildPackCapabilityFallbackContracts not found.");
    private static readonly MethodInfo TryBuildPackCapabilityFallbackToolCallMethod =
        typeof(ChatServiceSession).GetMethod("TryBuildPackCapabilityFallbackToolCall", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TryBuildPackCapabilityFallbackToolCall not found.");
    private static readonly MethodInfo BuildToolProgressRecoveryPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolProgressRecoveryPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolProgressRecoveryPrompt not found.");
    private static readonly MethodInfo BuildToolReceiptCorrectionPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolReceiptCorrectionPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolReceiptCorrectionPrompt not found.");
    private static readonly MethodInfo ResolveParallelToolConcurrencyMethod =
        typeof(ChatServiceSession).GetMethod("ResolveParallelToolConcurrency", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveParallelToolConcurrency not found.");
    private static readonly MethodInfo BuildToolBatchStartedMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolBatchStartedMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolBatchStartedMessage not found.");
    private static readonly MethodInfo BuildToolBatchProgressMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolBatchProgressMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolBatchProgressMessage not found.");
    private static readonly MethodInfo BuildToolBatchHeartbeatMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolBatchHeartbeatMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolBatchHeartbeatMessage not found.");
    private static readonly MethodInfo FinalizeToolBatchHeartbeatAsyncMethod =
        typeof(ChatServiceSession).GetMethod("FinalizeToolBatchHeartbeatAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("FinalizeToolBatchHeartbeatAsync not found.");
    private static readonly MethodInfo BuildToolBatchCompletedMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolBatchCompletedMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolBatchCompletedMessage not found.");
    private static readonly MethodInfo BuildToolRoundStartedMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoundStartedMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoundStartedMessage not found.");
    private static readonly MethodInfo BuildToolRoundCapAppliedMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoundCapAppliedMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoundCapAppliedMessage not found.");
    private static readonly MethodInfo BuildToolRoundCompletedMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoundCompletedMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoundCompletedMessage not found.");
    private static readonly MethodInfo BuildToolRoundLimitReachedMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoundLimitReachedMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoundLimitReachedMessage not found.");
    private static readonly MethodInfo CollectLowConcurrencyRecoveryIndexesMethod =
        typeof(ChatServiceSession).GetMethod("CollectLowConcurrencyRecoveryIndexes", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("CollectLowConcurrencyRecoveryIndexes not found.");
    private static readonly MethodInfo ShouldReplayToolCallAtLowConcurrencyMethod =
        typeof(ChatServiceSession).GetMethod("ShouldReplayToolCallAtLowConcurrency", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldReplayToolCallAtLowConcurrency not found.");
    private static readonly MethodInfo IsLikelyMutatingToolNameMethod =
        typeof(ChatServiceSession).GetMethod("IsLikelyMutatingToolName", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsLikelyMutatingToolName not found.");
    private static readonly MethodInfo HasLikelyMutatingToolCallsMethod =
        typeof(ChatServiceSession).GetMethod("HasLikelyMutatingToolCalls", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("HasLikelyMutatingToolCalls not found.");
    private static readonly MethodInfo BuildToolBatchRecoveringMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolBatchRecoveringMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolBatchRecoveringMessage not found.");
    private static readonly MethodInfo BuildToolBatchRecoveredMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolBatchRecoveredMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolBatchRecoveredMessage not found.");
    private static readonly MethodInfo BuildToolHeartbeatMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolHeartbeatMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolHeartbeatMessage not found.");
    private static readonly MethodInfo BuildMutatingToolHintsByNameMethod =
        typeof(ChatServiceSession).GetMethod("BuildMutatingToolHintsByName", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMutatingToolHintsByName not found.");
    private static readonly MethodInfo NormalizeParallelToolModeMethod =
        typeof(ChatServiceSession).GetMethod("NormalizeParallelToolMode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("NormalizeParallelToolMode not found.");
    private static readonly MethodInfo ResolveParallelToolExecutionModeMethod =
        typeof(ChatServiceSession).GetMethod("ResolveParallelToolExecutionMode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveParallelToolExecutionMode not found.");
    private static readonly MethodInfo BuildReviewPassClampMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildReviewPassClampMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildReviewPassClampMessage not found.");
    private static readonly MethodInfo BuildModelHeartbeatClampMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildModelHeartbeatClampMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildModelHeartbeatClampMessage not found.");
    private static readonly MethodInfo ExtractPrimaryUserRequestMethod =
        typeof(ChatServiceSession).GetMethod("ExtractPrimaryUserRequest", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractPrimaryUserRequest not found.");
    private static readonly MethodInfo ExtractIntentUserTextMethod =
        typeof(ChatServiceSession).GetMethod("ExtractIntentUserText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractIntentUserText not found.");
    private static readonly MethodInfo RememberUserIntentMethod =
        typeof(ChatServiceSession).GetMethod("RememberUserIntent", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberUserIntent not found.");
    private static readonly MethodInfo RememberPendingActionsMethod =
        typeof(ChatServiceSession).GetMethod("RememberPendingActions", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberPendingActions not found.");
    private static readonly MethodInfo RememberStructuredNextActionCarryoverMethod =
        typeof(ChatServiceSession).GetMethod("RememberStructuredNextActionCarryover", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberStructuredNextActionCarryover not found.");
    private static readonly MethodInfo TryBuildCarryoverStructuredNextActionToolCallMethod =
        typeof(ChatServiceSession).GetMethod("TryBuildCarryoverStructuredNextActionToolCall", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("TryBuildCarryoverStructuredNextActionToolCall not found.");
    private static readonly MethodInfo ExpandContinuationUserRequestMethod =
        typeof(ChatServiceSession).GetMethod("ExpandContinuationUserRequest", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ExpandContinuationUserRequest not found.");
    private static readonly MethodInfo TryValidateChatRequestOptionsMethod =
        typeof(ChatServiceSession).GetMethod("TryValidateChatRequestOptions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryValidateChatRequestOptions not found.");
    private static readonly MethodInfo ParsePlannerSelectedDefinitionsMethod =
        typeof(ChatServiceSession).GetMethod("ParsePlannerSelectedDefinitions", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ParsePlannerSelectedDefinitions not found.");
    private static readonly MethodInfo ResolveRoutingStrategyMethod =
        typeof(ChatServiceSession).GetMethod("ResolveRoutingStrategy", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveRoutingStrategy not found.");
    private static readonly MethodInfo BuildRoutingSelectionMessageMethod =
        typeof(ChatServiceSession).GetMethod("BuildRoutingSelectionMessage", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRoutingSelectionMessage not found.");
    private static readonly MethodInfo ShouldEmitRoutingTransparencyMethod =
        typeof(ChatServiceSession).GetMethod("ShouldEmitRoutingTransparency", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ShouldEmitRoutingTransparency not found.");
    private static readonly MethodInfo BuildRoutingMetaPayloadMethod =
        typeof(ChatServiceSession).GetMethod("BuildRoutingMetaPayload", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildRoutingMetaPayload not found.");
    private static readonly MethodInfo NormalizeRoutingToolCountsMethod =
        typeof(ChatServiceSession).GetMethod("NormalizeRoutingToolCounts", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("NormalizeRoutingToolCounts not found.");
    private static readonly FieldInfo ToolRoutingContextLockField =
        typeof(ChatServiceSession).GetField("_toolRoutingContextLock", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolRoutingContextLock not found.");
    private static readonly FieldInfo LastUserIntentByThreadIdField =
        typeof(ChatServiceSession).GetField("_lastUserIntentByThreadId", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_lastUserIntentByThreadId not found.");
    private static readonly FieldInfo LastUserIntentSeenUtcTicksField =
        typeof(ChatServiceSession).GetField("_lastUserIntentSeenUtcTicks", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_lastUserIntentSeenUtcTicks not found.");
    private static readonly FieldInfo ToolPackIdsByToolNameField =
        typeof(ChatServiceSession).GetField("_toolPackIdsByToolName", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_toolPackIdsByToolName not found.");

    [Fact]
    public void TrimToolRoutingStatsForTesting_RemovesNonPositiveTimestampEntriesFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var stats = new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < MaxTrackedToolRoutingStats; i++) {
            stats[$"active-{i:D3}"] = (10_000L + i, 0);
        }

        stats["stale-zero"] = (0, 0);
        stats["stale-negative"] = (-50, -50);

        session.SetToolRoutingStatsForTesting(stats);
        session.TrimToolRoutingStatsForTesting();

        var names = new HashSet<string>(session.GetTrackedToolRoutingStatNamesForTesting(), StringComparer.OrdinalIgnoreCase);

        Assert.Equal(MaxTrackedToolRoutingStats, names.Count);
        Assert.DoesNotContain("stale-zero", names);
        Assert.DoesNotContain("stale-negative", names);
        Assert.Contains("active-000", names);
        Assert.Contains($"active-{MaxTrackedToolRoutingStats - 1:D3}", names);
    }

    [Fact]
    public void TrimWeightedRoutingContextsForTesting_RemovesMissingAndZeroTickEntriesFirst() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);

        var names = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var seenTicks = new Dictionary<string, long>(StringComparer.Ordinal);
        for (var i = 0; i < MaxTrackedWeightedRoutingContexts; i++) {
            var threadId = $"thread-{i:D3}";
            names[threadId] = new[] { $"tool-{i:D3}" };
            seenTicks[threadId] = 50_000L + i;
        }

        names["thread-missing"] = new[] { "tool-missing" };
        names["thread-zero"] = new[] { "tool-zero" };
        seenTicks["thread-zero"] = 0;

        session.SetWeightedRoutingContextsForTesting(names, seenTicks);
        session.TrimWeightedRoutingContextsForTesting();

        var trackedThreadIds = new HashSet<string>(session.GetTrackedWeightedRoutingContextThreadIdsForTesting(), StringComparer.Ordinal);

        Assert.Equal(MaxTrackedWeightedRoutingContexts, trackedThreadIds.Count);
        Assert.DoesNotContain("thread-missing", trackedThreadIds);
        Assert.DoesNotContain("thread-zero", trackedThreadIds);
        Assert.Contains("thread-000", trackedThreadIds);
        Assert.Contains($"thread-{MaxTrackedWeightedRoutingContexts - 1:D3}", trackedThreadIds);
    }

    [Fact]
    public void UpdateToolRoutingStats_TracksOutputsWhenCallIdsDifferOnlyByWhitespace() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var calls = new List<ToolCall> {
            new("  call-001  ", "ad_replication_summary", null, null, new JsonObject())
        };
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-001",
                Output = "{\"ok\":true}",
                Ok = true
            }
        };

        var updateMethod = typeof(ChatServiceSession).GetMethod("UpdateToolRoutingStats", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(updateMethod);

        updateMethod!.Invoke(session, new object[] { calls, outputs });

        var names = session.GetTrackedToolRoutingStatNamesForTesting();
        Assert.Contains(names, static name => string.Equals(name, "ad_replication_summary", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("run now")]
    [InlineData("go for it")]
    [InlineData("do it")]
    [InlineData("yes run it")]
    [InlineData("dzialaj")]
    [InlineData("uruchom to")]
    [InlineData("dalej?")]
    [InlineData("please continue failed logon report for ado？")]
    [InlineData("please continue failed logon report for ado؟")]
    [InlineData("ok can you check if other dcs had similar patterns?")]
    [InlineData("继续")]
    [InlineData("继续执行")]
    [InlineData("xqzz ltmv")]
    [InlineData("frobulate proceed")]
    public void LooksLikeContinuationFollowUp_RecognizesCompactFollowUpsAcrossLanguages(string userText) {
        var result = LooksLikeContinuationFollowUpMethod.Invoke(null, new object?[] { userText });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TreatsCompactFollowUpByShapeNotKeyword() {
        var keywordRequest = "do it";
        var keywordDraft = "Proceeding with do it now.";
        var shapeOnlyRequest = "xqzz ltmv";
        var shapeOnlyDraft = "Proceeding with xqzz ltmv now.";

        var keywordResult = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { keywordRequest, keywordDraft, true, 0, 0, true });
        var shapeOnlyResult = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { shapeOnlyRequest, shapeOnlyDraft, true, 0, 0, true });

        Assert.Equal(Assert.IsType<bool>(keywordResult), Assert.IsType<bool>(shapeOnlyResult));
        Assert.True(Assert.IsType<bool>(shapeOnlyResult));
    }

    [Fact]
    public void CountLetterDigitTokens_CapsAtMaxTokens() {
        var twelve = "a b c d e f g h i j k l";
        var thirteen = "a b c d e f g h i j k l m";

        var result12 = CountLetterDigitTokensMethod.Invoke(null, new object?[] { twelve, 12 });
        Assert.Equal(12, Assert.IsType<int>(result12));

        var result13 = CountLetterDigitTokensMethod.Invoke(null, new object?[] { thirteen, 12 });
        Assert.Equal(12, Assert.IsType<int>(result13));
    }

    [Fact]
    public void BuildToolExecutionNudgePrompt_EmitsStableMarker() {
        var result = BuildToolExecutionNudgePromptMethod.Invoke(null, new object?[] { "run now", "draft" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-correction:v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForDeferredDraftWithoutToolCalls() {
        var userRequest = "run now?";
        var assistantDraft = "If you say \"run now\", I'll execute forest-wide checks immediately.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerWhenDraftLooksLikeExecutionCorrectionEcho() {
        var userRequest = "run now";
        var assistantDraft = """
            [Execution correction]
            ix:execution-correction:v1
            The previous assistant draft did not execute tools.

            Execute available tools now when they can satisfy this request.
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Theory]
    [InlineData("run now")]
    [InlineData("yes - run now")]
    [InlineData("please `run now`")]
    public void ShouldAttemptToolExecutionNudge_TriggersWhenUserEchoesQuotedCallToActionEvenWithoutContinuationSubset(string userRequest) {
        var assistantDraft = "If you say \"run now\", I'll execute forest-wide checks immediately.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Theory]
    [InlineData("- 'run now'")]
    [InlineData("-   'run now'")]
    [InlineData("* 'run now'")]
    [InlineData("*    'run now'")]
    [InlineData("1. 'run now'")]
    [InlineData("1) 'run now'")]
    [InlineData("1: 'run now'")]
    [InlineData("  12) 'run now'")]
    public void ShouldAttemptToolExecutionNudge_TriggersForBulletFormattedQuotedCallToActionWithoutContinuationSubset(string bulletLine) {
        var userRequest = "run now";
        var assistantDraft = $"Pick one:\n{bulletLine}\n";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersWhenQuotedCallToActionIsOnItsOwnLineWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = """
            To proceed:
            'run now'
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersWhenQuotedCallToActionIsOnItsOwnLineAtEndOfStringWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = "To proceed:\n'run now'";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersWhenQuotedCallToActionIsIndentedOnItsOwnLineWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = "To proceed:\n  'run now'";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerWhenQuotedTextHasTrailingContentOnSameLineWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = "To proceed:\n  'run now' trailing text";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForStandaloneQuotedLineWithoutLeadInLabelWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = "I saw this in logs\n'run now'";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForColonLabelQuotedTextWithoutContinuationSubset() {
        var userRequest = "access denied";
        var assistantDraft = "Status: 'access denied'";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForCommaAfterQuoteCallToActionWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = "If you say \"run now\", I'll execute checks.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForQuotedJsonValueWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = """
            {
              "note": "run now",
              "status": "ok"
            }
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForMultilineIncidentalQuotedTextWithoutContinuationSubset() {
        var userRequest = "run now";
        var assistantDraft = """
            First line.
            I saw "run now", in the logs, but I can retry if needed.
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerWithoutContinuationSubsetWhenDraftDoesNotContainEchoableCallToAction() {
        var userRequest = "run now";
        var assistantDraft = "I can help, but I need a DC FQDN first.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForQuotedNonCallToActionWhenContinuationSubsetNotUsed() {
        var userRequest = "access denied";
        var assistantDraft = "I got \"access denied\" from the server, but I can retry if needed.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForSingleQuotedCallToActionEvenWithApostrophesInText() {
        var assistantDraft = "Don't worry. If you say 'run now', I'll execute forest-wide checks immediately.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { "run now", assistantDraft, true, 0, 0, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerWhenSingleQuoteIsUnbalanced() {
        var assistantDraft = "If you say 'run now, I'll execute forest-wide checks immediately.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { "run now", assistantDraft, true, 0, 0, false });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_UsesCallToActionQuoteWhenMultipleQuotedSegmentsPresent() {
        var assistantDraft = "If you say \"run now\", I'll execute forest-wide checks. Last error was \"access denied\".";

        var resultCta = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { "run now", assistantDraft, true, 0, 0, false });
        Assert.True(Assert.IsType<bool>(resultCta));

        var resultNonCta = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { "access denied", assistantDraft, true, 0, 0, false });
        Assert.False(Assert.IsType<bool>(resultNonCta));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerOnUnlinkedQuestionDraft() {
        var userRequest = "run now";
        var assistantDraft = "Are you sure?";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerOnUnlinkedUnicodeQuestionDraft() {
        var userRequest = "run now";
        var assistantDraft = "Czy na pewno？";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Theory]
    [InlineData("please continue failed logon report for ado?", true)]
    [InlineData("please continue failed logon report for ado？", true)]
    [InlineData("please continue failed logon report for ado؟", true)]
    [InlineData("ok can you check if other dcs had similar patterns?", true)]
    [InlineData("please continue failed logon report for ado", false)]
    public void LooksLikeCompactFollowUp_RecognizesUnicodeQuestionPunctuation(string userRequest, bool expected) {
        var result = LooksLikeCompactFollowUpMethod.Invoke(null, new object?[] { userRequest });
        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForExplicitCapabilityBlocker() {
        var userRequest = "Get top 5 events from ADO system log.";
        var assistantDraft = "I can't query remote ADO live logs directly without machine access.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, true });

        Assert.False(Assert.IsType<bool>(result));
    }

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

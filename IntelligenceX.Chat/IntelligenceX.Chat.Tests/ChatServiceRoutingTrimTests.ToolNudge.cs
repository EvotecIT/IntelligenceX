using System;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForActionSelectionJsonEvenWithoutContinuationSubset() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run forest probe\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForNumericActionSelectionId() {
        var userRequest = "{\"ix_action_selection\":{\"id\":1,\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotThrowOnInvalidActionSelectionJson() {
        var userRequest = "{";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWithEmptyId() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWithZeroNumericId() {
        var userRequest = "{\"ix_action_selection\":{\"id\":0,\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWhenSelectionIsNotObject() {
        var userRequest = "{\"ix_action_selection\":null}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWhenToolsUnavailable() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, false, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWhenToolCallsAlreadyExist() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 1, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_TriggersForActionSelectionPayload() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { userRequest });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_DoesNotTriggerForPlainTextRequest() {
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { "run now" });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_TriggersForStrictNoToolTurnAfterRecoveryAttempt() {
        var args = new object?[] {
            "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}",
            "Ok, doing it now.",
            true,
            0,
            0,
            0,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("strict_contract_watchdog_retry", Assert.IsType<string>(args[9]));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_DoesNotTriggerWhenToolsUnavailable() {
        var args = new object?[] {
            "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}",
            "Ok, doing it now.",
            false,
            0,
            0,
            0,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("tools_unavailable", Assert.IsType<string>(args[9]));
    }

    [Fact]
    public void BuildNoToolExecutionWatchdogPrompt_EmitsStableMarker() {
        var result = BuildNoToolExecutionWatchdogPromptMethod.Invoke(null, new object?[] { "run now", "draft" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-watchdog:v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_EmitsStableMarkerAndReason() {
        var result = BuildExecutionContractBlockerTextMethod.Invoke(null, new object?[] { "run now", "draft", "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-contract:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no_tool_calls_after_watchdog_retry", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractEscapePrompt_EmitsStableMarker() {
        var result = BuildExecutionContractEscapePromptMethod.Invoke(null, new object?[] { "run now", "draft" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-contract-escape:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("full tool availability", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_EmbedsReplayableActionEnvelopeForSelectionPayload() {
        var request = "{\"ix_action_selection\":{\"id\":\"act_failed4625\",\"title\":\"Failed logons (4625)\",\"request\":\"Run failed logon report on ADO Security.\"}}";
        var result = BuildExecutionContractBlockerTextMethod.Invoke(null, new object?[] { request, "draft", "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("[Action]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:action:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: act_failed4625", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply: /act act_failed4625", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_EmbedsReplayableActionEnvelopeForSingleActionAssistantDraft() {
        var draft = """
            Pulling failed logons from ADO now would be the next step.
            You can run one of these follow-up actions:
            1. Run failed logon report (4625) on ADO Security (/act act_failed4625)
            """;
        var result = BuildExecutionContractBlockerTextMethod.Invoke(
            null,
            new object?[] { "failed logons please", draft, "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("[Action]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:action:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: act_failed4625", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply: /act act_failed4625", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_DoesNotEmbedReplayableActionEnvelopeForAmbiguousAssistantDraft() {
        var draft = """
            You can run one of these follow-up actions:
            1. Run failed logon report (4625) on ADO Security (/act act_failed4625)
            2. Pull account lockout events (4740) on ADO Security (/act act_lockouts4740)
            """;
        var result = BuildExecutionContractBlockerTextMethod.Invoke(
            null,
            new object?[] { "security log please", draft, "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.DoesNotContain("ix:action:v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldSkipWeightedRouting_TrueForActionSelectionPayload() {
        var request = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var result = ShouldSkipWeightedRoutingMethod.Invoke(null, new object?[] { request });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSkipWeightedRouting_TrueForCompactFollowUp() {
        var result = ShouldSkipWeightedRoutingMethod.Invoke(null, new object?[] { "run now" });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSkipWeightedRouting_FalseForRegularRequestText() {
        var result = ShouldSkipWeightedRoutingMethod.Invoke(null, new object?[] { "Show failed logons across all domain controllers for the last 24 hours with source IP breakdown." });

        Assert.False(Assert.IsType<bool>(result));
    }

}

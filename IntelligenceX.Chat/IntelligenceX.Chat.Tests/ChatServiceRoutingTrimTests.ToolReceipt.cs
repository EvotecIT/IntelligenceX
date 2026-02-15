using System;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_TriggersWhenDraftBindsToolNameToReturned() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var assistantDraft = "ad_search returned 2 users.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Find Bob", assistantDraft, tools, 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForSuggestionMentions() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var assistantDraft = "Use ad_search to find users under a specific OU.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "How do I find users?", assistantDraft, tools, 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_TriggersForToolNameColonJsonPattern() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("eventlog_live_query", "Event log", schema) };
        var assistantDraft = "eventlog_live_query: {\"count\":3}";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Show me recent errors", assistantDraft, tools, 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_TriggersForExitCodeReceiptFragmentsEvenWithoutToolNames() {
        var assistantDraft = "Process exited with code 0.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForCasualStdoutStderrMentions() {
        var assistantDraft = "If you want, I can show you stdout and stderr handling, but I did not run anything yet.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Explain it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_TriggersForStdoutReceiptLabel() {
        var assistantDraft = "stdout: hello world";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_TriggersForStdoutReceiptLabel_WithWhitespaceBeforeColon() {
        var assistantDraft = "stdout    : hello world";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_TriggersForStderrReceiptLabel() {
        var assistantDraft = "stderr: something failed";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerWhenToolCallsAlreadyExist() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var assistantDraft = "ad_search returned 2 users.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Find Bob", assistantDraft, tools, 1, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerWhenDraftExceedsMaxLength() {
        var maxField = typeof(ChatServiceSession).GetField("ToolReceiptCorrectionMaxDraftChars", BindingFlags.NonPublic | BindingFlags.Static)
                       ?? throw new InvalidOperationException("ToolReceiptCorrectionMaxDraftChars not found.");
        var max = (int)maxField.GetRawConstantValue()!;
        var assistantDraft = new string('A', max + 1);

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Request", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void BuildToolReceiptCorrectionPrompt_IncludesMarker() {
        var prompt = BuildToolReceiptCorrectionPromptMethod.Invoke(
            null,
            new object?[] { "Request", "Draft" });

        var value = Assert.IsType<string>(prompt);
        Assert.Contains("ix:tool-receipt-correction:v1", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolReceiptCorrectionPrompt_TruncatesOversizedUserRequest() {
        var oversized = new string('A', 2500) + "TAIL";

        var prompt = BuildToolReceiptCorrectionPromptMethod.Invoke(
            null,
            new object?[] { oversized, "Draft" });

        var value = Assert.IsType<string>(prompt);
        Assert.Contains("(truncated)", value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TAIL", value, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildToolReceiptCorrectionPrompt_DoesNotInjectEmptySentinelText() {
        var prompt = BuildToolReceiptCorrectionPromptMethod.Invoke(
            null,
            new object?[] { null, "   " });

        var value = Assert.IsType<string>(prompt);
        Assert.DoesNotContain("(empty)", value, StringComparison.OrdinalIgnoreCase);
    }
}

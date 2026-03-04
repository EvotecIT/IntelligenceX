using System;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForLexicalReturnedClaimWithoutStructuredBinding() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var assistantDraft = "ad_search returned 2 users.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Find Bob", assistantDraft, tools, 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
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
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForToolNameColonNumber() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("eventlog_live_query", "Event log", schema) };
        var assistantDraft = "eventlog_live_query: 80";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Show me recent errors", assistantDraft, tools, 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForToolNameColonNumber_WithWhitespace() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("eventlog_live_query", "Event log", schema) };
        var assistantDraft = "eventlog_live_query   : 80";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Show me recent errors", assistantDraft, tools, 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForExitCodeReceiptFragmentsWithoutStructuredBinding() {
        var assistantDraft = "Process exited with code 0.";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
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
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForStdoutReceiptLabelWithoutStructuredBinding() {
        var assistantDraft = "stdout: hello world";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForStdoutReceiptLabelWithWhitespaceBeforeColonWithoutStructuredBinding() {
        var assistantDraft = "stdout    : hello world";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolReceiptCorrection_DoesNotTriggerForStderrReceiptLabelWithoutStructuredBinding() {
        var assistantDraft = "stderr: something failed";

        var result = ShouldAttemptToolReceiptCorrectionMethod.Invoke(
            null,
            new object?[] { "Run it", assistantDraft, Array.Empty<ToolDefinition>(), 0, 0, 0 });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
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

    [Fact]
    public void AppendNoToolExecutionDisclosureIfNeeded_AppendsDisclosure_WhenDraftMentionsToolAndNoToolActivity() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var draft = "You can use ad_search to find matching users.";

        var result = AppendNoToolExecutionDisclosureIfNeededMethod.Invoke(
            null,
            new object?[] { draft, tools, 0, 0 });

        var value = Assert.IsType<string>(result);
        Assert.Contains("Tool receipt: no tools were run in this turn.", value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppendNoToolExecutionDisclosureIfNeeded_DoesNotAppend_WhenToolActivityExists() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var draft = "I will summarize the result.";

        var result = AppendNoToolExecutionDisclosureIfNeededMethod.Invoke(
            null,
            new object?[] { draft, tools, 1, 1 });

        var value = Assert.IsType<string>(result);
        Assert.Equal(draft, value);
    }

    [Fact]
    public void AppendNoToolExecutionDisclosureIfNeeded_DoesNotAppend_WhenDraftDoesNotMentionKnownTool() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var draft = "Here is a direct explanation without tool names.";

        var result = AppendNoToolExecutionDisclosureIfNeededMethod.Invoke(
            null,
            new object?[] { draft, tools, 0, 0 });

        var value = Assert.IsType<string>(result);
        Assert.Equal(draft, value);
    }

    [Fact]
    public void AppendNoToolExecutionDisclosureIfNeeded_DoesNotDuplicateDisclosure() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var tools = new[] { new ToolDefinition("ad_search", "AD search", schema) };
        var draft = """
                    You can use ad_search to find matching users.

                    Tool receipt: no tools were run in this turn.
                    """;

        var result = AppendNoToolExecutionDisclosureIfNeededMethod.Invoke(
            null,
            new object?[] { draft, tools, 0, 0 });

        var value = Assert.IsType<string>(result);
        Assert.Equal(draft, value);
    }
}

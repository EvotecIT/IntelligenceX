using System;
using System.Collections;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards execution-contract blocker collapsing so only prior assistant blockers trigger collapse.
/// </summary>
public sealed class MainWindowExecutionContractBlockerTests {
    private static readonly Type ConversationRuntimeType = typeof(MainWindow).GetNestedType("ConversationRuntime", BindingFlags.NonPublic)
                                                           ?? throw new InvalidOperationException("ConversationRuntime type not found.");

    private static readonly MethodInfo CollapseMethod = typeof(MainWindow).GetMethod(
                                                            "CollapseRepeatedExecutionContractBlockers",
                                                            BindingFlags.NonPublic | BindingFlags.Static)
                                                        ?? throw new InvalidOperationException("CollapseRepeatedExecutionContractBlockers not found.");

    private static readonly PropertyInfo MessagesProperty = ConversationRuntimeType.GetProperty("Messages", BindingFlags.Public | BindingFlags.Instance)
                                                           ?? throw new InvalidOperationException("ConversationRuntime.Messages property not found.");

    /// <summary>
    /// Ensures the current assistant blocker draft is not treated as historical evidence for repeat-collapse.
    /// </summary>
    [Fact]
    public void CollapseRepeatedExecutionContractBlockers_DoesNotCollapseWhenOnlyCurrentAssistantBlockerExists() {
        var current = BuildBlocker(actionId: "act_sidtrace", reasonCode: "execution_contract_unmet_tool_activity_absent");
        var conversation = CreateConversation(("Assistant", current));

        var collapsed = InvokeCollapse(conversation, current);

        Assert.Equal(current, collapsed);
    }

    /// <summary>
    /// Ensures collapse is applied only when a prior assistant blocker is present in conversation history.
    /// </summary>
    [Fact]
    public void CollapseRepeatedExecutionContractBlockers_CollapsesWhenPriorAssistantBlockerExists() {
        var previous = BuildBlocker(actionId: "act_sidtrace", reasonCode: "execution_contract_unmet_tool_activity_absent");
        var current = BuildBlocker(actionId: "act_sidtrace", reasonCode: "execution_contract_unmet_tool_activity_absent");
        var conversation = CreateConversation(
            ("Assistant", previous),
            ("User", "/act act_sidtrace"),
            ("Assistant", current));

        var collapsed = InvokeCollapse(conversation, current);

        Assert.Contains("Still blocked; no new tool output was produced in this retry.", collapsed, StringComparison.Ordinal);
        Assert.Contains("Action: /act act_sidtrace", collapsed, StringComparison.Ordinal);
        Assert.Contains("Reason code: execution_contract_unmet_tool_activity_absent", collapsed, StringComparison.Ordinal);
    }

    private static object CreateConversation(params (string Role, string Text)[] messages) {
        var conversation = Activator.CreateInstance(ConversationRuntimeType, nonPublic: true)
                           ?? throw new InvalidOperationException("Failed to create ConversationRuntime instance.");
        if (MessagesProperty.GetValue(conversation) is not IList list) {
            throw new InvalidOperationException("ConversationRuntime.Messages must implement IList.");
        }

        var baseTime = new DateTime(2026, 2, 16, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < messages.Length; i++) {
            list.Add((messages[i].Role, messages[i].Text, baseTime.AddSeconds(i)));
        }

        return conversation;
    }

    private static string InvokeCollapse(object conversation, string assistantText) {
        var result = CollapseMethod.Invoke(null, new[] { conversation, assistantText });
        return Assert.IsType<string>(result);
    }

    private static string BuildBlocker(string actionId, string reasonCode) {
        return $"""
                [Execution blocked]
                ix:execution-contract:v1
                I did not execute tools for this selected action in the current turn.

                Reason code: {reasonCode}
                id: {actionId}
                reply: /act {actionId}
                """;
    }
}

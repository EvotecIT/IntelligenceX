using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Validates planner and lexical-routing prompts include schema hints.
/// </summary>
public sealed class ChatServicePlannerPromptTests {
    private static readonly MethodInfo BuildModelPlannerPromptMethod =
        typeof(ChatServiceSession).GetMethod("BuildModelPlannerPrompt", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildModelPlannerPrompt not found.");

    private static readonly MethodInfo BuildToolRoutingSearchTextMethod =
        typeof(ChatServiceSession).GetMethod("BuildToolRoutingSearchText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildToolRoutingSearchText not found.");

    [Fact]
    public void BuildModelPlannerPrompt_IncludesSchemaArgumentsRequiredAndTableViewTrait() {
        var definitions = new List<ToolDefinition> {
            new(
                "eventlog_top_events",
                "Return top events from a log.",
                ToolSchema.Object(
                        ("log_name", ToolSchema.String("Log name.")),
                        ("machine_name", ToolSchema.String("Remote host.")))
                    .WithTableViewOptions()
                    .Required("log_name")
                    .NoAdditionalProperties())
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "top 5 system events from AD0",
            definitions,
            6
        }));

        Assert.Contains("required: log_name", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("args: ", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traits: table_view_projection", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesSchemaTokens() {
        var definition = new ToolDefinition(
            "eventlog_top_events",
            "Return top events from a log.",
            ToolSchema.Object(
                    ("log_name", ToolSchema.String("Log name.")),
                    ("machine_name", ToolSchema.String("Remote host.")))
                .WithTableViewOptions()
                .Required("log_name")
                .NoAdditionalProperties());

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("log_name", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("table view projection", searchText, StringComparison.OrdinalIgnoreCase);
    }
}

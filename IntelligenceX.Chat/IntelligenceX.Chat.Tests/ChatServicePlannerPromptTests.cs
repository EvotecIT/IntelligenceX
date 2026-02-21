using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
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

    private static readonly MethodInfo SelectWeightedToolSubsetMethod =
        typeof(ChatServiceSession).GetMethod("SelectWeightedToolSubset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("SelectWeightedToolSubset not found.");

    private static readonly MethodInfo ResolveMaxCandidateToolsSettingMethod =
        typeof(ChatServiceSession).GetMethod("ResolveMaxCandidateToolsSetting", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveMaxCandidateToolsSetting not found.");

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

    [Fact]
    public void SelectWeightedToolSubset_UsesRequestedLimit_WhenPromptHasNoRoutingSignal() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var args = new object?[] {
            definitions,
            "Please summarize release readiness trends for this quarter.",
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(4, selected.Count);
        Assert.Equal("ix_probe_tool_00", selected[0].Name);
        Assert.Equal("ix_probe_tool_03", selected[3].Name);
    }

    [Fact]
    public void SelectWeightedToolSubset_UsesRequestedLimit_WhenWeightedRoutingIsSkippedForShortPrompt() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 20; i++) {
            definitions.Add(new ToolDefinition(
                $"ix_probe_tool_{i:D2}",
                "Diagnostic probe.",
                ToolSchema.Object(("target", ToolSchema.String("Target host."))).NoAdditionalProperties()));
        }

        var args = new object?[] {
            definitions,
            "hello there",
            6,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(6, selected.Count);
        Assert.Equal("ix_probe_tool_00", selected[0].Name);
        Assert.Equal("ix_probe_tool_05", selected[5].Name);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_DefaultsCompatibleHttpToEight() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { null, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(8, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_PreservesExplicitRequest() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 17, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(17, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_ZeroUsesTransportDefaultForCompatibleHttp() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 0, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(8, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_ZeroUsesNoOverrideForNativeTransport() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 0, OpenAITransportKind.Native });
        Assert.Null(result);
    }

    [Fact]
    public void ResolveMaxCandidateToolsSetting_ClampsOversizedRequestToSafetyLimit() {
        var result = ResolveMaxCandidateToolsSettingMethod.Invoke(null, new object?[] { 999, OpenAITransportKind.CompatibleHttp });
        var value = Assert.IsType<int>(result);
        Assert.Equal(256, value);
    }
}

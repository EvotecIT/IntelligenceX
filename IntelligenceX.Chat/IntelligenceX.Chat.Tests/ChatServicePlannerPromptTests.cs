using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
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
    private static readonly MethodInfo ResolveContextAwareCompatibleHttpDefaultMaxCandidateToolsMethod =
        typeof(ChatServiceSession).GetMethod("ResolveContextAwareCompatibleHttpDefaultMaxCandidateTools", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ResolveContextAwareCompatibleHttpDefaultMaxCandidateTools not found.");
    private static readonly MethodInfo ResolveMaxCandidateToolsForTurnMethod =
        typeof(ChatServiceSession).GetMethod("ResolveMaxCandidateToolsForTurn", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ResolveMaxCandidateToolsForTurn not found.");
    private static readonly FieldInfo ModelListCacheField =
        typeof(ChatServiceSession).GetField("_modelListCache", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("_modelListCache not found.");
    private static readonly Type ModelListCacheEntryType =
        typeof(ChatServiceSession).GetNestedType("ModelListCacheEntry", BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ModelListCacheEntry type not found.");

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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
    public void SelectWeightedToolSubset_UsesFullToolSet_WhenWeightedRoutingIsSkippedForShortPrompt() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        Assert.Equal(definitions.Count, selected.Count);
        Assert.Equal("ix_probe_tool_00", selected[0].Name);
        Assert.Equal("ix_probe_tool_19", selected[19].Name);
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

    [Theory]
    [InlineData(0L, 8)]
    [InlineData(4096L, 4)]
    [InlineData(8192L, 4)]
    [InlineData(12000L, 6)]
    [InlineData(16384L, 6)]
    [InlineData(32768L, 8)]
    public void ResolveContextAwareCompatibleHttpDefaultMaxCandidateTools_UsesContextBands(long effectiveContextLength, int expected) {
        var result = ResolveContextAwareCompatibleHttpDefaultMaxCandidateToolsMethod.Invoke(null, new object?[] { effectiveContextLength });
        var value = Assert.IsType<int>(result);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_PreservesExplicitRequestForCompatibleHttp() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { 21, OpenAITransportKind.CompatibleHttp, "any-model" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(21, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_ReturnsNoOverrideForNativeWithoutRequest() {
        var session = BuildSessionWithModelCache(BuildModelInfo("native-model", loadedContextLength: 4096, maxContextLength: null));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.Native, "native-model" });

        Assert.Null(result);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_UsesMatchedModelLoadedContextForCompatibleHttp() {
        var session = BuildSessionWithModelCache(BuildModelInfo("ctx-small", loadedContextLength: 4096, maxContextLength: 32768));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "ctx-small" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(4, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_UsesMatchedModelMaxContextWhenLoadedContextMissing() {
        var session = BuildSessionWithModelCache(BuildModelInfo("ctx-max-only", loadedContextLength: null, maxContextLength: 12000));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "ctx-max-only" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(6, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_UsesSingleModelFallbackWhenSelectionDoesNotMatch() {
        var session = BuildSessionWithModelCache(BuildModelInfo("only-model", loadedContextLength: 12000, maxContextLength: null));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "unknown-model" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(6, value);
    }

    [Fact]
    public void ResolveMaxCandidateToolsForTurn_FallsBackToCompatibleHttpDefaultWhenModelSelectionUnknownAndMultipleModelsExist() {
        var session = BuildSessionWithModelCache(
            BuildModelInfo("model-a", loadedContextLength: 4096, maxContextLength: null),
            BuildModelInfo("model-b", loadedContextLength: 12000, maxContextLength: null));

        var result = ResolveMaxCandidateToolsForTurnMethod.Invoke(session, new object?[] { null, OpenAITransportKind.CompatibleHttp, "unknown-model" });
        var value = Assert.IsType<int>(result);

        Assert.Equal(8, value);
    }

    private static ChatServiceSession BuildSessionWithModelCache(params ModelInfo[] models) {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var modelList = new ModelListResult(models, nextCursor: null, raw: new JsonObject(), additional: null);
        var cacheEntry = Activator.CreateInstance(
            ModelListCacheEntryType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object?[] { "test-cache-key", DateTime.UtcNow.AddMinutes(5), modelList },
            culture: null);
        Assert.NotNull(cacheEntry);

        ModelListCacheField.SetValue(session, cacheEntry);
        return session;
    }

    private static ModelInfo BuildModelInfo(string id, long? loadedContextLength, long? maxContextLength) {
        return new ModelInfo(
            id: id,
            model: id,
            displayName: id,
            description: string.Empty,
            supportedReasoningEfforts: Array.Empty<ReasoningEffortOption>(),
            defaultReasoningEffort: null,
            isDefault: false,
            raw: new JsonObject(),
            additional: null,
            maxContextLength: maxContextLength,
            loadedContextLength: loadedContextLength,
            capabilities: Array.Empty<string>());
    }
}

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
    private static readonly MethodInfo TokenizeRoutingTokensMethod =
        typeof(ChatServiceSession).GetMethod("TokenizeRoutingTokens", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TokenizeRoutingTokens not found.");

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
    public void BuildModelPlannerPrompt_IncludesCategoryFamilyAndTagsHints() {
        var definitions = new List<ToolDefinition> {
            new(
                "domaindetective_domain_summary",
                "Domain posture summary.",
                ToolSchema.Object(("domain", ToolSchema.String("Domain name."))).Required("domain").NoAdditionalProperties(),
                category: "dns",
                tags: new[] { "intent:public_domain", "pack:domaindetective" })
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "summarize contoso.com",
            definitions,
            4
        }));

        Assert.Contains("category: dns", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack: domaindetective", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_aliases: domain_detective", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("family: public_domain", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tags: intent:public_domain", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:domaindetective", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackHintFromToolNameWhenTagMissing() {
        var definitions = new List<ToolDefinition> {
            new(
                "system_info",
                "System inventory.",
                ToolSchema.Object().NoAdditionalProperties())
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "collect host baseline",
            definitions,
            4
        }));

        Assert.Contains("pack: system", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackAliasesForEventLogCategoryAlias() {
        var definitions = new List<ToolDefinition> {
            new(
                "event_log_query",
                "Query event log entries.",
                ToolSchema.Object(("log_name", ToolSchema.String("Log name."))).NoAdditionalProperties(),
                category: "event_log")
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "collect system log evidence",
            definitions,
            4
        }));

        Assert.Contains("pack: eventlog", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack_aliases: event_log", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackHintFromComputerXAliasPrefixWhenTagMissing() {
        var definitions = new List<ToolDefinition> {
            new(
                "computerx_inventory_snapshot",
                "ComputerX inventory snapshot.",
                ToolSchema.Object().NoAdditionalProperties())
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "collect host baseline",
            definitions,
            4
        }));

        Assert.Contains("pack: system", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackHintFromDomainDetectiveAliasPrefixWhenTagMissing() {
        var definitions = new List<ToolDefinition> {
            new(
                "domain_detective_domain_summary",
                "Domain posture summary.",
                ToolSchema.Object(("domain", ToolSchema.String("Domain name."))).NoAdditionalProperties())
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "summarize contoso.com",
            definitions,
            4
        }));

        Assert.Contains("pack: domaindetective", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackHintFromHyphenatedAliasPrefixWhenTagMissing() {
        var definitions = new List<ToolDefinition> {
            new(
                "domain-detective-domain-summary",
                "Domain posture summary.",
                ToolSchema.Object(("domain", ToolSchema.String("Domain name."))).NoAdditionalProperties())
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "summarize contoso.com",
            definitions,
            4
        }));

        Assert.Contains("pack: domaindetective", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelPlannerPrompt_IncludesPackHintFromHyphenatedComputerXAliasPrefixWhenTagMissing() {
        var definitions = new List<ToolDefinition> {
            new(
                "computerx-inventory-snapshot",
                "ComputerX inventory snapshot.",
                ToolSchema.Object().NoAdditionalProperties())
        };

        var prompt = Assert.IsType<string>(BuildModelPlannerPromptMethod.Invoke(null, new object?[] {
            "collect host baseline",
            definitions,
            4
        }));

        Assert.Contains("pack: system", prompt, StringComparison.OrdinalIgnoreCase);
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
    public void BuildToolRoutingSearchText_IncludesPackTokensFromNameFallback() {
        var definition = new ToolDefinition(
            "ad_get_users",
            "Read directory users.",
            ToolSchema.Object(("domain", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties());

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack active_directory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack adplayground", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack ad_playground", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesActiveDirectoryLiteralAliasWhenPackCategoryIsAd() {
        var definition = new ToolDefinition(
            "directory_scope_probe",
            "Directory scope probe.",
            ToolSchema.Object(("domain", ToolSchema.String("Domain DNS name."))).NoAdditionalProperties(),
            category: "ad");

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack ad", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack active_directory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:active_directory", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack ad_playground", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:ad_playground", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesComputerXAliasForSystemPack() {
        var definition = new ToolDefinition(
            "inventory_collect",
            "Collect host inventory.",
            ToolSchema.Object(("computer_name", ToolSchema.String("Target host."))).NoAdditionalProperties(),
            category: "computerx");

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack system", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack computerx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack computer_x", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:computer_x", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesDnsClientXAliasTokensFromToolNamePrefixFallback() {
        var definition = new ToolDefinition(
            "dns_client_x_query",
            "Query DNS records.",
            ToolSchema.Object(("name", ToolSchema.String("Name."))).NoAdditionalProperties());

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack dnsclientx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:dnsclientx", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack dns_client_x", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:dns_client_x", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesDomainDetectivePackTokensFromHyphenatedToolNamePrefixFallback() {
        var definition = new ToolDefinition(
            "domain-detective-query",
            "Query domain evidence.",
            ToolSchema.Object(("name", ToolSchema.String("Name."))).NoAdditionalProperties());

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack domaindetective", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:domaindetective", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack domain_detective", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:domain_detective", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesTestimoXUnderscoreAliasTokens() {
        var definition = new ToolDefinition(
            "health_rules",
            "Run TestimoX rules.",
            ToolSchema.Object(("scope", ToolSchema.String("Scope."))).NoAdditionalProperties(),
            category: "testimox");

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack testimox", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:testimox", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack testimo_x", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:testimo_x", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolRoutingSearchText_IncludesEventLogUnderscoreAliasTokens() {
        var definition = new ToolDefinition(
            "event_log_query",
            "Query event log entries.",
            ToolSchema.Object(("log_name", ToolSchema.String("Log name."))).NoAdditionalProperties(),
            category: "event_log");

        var searchText = Assert.IsType<string>(BuildToolRoutingSearchTextMethod.Invoke(null, new object?[] { definition }));

        Assert.Contains("pack eventlog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:eventlog", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack event_log", searchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack:event_log", searchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TokenizeRoutingTokens_PreservesSeparatorAwarePackAliasTokensAndCompactVariants() {
        var result = TokenizeRoutingTokensMethod.Invoke(
            null,
            new object?[] { "Use pack:domain_detective and pack:dns-client-x with pack:testimo_x.", 16 });
        var tokens = Assert.IsType<string[]>(result);

        Assert.Contains(tokens, token => string.Equals(token, "domain_detective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "domaindetective", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "dns_client_x", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "dnsclientx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "testimo_x", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "testimox", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TokenizeRoutingTokens_PreservesShortPackAliasToken_WhenPrefixedByPackMarker() {
        var result = TokenizeRoutingTokensMethod.Invoke(
            null,
            new object?[] { "Use pack:ad and pack:system for this task.", 16 });
        var tokens = Assert.IsType<string[]>(result);

        Assert.Contains(tokens, token => string.Equals(token, "ad", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, "system", StringComparison.OrdinalIgnoreCase));
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
    public void SelectWeightedToolSubset_NoSignalFallback_DiversifiesAcrossToolFamilies() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition>();
        for (var i = 0; i < 10; i++) {
            definitions.Add(new ToolDefinition(
                $"ad_query_{i:D2}",
                "AD query.",
                ToolSchema.Object(("target", ToolSchema.String("Target"))).NoAdditionalProperties()));
        }

        for (var i = 0; i < 3; i++) {
            definitions.Add(new ToolDefinition(
                $"eventlog_query_{i:D2}",
                "Event query.",
                ToolSchema.Object(("target", ToolSchema.String("Target"))).NoAdditionalProperties()));
        }

        for (var i = 0; i < 3; i++) {
            definitions.Add(new ToolDefinition(
                $"system_info_{i:D2}",
                "System query.",
                ToolSchema.Object(("target", ToolSchema.String("Target"))).NoAdditionalProperties()));
        }

        var args = new object?[] {
            definitions,
            "Please summarize release readiness trends for this quarter.",
            4,
            null
        };
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(SelectWeightedToolSubsetMethod.Invoke(session, args));

        Assert.Equal(4, selected.Count);
        Assert.Contains(selected, tool => string.Equals(tool.Name, "ad_query_00", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "eventlog_query_00", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, tool => string.Equals(tool.Name, "system_info_00", StringComparison.OrdinalIgnoreCase));
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

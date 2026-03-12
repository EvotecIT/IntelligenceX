using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class HostNoToolRetryHeuristicsTests {
    private static bool InvokeShouldRetryNoToolExecution(string userRequest, string assistantDraft) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldRetryNoToolExecution",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, assistantDraft });
        return value is bool b && b;
    }

    private static bool InvokeShouldRetryScenarioContractRepair(string userRequest, IReadOnlyList<ToolCall> calls) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldRetryScenarioContractRepair",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, calls });
        return value is bool b && b;
    }

    private static string? InvokeResolveScenarioRepairForcedToolName(
        string userRequest,
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        int retryAttempt) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ResolveScenarioRepairForcedToolName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, calls, toolDefinitions, retryAttempt });
        return value as string;
    }

    private static bool InvokePatternMatchesToolName(string pattern, string toolName) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "PatternMatchesToolName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { pattern, toolName });
        return value is bool b && b;
    }

    private static string InvokeBuildNoToolExecutionRetryPrompt(
        string userRequest,
        string assistantDraft,
        int retryAttempt,
        IReadOnlyList<string> knownHostTargets,
        IReadOnlyList<ToolDefinition>? toolDefinitions = null) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "BuildNoToolExecutionRetryPrompt",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: new[] {
                typeof(string),
                typeof(string),
                typeof(int),
                typeof(IReadOnlyList<ToolDefinition>),
                typeof(IReadOnlyList<string>)
            },
            modifiers: null);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] {
            userRequest,
            assistantDraft,
            retryAttempt,
            toolDefinitions ?? Array.Empty<ToolDefinition>(),
            knownHostTargets
        });
        return Assert.IsType<string>(value);
    }

    private static bool InvokeShouldRetryModelPhaseAttempt(
        Exception ex,
        int attempt,
        int maxAttempts,
        CancellationToken cancellationToken) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldRetryModelPhaseAttempt",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { ex, attempt, maxAttempts, cancellationToken });
        return value is bool b && b;
    }

    private static async Task<(bool timedOut, string? output)> InvokeWaitForToolOutputWithTimeoutAsync(
        Task<string> invocationTask,
        int timeoutSeconds,
        CancellationToken cancellationToken) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "WaitForToolOutputWithTimeoutAsync",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        dynamic task = method!.Invoke(null, new object?[] { invocationTask, timeoutSeconds, cancellationToken })!;
        await task;
        bool timedOut = task.Result.Item1;
        string? output = task.Result.Item2;
        return (timedOut, output);
    }

    private static string InvokeBuildNoTextReplFallbackTextForTesting(
        string assistantDraft,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<ToolOutput> toolOutputs,
        string? model,
        OpenAITransportKind transport,
        string? baseUrl) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "BuildNoTextReplFallbackTextForTesting",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { assistantDraft, toolCalls, toolOutputs, model, transport, baseUrl });
        return Assert.IsType<string>(value);
    }

    private static string InvokeBuildNoTextToolOutputRetryPromptForTesting(
        string userRequest,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<ToolOutput> toolOutputs) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "BuildNoTextToolOutputRetryPromptForTesting",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, toolCalls, toolOutputs });
        return Assert.IsType<string>(value);
    }

    private static (int[] canonical, int dedupedCount) InvokeBuildReadOnlyCallCanonicalIndices(
        IReadOnlyList<ToolCall> calls,
        ISet<int> nonReusableIndices) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "BuildReadOnlyCallCanonicalIndices",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var args = new object?[] { calls, nonReusableIndices, 0 };
        var value = method!.Invoke(null, args);
        var canonical = Assert.IsType<int[]>(value);
        var dedupedCount = Assert.IsType<int>(args[2]);
        return (canonical, dedupedCount);
    }

    private static (bool matched, string cacheKey) InvokeTryGetSessionToolOutputCacheKey(ToolCall call) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "TryGetSessionToolOutputCacheKey",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var args = new object?[] { call, null };
        var value = method!.Invoke(null, args);
        var matched = value is bool b && b;
        var cacheKey = args[1] as string ?? string.Empty;
        return (matched, cacheKey);
    }

    private static bool InvokeShouldCacheSessionToolOutput(string output) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldCacheSessionToolOutput",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { output });
        return value is bool b && b;
    }

    private static ToolCall InvokeApplyKnownHostTargetFallbacks(
        ToolCall call,
        ToolDefinition definition,
        IReadOnlyList<string> knownHostTargets) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ApplyKnownHostTargetFallbacks",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { call, definition, knownHostTargets });
        return Assert.IsType<ToolCall>(value);
    }

    private static IReadOnlyList<ToolCall> InvokeApplyScenarioDistinctHostCoverageFallbacks(
        string userRequest,
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<string> knownHostTargets) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ApplyScenarioDistinctHostCoverageFallbacks",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, calls, toolDefinitions, knownHostTargets });
        return Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(value);
    }

    private static ToolCall BuildToolCall(string callId, string name, string jsonArgs) {
        var args = JsonLite.Parse(jsonArgs)?.AsObject();
        var raw = new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("call_id", callId)
            .Add("name", name)
            .Add("input", jsonArgs);
        return new ToolCall(callId, name, jsonArgs, args, raw);
    }
}

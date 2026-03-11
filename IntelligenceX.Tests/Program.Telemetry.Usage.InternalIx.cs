using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Telemetry;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestIntelligenceXClientEmitsTurnCompletedTelemetry() {
        var turn = CreateTelemetryTurn(
            "turn_ix_1",
            "resp_ix_1",
            inputTokens: 120,
            cachedInputTokens: 32,
            outputTokens: 48,
            reasoningTokens: 11,
            totalTokens: 168);
        ChatOptions? capturedOptions = null;
        using var client = CreateToolRunnerClient(new[] { turn }, onStartTurn: (_, options) => capturedOptions = options);

        IntelligenceXTurnCompletedEventArgs? captured = null;
        client.TurnCompleted += (_, args) => captured = args;

        var result = client.ChatAsync(
            ChatInput.FromText("summarize telemetry"),
            new ChatOptions {
                Workspace = @"C:\repo",
                TelemetryFeature = "reviewer",
                TelemetrySurface = "setup-web"
            }).GetAwaiter().GetResult();

        AssertEqual("turn_ix_1", result.Id, "returned turn id");
        AssertNotNull(capturedOptions, "captured chat options");
        AssertEqual("reviewer", capturedOptions!.TelemetryFeature, "forwarded telemetry feature");
        AssertEqual("setup-web", capturedOptions.TelemetrySurface, "forwarded telemetry surface");
        AssertNotNull(captured, "turn completed event args");
        AssertEqual(true, captured!.Success, "turn completed success");
        AssertEqual("thread1", captured.ThreadId, "turn completed thread id");
        AssertEqual("gpt-5.4", captured.Model, "turn completed model");
        AssertEqual(@"C:\repo", captured.WorkingDirectory, "turn completed working directory");
        AssertEqual(@"C:\repo", captured.Workspace, "turn completed workspace");
        AssertEqual("reviewer", captured.Feature, "turn completed feature");
        AssertEqual("setup-web", captured.Surface, "turn completed surface");
        AssertEqual("resp_ix_1", captured.Turn!.ResponseId, "turn completed response id");
        AssertEqual(168L, captured.Turn.Usage!.TotalTokens, "turn completed total tokens");
        AssertEqual(true, captured.CompletedAtUtc >= captured.StartedAtUtc, "turn completed timestamps");
        AssertEqual(true, captured.Duration >= TimeSpan.Zero, "turn completed duration");
    }

    private static void TestEasySessionForwardsTelemetryLabels() {
        var turn = CreateTelemetryTurn(
            "turn_easy_1",
            "resp_easy_1",
            inputTokens: 42,
            cachedInputTokens: 5,
            outputTokens: 19,
            reasoningTokens: 2,
            totalTokens: 61);
        ChatOptions? capturedOptions = null;
        using var client = CreateToolRunnerClient(new[] { turn }, onStartTurn: (_, options) => capturedOptions = options);
        using var session = CreateTestEasySession(client, new EasySessionOptions { Login = EasyLoginMode.None });

        _ = session.ChatAsync(
            ChatInput.FromText("hello"),
            new EasyChatOptions {
                TelemetryFeature = "ix-chat",
                TelemetrySurface = "desktop-app",
                Workspace = @"C:\workspace"
            }).GetAwaiter().GetResult();

        AssertNotNull(capturedOptions, "easy session forwarded options");
        AssertEqual("ix-chat", capturedOptions!.TelemetryFeature, "easy session telemetry feature");
        AssertEqual("desktop-app", capturedOptions.TelemetrySurface, "easy session telemetry surface");
        AssertEqual(@"C:\workspace", capturedOptions.Workspace, "easy session workspace");
    }

    private static void TestInternalIxUsageRecorderWritesSuccessfulTurnsToLedger() {
        var turn = CreateTelemetryTurn(
            "turn_ix_ledger_1",
            "resp_ix_ledger_1",
            inputTokens: 512,
            cachedInputTokens: 64,
            outputTokens: 144,
            reasoningTokens: 18,
            totalTokens: 656);
        var rootStore = new InMemorySourceRootStore();
        var eventStore = new InMemoryUsageEventStore();
        using var client = CreateToolRunnerClient(turn);
        using var recorder = new InternalIxUsageRecorder(
            client,
            rootStore,
            eventStore,
            machineId: "devbox",
            accountLabel: "workspace-a");

        _ = client.ChatAsync(
            ChatInput.FromText("run review"),
            new ChatOptions {
                TelemetryFeature = "reviewer",
                TelemetrySurface = "cli"
            }).GetAwaiter().GetResult();

        var roots = rootStore.GetAll();
        AssertEqual(1, roots.Count, "internal ix root count");
        AssertEqual("ix", roots[0].ProviderId, "internal ix root provider");
        AssertEqual(UsageSourceKind.InternalIx, roots[0].SourceKind, "internal ix source kind");
        AssertEqual("devbox", roots[0].MachineLabel, "internal ix machine label");

        var events = eventStore.GetAll();
        AssertEqual(1, events.Count, "internal ix event count");
        AssertEqual("ix", events[0].ProviderId, "internal ix event provider");
        AssertEqual("ix.client-turn", events[0].AdapterId, "internal ix adapter id");
        AssertEqual("workspace-a", events[0].AccountLabel, "internal ix account label");
        AssertEqual("devbox", events[0].MachineId, "internal ix machine id");
        AssertEqual("thread1", events[0].SessionId, "internal ix session id");
        AssertEqual("thread1", events[0].ThreadId, "internal ix thread id");
        AssertEqual("turn_ix_ledger_1", events[0].TurnId, "internal ix turn id");
        AssertEqual("resp_ix_ledger_1", events[0].ResponseId, "internal ix response id");
        AssertEqual("gpt-5.4", events[0].Model, "internal ix model");
        AssertEqual("reviewer", events[0].Surface, "internal ix surface favors feature");
        AssertEqual(512L, events[0].InputTokens, "internal ix input tokens");
        AssertEqual(64L, events[0].CachedInputTokens, "internal ix cached tokens");
        AssertEqual(144L, events[0].OutputTokens, "internal ix output tokens");
        AssertEqual(18L, events[0].ReasoningTokens, "internal ix reasoning tokens");
        AssertEqual(656L, events[0].TotalTokens, "internal ix total tokens");
        AssertEqual(UsageTruthLevel.Exact, events[0].TruthLevel, "internal ix truth level");
        AssertEqual(true, events[0].DurationMs >= 0, "internal ix duration");
        AssertEqual(true, !string.IsNullOrWhiteSpace(events[0].RawHash), "internal ix raw hash");
    }

    private static TurnInfo CreateTelemetryTurn(
        string turnId,
        string responseId,
        long inputTokens,
        long cachedInputTokens,
        long outputTokens,
        long reasoningTokens,
        long totalTokens) {
        return TurnInfo.FromJson(new JsonObject()
            .Add("id", turnId)
            .Add("response_id", responseId)
            .Add("status", "completed")
            .Add("usage", new JsonObject()
                .Add("input_tokens", inputTokens)
                .Add("cached_input_tokens", cachedInputTokens)
                .Add("output_tokens", outputTokens)
                .Add("reasoning_tokens", reasoningTokens)
                .Add("total_tokens", totalTokens))
            .Add("output", new JsonArray()));
    }
}

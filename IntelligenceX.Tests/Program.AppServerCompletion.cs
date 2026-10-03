namespace IntelligenceX.Tests;

internal static partial class Program {
    private static readonly TimeSpan AppServerFixtureGuard = TimeSpan.FromSeconds(15);
    private static int RunAppServerCompletionTests() {
        var failed = 0;
        failed += Run("App-server v2 items retain assistant output", TestAppServerTurnItems);
        failed += Run("App-server chat awaits final result and usage", TestAppServerChatCompletion);
        failed += Run("App-server completion before start receipt is retained", TestAppServerEarlyCompletion);
        failed += Run("App-server canceled wait does not affect the next turn", TestAppServerCanceledCompletion);
        failed += Run("App-server cancellation bounds an unacknowledged interrupt", TestAppServerUnacknowledgedInterrupt);
        failed += Run("App-server observational callbacks do not terminate chat", TestAppServerObservers);
        failed += Run("App-server failed turn fails chat", TestAppServerFailedCompletion);
        failed += Run("App-server interrupted turn preserves status", TestAppServerInterruptedCompletion);
        failed += Run("App-server EOF fails pending start", TestAppServerExitBeforeReceipt);
        failed += Run("App-server EOF fails pending completion", TestAppServerExitAfterReceipt);
        failed += Run("App-server disposal releases pending completion", TestAppServerDisposedCompletion);
        failed += Run("Low-level app-server start returns the receipt", TestAppServerLowLevelStart);
        failed += Run("App-server EOF releases login waiter", TestAppServerLoginEof);
        failed += Run("App-server disposal releases login waiter", TestAppServerLoginDisposal);
        failed += Run("App-server login notification completes matching waiter", TestAppServerLoginCompletion);
        failed += Run("App-server failed login does not report success", TestAppServerLoginFailure);
#if INTELLIGENCEX_REVIEWER
        failed += Run("App-server reviewer uses the final answer", TestAppServerFinalReview);
        failed += Run("App-server reviewer rejects interrupted output", TestAppServerInterruptedReview);
        failed += Run("App-server reviewer bounds the completion wait", TestAppServerReviewDeadline);
        failed += Run("App-server reviewer does not promote progress to final output", TestAppServerReviewProgressOnly);
#endif
        return failed;
    }

    private static AppServerOptions AppServerFixtureOptions(string scenario) {
        var assembly = typeof(Program).Assembly.Location;
#if NET472
        var executable = assembly;
        var arguments = "--app-server-fixture " + scenario;
#else
        var executable = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(executable)) executable = Process.GetCurrentProcess().MainModule!.FileName;
        var arguments = "\"" + assembly + "\" --app-server-fixture " + scenario;
        if (!string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase)) {
            executable = Path.ChangeExtension(assembly, OperatingSystem.IsWindows() ? ".exe" : null);
            arguments = "--app-server-fixture " + scenario;
        }
#endif
        return new AppServerOptions {
            ExecutablePath = executable!, Arguments = arguments, ConnectRetryCount = 0,
            ShutdownTimeout = TimeSpan.FromMilliseconds(100)
        };
    }

    private static async Task<IntelligenceXClient> ConnectAppServerFixtureAsync(string scenario) {
        var options = new IntelligenceXClientOptions { TransportKind = OpenAITransportKind.AppServer };
        var fixture = AppServerFixtureOptions(scenario);
        options.AppServerOptions.ExecutablePath = fixture.ExecutablePath;
        options.AppServerOptions.Arguments = fixture.Arguments;
        options.AppServerOptions.ConnectRetryCount = 0;
        options.AppServerOptions.ShutdownTimeout = fixture.ShutdownTimeout;
        return await IntelligenceXClient.ConnectAsync(options).ConfigureAwait(false);
    }

    private static TaskCompletionSource<bool> FixtureReady(IntelligenceXClient client) {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.RawClient.NotificationReceived += (_, args) => {
            if (args.Method == "fixture/ready") ready.TrySetResult(true);
        };
        return ready;
    }

    private static async Task AwaitFixtureAsync(Task task) {
        if (await Task.WhenAny(task, Task.Delay(AppServerFixtureGuard)).ConfigureAwait(false) != task)
            throw new TimeoutException("App-server fixture did not reach the expected stage.");
        await task.ConfigureAwait(false);
    }

    private static void TestAppServerTurnItems() {
        var item = new JsonObject().Add("id", "answer").Add("type", "agentMessage").Add("text", "Final answer");
        var turn = TurnInfo.FromJson(FixtureTurn("turn-1", "completed").Add("items", new JsonArray().Add(item)));
        AssertEqual(1, turn.Outputs.Count, "app-server item count");
        AssertEqual(true, turn.Outputs[0].IsText, "assistant item is text output");
        AssertEqual("Final answer", turn.Outputs[0].Text, "assistant item text");
        AssertEqual("agentMessage", turn.Outputs[0].Raw.GetString("type"), "raw protocol type retained");
    }

    private static void TestAppServerChatCompletion() => RunAppServerChatCompletionAsync("completed").GetAwaiter().GetResult();

    private static void TestAppServerEarlyCompletion() => RunAppServerChatCompletionAsync("early-completion").GetAwaiter().GetResult();

    private static async Task RunAppServerChatCompletionAsync(string scenario) {
        using var client = await ConnectAppServerFixtureAsync(scenario).ConfigureAwait(false);
        var ready = FixtureReady(client);
        var deltas = new StringBuilder();
        client.DeltaReceived += (_, delta) => { lock (deltas) deltas.Append(delta); };
        var chat = client.ChatAsync("hello");
        await AwaitFixtureAsync(ready.Task).ConfigureAwait(false);
        if (scenario != "early-completion") {
            await AwaitFixtureAsync(client.HealthCheckAsync()).ConfigureAwait(false);
        }
        await AwaitFixtureAsync(chat).ConfigureAwait(false);
        var turn = await chat.ConfigureAwait(false);
        AssertEqual("completed", turn.Status, "chat returns a completed turn");
        AssertEqual("turn-1", turn.Id, "matching turn retained");
        AssertEqual(1, turn.Outputs.Count, "streamed item retained when terminal items are empty");
        AssertEqual("Final answer", turn.Outputs[0].Text, "final assistant answer");
        if (scenario == "completed") {
            lock (deltas) AssertEqual("Final answer", deltas.ToString(), "assistant deltas only");
            AssertEqual(12L, turn.Usage?.InputTokens, "per-turn input usage");
            AssertEqual(19L, turn.Usage?.TotalTokens, "per-turn usage instead of cumulative usage");
            AssertEqual(3L, turn.Usage?.CachedInputTokens, "cached token usage");
            AssertEqual(2L, turn.Usage?.ReasoningTokens, "reasoning token usage");
        }
    }

    private static void TestAppServerCanceledCompletion() => TestAppServerCanceledCompletionAsync().GetAwaiter().GetResult();

    private static async Task TestAppServerCanceledCompletionAsync() {
        using var client = await ConnectAppServerFixtureAsync("completed").ConfigureAwait(false);
        var ready = FixtureReady(client);
        using var cancellation = new CancellationTokenSource();
        var chat = client.ChatAsync("hello", cancellationToken: cancellation.Token);
        await AwaitFixtureAsync(ready.Task).ConfigureAwait(false);
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "completion wait canceled");
        var next = client.ChatAsync("next");
        await AwaitFixtureAsync(next).ConfigureAwait(false);
        AssertEqual("turn-2", next.Result.Id, "next turn does not consume old completion");
        AssertEqual("completed", next.Result.Status, "next turn completed");
    }

    private static void TestAppServerUnacknowledgedInterrupt() {
        using var client = ConnectAppServerFixtureAsync("unacknowledged-interrupt").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        using var cancellation = new CancellationTokenSource();
        var chat = client.ChatAsync("hello", cancellationToken: cancellation.Token);
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "interrupt acknowledgement is bounded");
    }

    private static void TestAppServerObservers() {
        using var client = ConnectAppServerFixtureAsync("completed").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        client.RawClient.ProtocolLineReceived += (_, _) => throw new InvalidOperationException("protocol observer");
        client.RawClient.NotificationReceived += (_, _) => throw new InvalidOperationException("notification observer");
        client.RawClient.RpcCallStarted += (_, _) => throw new InvalidOperationException("RPC start observer");
        client.RawClient.RpcCallCompleted += (_, _) => throw new InvalidOperationException("RPC completion observer");
        var chat = client.ChatAsync("hello");
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        AwaitFixtureAsync(client.HealthCheckAsync()).GetAwaiter().GetResult();
        AwaitFixtureAsync(chat).GetAwaiter().GetResult();
        AssertEqual("completed", chat.Result.Status, "internal completion is delivered despite external observer errors");
    }

    private static void TestAppServerFailedCompletion() => TestAppServerFailedCompletionAsync().GetAwaiter().GetResult();

    private static async Task TestAppServerFailedCompletionAsync() {
        using var client = await ConnectAppServerFixtureAsync("failed").ConfigureAwait(false);
        var ready = FixtureReady(client);
        IntelligenceXTurnCompletedEventArgs? completion = null;
        client.TurnCompleted += (_, args) => completion = args;
        var chat = client.ChatAsync("hello");
        await AwaitFixtureAsync(ready.Task).ConfigureAwait(false);
        await AwaitFixtureAsync(client.HealthCheckAsync()).ConfigureAwait(false);
        AssertThrows<InvalidOperationException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "failed turn fails chat");
        AssertEqual(false, completion?.Success, "failed completion telemetry");
        AssertEqual("permission denied", completion?.Error?.Message, "final provider error retained");
    }

    private static void TestAppServerInterruptedCompletion() {
        using var client = ConnectAppServerFixtureAsync("interrupted").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        var chat = client.ChatAsync("hello");
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        AwaitFixtureAsync(client.HealthCheckAsync()).GetAwaiter().GetResult();
        AwaitFixtureAsync(chat).GetAwaiter().GetResult();
        AssertEqual("interrupted", chat.Result.Status, "remote interruption status");
    }

    private static void TestAppServerExitBeforeReceipt() {
        using var client = ConnectAppServerFixtureAsync("exit-before-receipt").GetAwaiter().GetResult();
        var chat = client.ChatAsync("hello");
        AssertThrows<IOException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "EOF fails pending RPC");
    }

    private static void TestAppServerExitAfterReceipt() {
        using var client = ConnectAppServerFixtureAsync("exit-after-receipt").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        var chat = client.ChatAsync("hello");
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        AwaitFixtureAsync(client.HealthCheckAsync()).GetAwaiter().GetResult();
        AssertThrows<IOException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "EOF fails pending turn");
    }

    private static void TestAppServerDisposedCompletion() {
        using var client = ConnectAppServerFixtureAsync("completed").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        var chat = client.ChatAsync("hello");
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        client.Dispose();
        AssertThrows<ObjectDisposedException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "disposal fails pending turn");
    }

    private static void TestAppServerLowLevelStart() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("completed")).GetAwaiter().GetResult();
        client.InitializeAsync(new ClientInfo("fixture", "Fixture", "1")).GetAwaiter().GetResult();
        var start = client.StartTurnAsync("thread-fixture", "hello");
        AwaitFixtureAsync(start).GetAwaiter().GetResult();
        AssertEqual("inProgress", start.Result.Status, "low-level start remains a start receipt");
    }

    private static void TestAppServerLoginEof() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("exit-after-receipt")).GetAwaiter().GetResult();
        client.InitializeAsync(new ClientInfo("fixture", "Fixture", "1")).GetAwaiter().GetResult();
        var login = client.WaitForLoginCompletionAsync("login-fixture");
        AwaitFixtureAsync(client.HealthCheckAsync()).GetAwaiter().GetResult();
        AssertThrows<IOException>(() => AwaitFixtureAsync(login).GetAwaiter().GetResult(), "EOF fails login waiter");
    }

    private static void TestAppServerLoginDisposal() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("login")).GetAwaiter().GetResult();
        var login = client.WaitForLoginCompletionAsync("login-fixture");
        client.Dispose();
        AssertThrows<ObjectDisposedException>(() => AwaitFixtureAsync(login).GetAwaiter().GetResult(), "disposal fails login waiter");
    }

    private static void TestAppServerLoginCompletion() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("login")).GetAwaiter().GetResult();
        client.InitializeAsync(new ClientInfo("fixture", "Fixture", "1")).GetAwaiter().GetResult();
        var login = client.WaitForLoginCompletionAsync("login-fixture");
        AwaitFixtureAsync(client.HealthCheckAsync()).GetAwaiter().GetResult();
        AwaitFixtureAsync(login).GetAwaiter().GetResult();
    }

    private static void TestAppServerLoginFailure() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("login-failed")).GetAwaiter().GetResult();
        client.InitializeAsync(new ClientInfo("fixture", "Fixture", "1")).GetAwaiter().GetResult();
        var successful = false;
        client.LoginCompleted += (_, _) => successful = true;
        var login = client.WaitForLoginCompletionAsync("login-fixture");
        AwaitFixtureAsync(client.HealthCheckAsync()).GetAwaiter().GetResult();
        AssertThrows<InvalidOperationException>(() => AwaitFixtureAsync(login).GetAwaiter().GetResult(), "explicit login failure is propagated");
        AssertEqual(false, successful, "failed login does not raise successful completion");
    }
}

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static readonly TimeSpan AppServerFixtureGuard = TimeSpan.FromSeconds(15);
    private static int RunAppServerCompletionTests() {
        var failed = 0;
        failed += Run("App-server v2 items retain assistant output", TestAppServerTurnItems);
        failed += Run("App-server chat awaits final result and usage", TestAppServerChatCompletion);
        failed += Run("App-server completion before start receipt is retained", TestAppServerEarlyCompletion);
        failed += Run("App-server canceled wait does not affect the next turn", TestAppServerCanceledCompletion);
        failed += Run("App-server cancellation before the receipt interrupts the accepted turn", TestAppServerCanceledStart);
        failed += Run("App-server cancellation without a receipt closes the connection", TestAppServerCanceledMissingReceipt);
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
        failed += Run("App-server login completion before the receipt is retained", TestAppServerEarlyLogin);
        failed += Run("App-server login completion inside the URL callback is retained", TestAppServerLoginDuringCallback);
        failed += Run("App-server late login waiter retains failed outcomes", TestAppServerLateLoginFailure);
        failed += Run("App-server new login does not reuse an earlier outcome", TestAppServerNextLogin);
        failed += Run("App-server high-level login bounds authorization and start waits", TestAppServerLoginDeadline);
        failed += Run("App-server login preserves caller cancellation", TestAppServerLoginCancellation);
#if INTELLIGENCEX_REVIEWER
        failed += Run("App-server reviewer uses the final answer", TestAppServerFinalReview);
        failed += Run("App-server reviewer rejects interrupted output", TestAppServerInterruptedReview);
        failed += Run("App-server reviewer bounds the completion wait", TestAppServerReviewDeadline);
        failed += Run("App-server reviewer does not promote progress to final output", TestAppServerReviewProgressOnly);
        failed += Run("App-server reviewer rejects commentary-only terminal output", TestAppServerReviewCommentaryOnly);
        failed += Run("App-server reviewer accepts unphased terminal text", TestAppServerReviewUnphased);
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

    private static void TestAppServerCanceledStart() {
        using var client = ConnectAppServerFixtureAsync("delayed-receipt").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        using var cancellation = new CancellationTokenSource();
        var chat = client.ChatAsync("hello", cancellationToken: cancellation.Token);
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        cancellation.Cancel();
        AwaitFixtureAsync(client.RawClient.CallAsync("fixture/release-start", null)).GetAwaiter().GetResult();
        AssertThrows<OperationCanceledException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "start wait canceled");
        var next = client.ChatAsync("next");
        AwaitFixtureAsync(next).GetAwaiter().GetResult();
        AssertEqual("completed", next.Result.Status, "accepted canceled turn was interrupted before the next turn");
    }

    private static void TestAppServerCanceledMissingReceipt() {
        using var client = ConnectAppServerFixtureAsync("missing-receipt").GetAwaiter().GetResult();
        var ready = FixtureReady(client);
        using var cancellation = new CancellationTokenSource();
        var chat = client.ChatAsync("hello", cancellationToken: cancellation.Token);
        AwaitFixtureAsync(ready.Task).GetAwaiter().GetResult();
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => AwaitFixtureAsync(chat).GetAwaiter().GetResult(), "missing receipt cancellation is bounded");
        AssertThrows<ObjectDisposedException>(() => client.RawClient.CallAsync("fixture/status", null).GetAwaiter().GetResult(),
            "connection is closed when the accepted turn cannot be identified");
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

    private static void TestAppServerEarlyLogin() {
        using var client = ConnectAppServerFixtureAsync("login-early").GetAwaiter().GetResult();
        var login = client.LoginChatGptAsync();
        AwaitFixtureAsync(login).GetAwaiter().GetResult();
        // The documented start-then-wait flow must also work after the high-level login has already waited.
        AwaitFixtureAsync(client.RawClient.WaitForLoginCompletionAsync(login.Result.LoginId)).GetAwaiter().GetResult();
        AwaitFixtureAsync(client.RawClient.WaitForLoginCompletionAsync()).GetAwaiter().GetResult();
    }

    private static void TestAppServerLoginDuringCallback() {
        using var client = ConnectAppServerFixtureAsync("login-pending").GetAwaiter().GetResult();
        var login = client.LoginChatGptAsync(_ => client.RawClient.CallAsync("fixture/complete-login", null).GetAwaiter().GetResult(), null);
        AwaitFixtureAsync(login).GetAwaiter().GetResult();
    }

    private static void TestAppServerLateLoginFailure() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("login-early-failed")).GetAwaiter().GetResult();
        var successful = false;
        client.LoginCompleted += (_, _) => successful = true;
        var started = client.StartChatGptLoginAsync().GetAwaiter().GetResult();
        AssertThrows<InvalidOperationException>(() => AwaitFixtureAsync(client.WaitForLoginCompletionAsync(started.LoginId)).GetAwaiter().GetResult(),
            "late waiter observes the failed outcome");
        AssertEqual(false, successful, "replayed failure does not report success");
    }

    private static void TestAppServerNextLogin() {
        using var client = AppServerClient.StartAsync(AppServerFixtureOptions("login-next-pending")).GetAwaiter().GetResult();
        var first = client.StartChatGptLoginAsync().GetAwaiter().GetResult();
        client.StartChatGptLoginAsync().GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        var current = client.WaitForLoginCompletionAsync(cancellationToken: cancellation.Token);
        AssertEqual(false, current.IsCompleted, "generic waiter does not reuse a previous login outcome");
        cancellation.Cancel();
        AssertThrows<OperationCanceledException>(() => AwaitFixtureAsync(current).GetAwaiter().GetResult(), "current login canceled");
        AwaitFixtureAsync(client.WaitForLoginCompletionAsync(first.LoginId)).GetAwaiter().GetResult();
    }

    private static void TestAppServerLoginDeadline() {
        foreach (var scenario in new[] { "login-pending", "login-missing-receipt" }) {
            using var client = ConnectAppServerFixtureAsync(scenario).GetAwaiter().GetResult();
            var login = client.LoginChatGptAndWaitAsync(timeout: TimeSpan.FromSeconds(1));
            AssertEqual(true, Task.WhenAny(login, Task.Delay(AppServerFixtureGuard)).GetAwaiter().GetResult() == login,
                "configured login deadline settles before the fixture guard: " + scenario);
            AssertThrows<TimeoutException>(() => login.GetAwaiter().GetResult(), "configured login deadline: " + scenario);
        }
    }

    private static void TestAppServerLoginCancellation() {
        using var client = ConnectAppServerFixtureAsync("login-pending").GetAwaiter().GetResult();
        using var cancellation = new CancellationTokenSource();
        var login = client.LoginChatGptAndWaitAsync(_ => cancellation.Cancel(), timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cancellation.Token);
        AssertThrows<OperationCanceledException>(() => AwaitFixtureAsync(login).GetAwaiter().GetResult(), "caller cancellation remains cancellation");
    }
}

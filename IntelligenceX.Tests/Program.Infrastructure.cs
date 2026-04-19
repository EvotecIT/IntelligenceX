namespace IntelligenceX.Tests;

internal static partial class Program {
    private static IntelligenceXClient CreateTestClient(IOpenAITransport transport) {
        var ctor = typeof(IntelligenceXClient).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(IOpenAITransport), typeof(string), typeof(string), typeof(string), typeof(SandboxPolicy) },
            null);
        if (ctor is null) {
            throw new InvalidOperationException("IntelligenceXClient constructor not found.");
        }
        return (IntelligenceXClient)ctor.Invoke(new object?[] { transport, "gpt-5.4", null, null, null });
    }

    private static IntelligenceXClient CreateToolRunnerClient(TurnInfo turn, OpenAITransportKind transportKind = OpenAITransportKind.Native) {
        var transport = new FakeToolTransport(new[] { turn }, onStartTurn: null, transportKind);
        return CreateTestClient(transport);
    }

    private static IntelligenceXClient CreateToolRunnerClient(
        IReadOnlyList<TurnInfo> turns,
        Action<ChatInput, ChatOptions?>? onStartTurn = null,
        OpenAITransportKind transportKind = OpenAITransportKind.Native) {
        var transport = new FakeToolTransport(turns, onStartTurn, transportKind);
        return CreateTestClient(transport);
    }

    private static EasySession CreateTestEasySession(IntelligenceXClient client, EasySessionOptions? options = null) {
        var ctor = typeof(EasySession).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(IntelligenceXClient), typeof(EasySessionOptions) },
            null);
        if (ctor is null) {
            throw new InvalidOperationException("EasySession constructor not found.");
        }
        return (EasySession)ctor.Invoke(new object?[] {
            client,
            options ?? new EasySessionOptions { Login = EasyLoginMode.None }
        });
    }

    private static TurnInfo BuildToolCallTurn(params (string CallId, string ToolName)[] calls) {
        if (calls is null || calls.Length == 0) {
            throw new InvalidOperationException("Tool call list cannot be empty.");
        }
        var output = new JsonArray();
        foreach (var call in calls) {
            output.Add(new JsonObject()
                .Add("type", "custom_tool_call")
                .Add("call_id", call.CallId)
                .Add("name", call.ToolName)
                .Add("input", "{}"));
        }
        return TurnInfo.FromJson(new JsonObject()
            .Add("id", "turn_" + calls[0].CallId)
            .Add("output", output));
    }

    private sealed class StubTool : ITool {
        private readonly ToolDefinition _definition;

        public StubTool(string name) {
            _definition = new ToolDefinition(
                name,
                "Stub tool",
                new JsonObject().Add("type", "object"),
                routing: CreateTestRoutingContract());
        }

        public ToolDefinition Definition => _definition;

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken)
            => Task.FromResult("ok");
    }

    private sealed class GateTool : ITool {
        private readonly ToolDefinition _definition;
        private readonly TaskCompletionSource<bool> _startGate;
        private readonly TaskCompletionSource<bool> _releaseGate;
        private readonly Func<int> _increment;
        private readonly int _expected;

        public GateTool(string name, TaskCompletionSource<bool> startGate, TaskCompletionSource<bool> releaseGate,
            Func<int> increment, int expected) {
            _definition = new ToolDefinition(
                name,
                "Gate tool",
                new JsonObject().Add("type", "object"),
                routing: CreateTestRoutingContract());
            _startGate = startGate;
            _releaseGate = releaseGate;
            _increment = increment;
            _expected = expected;
        }

        public ToolDefinition Definition => _definition;

        public async Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            var count = _increment();
            if (count >= _expected) {
                _startGate.TrySetResult(true);
            }
            await _startGate.Task.ConfigureAwait(false);
            await _releaseGate.Task.ConfigureAwait(false);
            return "ok";
        }
    }

    private sealed class FakeToolTransport : IOpenAITransport {
        private readonly Queue<TurnInfo> _turns;
        private readonly Action<ChatInput, ChatOptions?>? _onStartTurn;
        private readonly OpenAITransportKind _kind;

        public FakeToolTransport(
            IReadOnlyList<TurnInfo> turns,
            Action<ChatInput, ChatOptions?>? onStartTurn,
            OpenAITransportKind kind) {
            if (turns is null || turns.Count == 0) {
                throw new InvalidOperationException("At least one turn is required.");
            }

            _turns = new Queue<TurnInfo>(turns);
            _onStartTurn = onStartTurn;
            _kind = kind;
        }

        public OpenAITransportKind Kind => _kind;
        public AppServerClient? RawAppServerClient => null;

#pragma warning disable CS0067
        public event EventHandler<string>? DeltaReceived;
        public event EventHandler<LoginEventArgs>? LoginStarted;
        public event EventHandler<LoginEventArgs>? LoginCompleted;
        public event EventHandler<string>? ProtocolLineReceived;
        public event EventHandler<string>? StandardErrorReceived;
        public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
        public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;
#pragma warning restore CS0067

        public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken)
            => Task.FromResult(new HealthCheckResult(true));
        public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AccountInfo(null, null, null, new JsonObject(), null));
        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ModelListResult(Array.Empty<ModelInfo>(), null, new JsonObject(), null));
        public Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
            bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken)
            => Task.FromResult(new ChatGptLoginStart("login", "https://example", new JsonObject(), null));
        public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
            string? sandbox, CancellationToken cancellationToken)
            => Task.FromResult(new ThreadInfo("thread1", null, null, null, null, new JsonObject(), null));
        public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
            => Task.FromResult(new ThreadInfo(threadId, null, null, null, null, new JsonObject(), null));
        public Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
            string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken)
            => Task.FromResult(DequeueTurn(input, options));

        private TurnInfo DequeueTurn(ChatInput input, ChatOptions? options) {
            _onStartTurn?.Invoke(input, options);
            if (_turns.Count == 0) {
                throw new InvalidOperationException("No queued turns remaining for fake tool transport.");
            }

            return _turns.Dequeue();
        }

        public void Dispose() { }
    }

    private static List<string> ParseUsageSummaryParts(string line) {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(line)) {
            return result;
        }
        const string prefix = "Usage: ";
        const string separator = " | ";
        var body = line.StartsWith(prefix, StringComparison.Ordinal)
            ? line.Substring(prefix.Length)
            : line;
        foreach (var part in body.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)) {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed)) {
                result.Add(trimmed);
            }
        }
        return result;
    }

    private static bool ContainsUsageSummaryPart(IReadOnlyList<string> parts, string expected) {
        foreach (var part in parts) {
            if (string.Equals(part, expected, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    private static string LoadReviewerFixture(string filename) {
        var path = GetReviewerFixturePath(filename);
        if (!File.Exists(path)) {
            throw new InvalidOperationException($"Fixture not found: {path}");
        }
        return File.ReadAllText(path);
    }

    private static void MaybeUpdateReviewerFixture(string filename, string content) {
        var update = Environment.GetEnvironmentVariable("INTELLIGENCEX_UPDATE_GOLDEN");
        if (!string.Equals(update, "1", StringComparison.Ordinal)) {
            return;
        }
        var normalized = string.IsNullOrEmpty(content)
            ? string.Empty
            : content.Replace("\r\n", "\n").Replace('\r', '\n');
        File.WriteAllText(GetReviewerFixturePath(filename), normalized.Trim() + "\n");
    }

    private static string GetReviewerFixturePath(string filename) {
        if (string.IsNullOrWhiteSpace(filename)) {
            throw new ArgumentException("Fixture filename cannot be empty.", nameof(filename));
        }
        if (Path.IsPathRooted(filename)) {
            throw new ArgumentException("Fixture filename must be a relative path.", nameof(filename));
        }
        return Path.Combine("InternalDocs", "ReviewerFixtures", filename);
    }

    private static void AssertEqual<T>(T expected, T? actual, string name) {
        if (!Equals(expected, actual)) {
            throw new InvalidOperationException($"Expected {name} to be '{expected}', got '{actual}'.");
        }
    }

    private static void AssertSequenceEqual(IReadOnlyList<string> expected, IReadOnlyList<string> actual, string name) {
        if (expected.Count != actual.Count) {
            throw new InvalidOperationException($"Expected {name} length {expected.Count}, got {actual.Count}.");
        }
        for (var i = 0; i < expected.Count; i++) {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal)) {
                throw new InvalidOperationException($"Expected {name}[{i}] to be '{expected[i]}', got '{actual[i]}'.");
            }
        }
    }


    private static void AssertContains(IReadOnlyList<string> values, string expected, string name) {
        foreach (var value in values) {
            if (string.Equals(value, expected, StringComparison.OrdinalIgnoreCase)) {
                return;
            }
        }
        throw new InvalidOperationException($"Expected {name} to contain '{expected}'.");
    }

    private static void AssertContainsText(string value, string expected, string name) {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOf(expected, StringComparison.Ordinal) < 0) {
            throw new InvalidOperationException($"Expected {name} to contain '{expected}'.");
        }
    }

    private static void AssertDoesNotContainText(string value, string unexpected, string name) {
        if (!string.IsNullOrEmpty(value) && value.IndexOf(unexpected, StringComparison.Ordinal) >= 0) {
            throw new InvalidOperationException($"Expected {name} not to contain '{unexpected}'.");
        }
    }

    private static void AssertNotNull(object? value, string name) {
        if (value is null) {
            throw new InvalidOperationException($"Expected {name} to be non-null.");
        }
    }

    private static ToolRoutingContract CreateTestRoutingContract(string packId = "test-pack") {
        return new ToolRoutingContract {
            PackId = packId,
            Role = ToolRoutingTaxonomy.RoleOperational,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit
        };
    }

    private sealed class TestTool : ITool {
        public TestTool(string name) {
            Definition = new ToolDefinition(name, "test tool", routing: CreateTestRoutingContract());
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult(string.Empty);
        }
    }

    private sealed class ConfiguredTool : ITool {
        public ConfiguredTool(ToolDefinition definition) {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("ok");
        }
    }

    private static JsonArray CallChatInputToJson(ChatInput input) {
        var method = typeof(ChatInput).GetMethod("ToJson", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method is null) {
            throw new InvalidOperationException("ChatInput.ToJson method not found.");
        }
        var result = method.Invoke(input, Array.Empty<object>()) as JsonArray;
        return result ?? new JsonArray();
    }

    private static void AssertThrows<T>(Action action, string name) where T : Exception {
        try {
            action();
        } catch (T) {
            return;
        }
        throw new InvalidOperationException($"Expected {name} to throw {typeof(T).Name}.");
    }

    private static void AssertCompletes(Task task, int timeoutMs, string name) {
        if (task is null) {
            throw new InvalidOperationException($"Expected {name} task to be non-null.");
        }
        var completed = Task.WhenAny(task, Task.Delay(timeoutMs)).GetAwaiter().GetResult();
        if (!ReferenceEquals(completed, task)) {
            throw new InvalidOperationException($"Expected {name} to complete within {timeoutMs}ms.");
        }
        task.GetAwaiter().GetResult();
    }
}

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestEnsureChatGptLoginUsesCache() {
        var transport = new FakeAuthTransport {
            AccountMode = FakeAccountMode.Ok
        };
        using var client = CreateTestClient(transport);

        client.EnsureChatGptLoginAsync(cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        AssertEqual(1, transport.GetAccountCalls, "GetAccountCalls");
        AssertEqual(0, transport.LoginCalls, "LoginCalls");
    }

    private static void TestEnsureChatGptLoginTriggersLoginWhenMissing() {
        var transport = new FakeAuthTransport {
            AccountMode = FakeAccountMode.AuthMissing
        };
        using var client = CreateTestClient(transport);

        client.EnsureChatGptLoginAsync(cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        AssertEqual(1, transport.GetAccountCalls, "GetAccountCalls");
        AssertEqual(1, transport.LoginCalls, "LoginCalls");
    }

    private static void TestEnsureChatGptLoginForceTriggersLogin() {
        var transport = new FakeAuthTransport {
            AccountMode = FakeAccountMode.Ok
        };
        using var client = CreateTestClient(transport);

        client.EnsureChatGptLoginAsync(forceLogin: true, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        AssertEqual(0, transport.GetAccountCalls, "GetAccountCalls");
        AssertEqual(1, transport.LoginCalls, "LoginCalls");
    }

    private static void TestEnsureChatGptLoginCancellationPropagates() {
        var transport = new FakeAuthTransport {
            AccountMode = FakeAccountMode.Cancel
        };
        using var client = CreateTestClient(transport);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        AssertThrows<OperationCanceledException>(() => {
            client.EnsureChatGptLoginAsync(cancellationToken: cts.Token).GetAwaiter().GetResult();
        }, "EnsureChatGptLoginAsync cancellation");
    }

#if NET8_0_OR_GREATER
    private static void TestAuthStoreInvalidKeyThrows() {
        AssertThrows<InvalidOperationException>(() => {
            _ = new FileAuthBundleStore(path: Path.Combine(Path.GetTempPath(), $"ix-auth-{Guid.NewGuid():N}.json"), encryptionKeyBase64: "nope");
        }, "invalid auth key throws");
    }

    private static void TestAuthStoreEncryptedRoundtrip() {
        var path = Path.Combine(Path.GetTempPath(), $"ix-auth-{Guid.NewGuid():N}.json");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        try {
            var store = new FileAuthBundleStore(path: path, encryptionKeyBase64: key);
            var bundle = new AuthBundle("openai", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "acct"
            };
            store.SaveAsync(bundle, CancellationToken.None).GetAwaiter().GetResult();

            var content = File.ReadAllText(path);
            AssertEqual(true, content.StartsWith("{\"encrypted\":true", StringComparison.OrdinalIgnoreCase), "encrypted envelope written");

            var loaded = store.GetAsync("openai", accountId: "acct", cancellationToken: CancellationToken.None).GetAwaiter().GetResult();
            AssertNotNull(loaded, "loaded bundle");
            AssertEqual("access", loaded!.AccessToken, "access token");
            AssertEqual("refresh", loaded.RefreshToken, "refresh token");
            AssertEqual("acct", loaded.AccountId, "account id");
        } finally {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestAuthStoreDecryptWithExplicitKeyOverride() {
        var path = Path.Combine(Path.GetTempPath(), $"ix-auth-explicit-{Guid.NewGuid():N}.json");
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var previousKey = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY");
        try {
            var store = new FileAuthBundleStore(path: path, encryptionKeyBase64: key);
            var bundle = new AuthBundle("openai-codex", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "acct-explicit"
            };
            store.SaveAsync(bundle, CancellationToken.None).GetAwaiter().GetResult();

            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY", null);
            var content = File.ReadAllText(path);
            var decrypted = IntelligenceX.Cli.Auth.AuthStoreUtils.DecryptAuthStoreIfNeeded(content, key);
            var entries = IntelligenceX.Cli.Auth.AuthStoreUtils.ParseAuthStoreEntries(decrypted);
            AssertEqual(1, entries.Count, "auth store explicit key entries count");
            AssertEqual("acct-explicit", entries[0].AccountId, "auth store explicit key account id");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY", previousKey);
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestAuthStoreListAsyncFiltersProviderAndOrdersAccounts() {
        var path = Path.Combine(Path.GetTempPath(), $"ix-auth-list-{Guid.NewGuid():N}.json");
        try {
            var store = new FileAuthBundleStore(path: path);
            store.SaveAsync(new AuthBundle("openai-codex", "access-b", "refresh", DateTimeOffset.UtcNow.AddHours(2)) {
                AccountId = "acct-b"
            }, CancellationToken.None).GetAwaiter().GetResult();
            store.SaveAsync(new AuthBundle("openai-codex", "access-a", "refresh", DateTimeOffset.UtcNow.AddHours(1)) {
                AccountId = "acct-a"
            }, CancellationToken.None).GetAwaiter().GetResult();
            store.SaveAsync(new AuthBundle("openai", "access-other", "refresh", DateTimeOffset.UtcNow.AddHours(3)) {
                AccountId = "acct-other"
            }, CancellationToken.None).GetAwaiter().GetResult();

            var bundles = store.ListAsync("openai-codex", CancellationToken.None).GetAwaiter().GetResult();
            AssertEqual(2, bundles.Count, "auth store list provider count");
            AssertEqual("acct-a", bundles[0].AccountId, "auth store list sorts first account");
            AssertEqual("acct-b", bundles[1].AccountId, "auth store list sorts second account");
        } finally {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }
#endif

    private enum FakeAccountMode {
        Ok,
        AuthMissing,
        Cancel
    }

    private sealed class FakeAuthTransport : IOpenAITransport {
        public FakeAccountMode AccountMode { get; set; }
        public int GetAccountCalls { get; private set; }
        public int LoginCalls { get; private set; }

        public OpenAITransportKind Kind => OpenAITransportKind.Native;
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

        public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
            GetAccountCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return AccountMode switch {
                FakeAccountMode.Ok => Task.FromResult(new AccountInfo(null, null, null, new IntelligenceX.Json.JsonObject(), null)),
                FakeAccountMode.AuthMissing => throw new OpenAIAuthenticationRequiredException(),
                FakeAccountMode.Cancel => throw new OperationCanceledException(cancellationToken),
                _ => Task.FromResult(new AccountInfo(null, null, null, new IntelligenceX.Json.JsonObject(), null))
            };
        }

        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ModelListResult(Array.Empty<ModelInfo>(), null, new IntelligenceX.Json.JsonObject(), null));

        public Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
            bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
            LoginCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatGptLoginStart("login", "https://example", new IntelligenceX.Json.JsonObject(), null));
        }

        public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
            string? sandbox, CancellationToken cancellationToken)
            => Task.FromResult(new ThreadInfo("thread1", null, null, null, null, new IntelligenceX.Json.JsonObject(), null));
        public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken)
            => Task.FromResult(new ThreadInfo(threadId, null, null, null, null, new IntelligenceX.Json.JsonObject(), null));
        public Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
            string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken)
            => Task.FromResult(TurnInfo.FromJson(new IntelligenceX.Json.JsonObject().Add("id", "turn1").Add("output", new IntelligenceX.Json.JsonArray())));

        public void Dispose() { }
    }
}

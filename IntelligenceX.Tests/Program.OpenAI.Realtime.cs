using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Realtime;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestCodexAuthStoreReadsCurrentOAuthBundle() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-codex-auth-" + Guid.NewGuid().ToString("N"));
        var authPath = Path.Combine(tempRoot, "auth.json");
        Directory.CreateDirectory(tempRoot);
        try {
            var payload = """
                {"exp":2000000000,"https://api.openai.com/auth":{"chatgpt_account_id":"acct-current"}}
                """;
            var token = EncodeBase64Url("""{"alg":"none"}""") + "." + EncodeBase64Url(payload) + ".signature";
            File.WriteAllText(
                authPath,
                "{\"tokens\":{\"access_token\":\"" + token +
                "\",\"refresh_token\":\"refresh-current\",\"id_token\":\"id-current\"}}");

            var bundle = CodexAuthStore.TryReadBundle(authPath);

            AssertNotNull(bundle, "Codex auth bundle");
            AssertEqual(token, bundle!.AccessToken, "Codex auth access token");
            AssertEqual("refresh-current", bundle.RefreshToken, "Codex auth refresh token");
            AssertEqual("acct-current", bundle.AccountId, "Codex auth account id");
            AssertEqual(DateTimeOffset.FromUnixTimeSeconds(2000000000), bundle.ExpiresAt, "Codex auth expiry");
        } finally {
            if (Directory.Exists(tempRoot)) {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static void TestRealtimeClientSecretUsesChatGptOAuth() {
        var store = new TestRealtimeAuthStore(new AuthBundle(
            OpenAICodexDefaults.Provider,
            "oauth-access-token",
            "oauth-refresh-token",
            DateTimeOffset.UtcNow.AddHours(1)) {
            AccountId = "account-123"
        });
        var handler = new RecordingRealtimeHandler(
            """{"value":"ek_test","expires_at":2000000000,"session":{"model":"gpt-realtime-2.1"}}""");
        var chatGptOptions = new OpenAINativeOptions {
            AuthStore = store,
            PersistCodexAuthJson = false,
            LoadCodexAuthJson = false
        };
        using var httpClient = new HttpClient(handler);
        using var client = new OpenAIRealtimeClient(
            new OpenAIRealtimeOptions(),
            chatGptOptions,
            httpClient,
            ownsHttpClient: false);

        var secret = client.CreateClientSecretAsync(new OpenAIRealtimeSessionOptions {
            OutputModalities = new[] { "text" },
            ClientSecretLifetime = TimeSpan.FromSeconds(90)
        }).GetAwaiter().GetResult();

        AssertEqual("ek_test", secret.Value, "Realtime client secret value");
        AssertEqual(OpenAIModelCatalog.DefaultRealtimeModel, secret.Model, "Realtime client secret model");
        AssertEqual("Bearer", handler.AuthorizationScheme, "Realtime OAuth authorization scheme");
        AssertEqual("oauth-access-token", handler.AuthorizationParameter, "Realtime OAuth access token");
        AssertEqual("account-123", handler.AccountId, "Realtime OAuth account header");
        AssertContainsText(handler.Body ?? string.Empty, "\"seconds\":90", "Realtime client secret lifetime");
        AssertContainsText(handler.Body ?? string.Empty, "\"output_modalities\":[\"text\"]", "Realtime output modality");
    }

    private static void TestRealtimeSessionPayloadCarriesVoiceContract() {
        var options = new OpenAIRealtimeSessionOptions {
            Model = "gpt-realtime-2.1",
            Instructions = "Speak briefly.",
            OutputModalities = new[] { "audio" },
            Voice = "marin",
            ClientSecretLifetime = TimeSpan.FromSeconds(75)
        };
        options.Validate();

        var payload = OpenAIRealtimeClient.BuildClientSecretPayload(options);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        AssertEqual(75, root.GetProperty("expires_after").GetProperty("seconds").GetInt32(), "Realtime expiry seconds");
        var session = root.GetProperty("session");
        AssertEqual("realtime", session.GetProperty("type").GetString(), "Realtime session type");
        AssertEqual("gpt-realtime-2.1", session.GetProperty("model").GetString(), "Realtime model");
        AssertEqual("Speak briefly.", session.GetProperty("instructions").GetString(), "Realtime instructions");
        AssertEqual("audio", session.GetProperty("output_modalities")[0].GetString(), "Realtime voice modality");
        AssertEqual("marin", session.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString(),
            "Realtime voice");
    }

    private static void TestRealtimeSessionValidatesServiceContract() {
        AssertThrows<ArgumentException>(
            () => new OpenAIRealtimeSessionOptions {
                OutputModalities = new[] { "audio", "text" }
            }.Validate(),
            "Realtime rejects multiple output modalities");
        AssertThrows<ArgumentOutOfRangeException>(
            () => new OpenAIRealtimeSessionOptions {
                ClientSecretLifetime = TimeSpan.FromSeconds(9)
            }.Validate(),
            "Realtime rejects too-short client secret lifetime");
        AssertThrows<ArgumentOutOfRangeException>(
            () => new OpenAIRealtimeSessionOptions {
                ClientSecretLifetime = TimeSpan.FromSeconds(7201)
            }.Validate(),
            "Realtime rejects too-long client secret lifetime");

        var minimum = new OpenAIRealtimeSessionOptions {
            ClientSecretLifetime = TimeSpan.FromSeconds(10)
        };
        minimum.Validate();
        var maximum = new OpenAIRealtimeSessionOptions {
            ClientSecretLifetime = TimeSpan.FromSeconds(7200)
        };
        maximum.Validate();
    }

    private static void TestRealtimeEventsProjectTextAudioAndErrors() {
        var textEvent = OpenAIRealtimeEvent.Parse("""{"type":"response.output_text.delta","delta":"hello"}""");
        AssertEqual("response.output_text.delta", textEvent.Type, "Realtime text event type");
        AssertEqual("hello", textEvent.TextDelta, "Realtime text delta");
        AssertEqual<string?>(null, textEvent.AudioDelta, "Realtime text audio delta");

        var audioEvent = OpenAIRealtimeEvent.Parse("""{"type":"response.output_audio.delta","delta":"AQID"}""");
        AssertEqual("AQID", audioEvent.AudioDelta, "Realtime audio delta");
        AssertEqual<string?>(null, audioEvent.TextDelta, "Realtime audio text delta");

        var errorEvent = OpenAIRealtimeEvent.Parse("""{"type":"error","error":{"message":"bad request"}}""");
        AssertEqual("bad request", errorEvent.ErrorMessage, "Realtime error message");
    }

    private static void TestStoredChatGptAuthTakesPrecedenceOverCodexFallback() {
        var stored = new AuthBundle(
            OpenAICodexDefaults.Provider,
            "stored-token",
            "stored-refresh",
            DateTimeOffset.UtcNow.AddMinutes(5)) {
            AccountId = "stored-account"
        };
        var codex = new AuthBundle(
            OpenAICodexDefaults.Provider,
            "codex-token",
            "codex-refresh",
            DateTimeOffset.UtcNow.AddHours(1)) {
            AccountId = "different-account"
        };

        var selected = OpenAINativeAuthManager.SelectPreferredBundle(stored, codex);

        AssertEqual(stored, selected, "Configured auth store bundle");
        AssertEqual(codex, OpenAINativeAuthManager.SelectPreferredBundle(null, codex), "Codex auth fallback bundle");

        codex.AccountId = stored.AccountId;
        AssertEqual(
            codex,
            OpenAINativeAuthManager.SelectPreferredBundle(stored, codex),
            "Fresher Codex bundle for same account");

        codex.AccountId = "different-account";
        AssertEqual(
            codex,
            OpenAINativeAuthManager.SelectPreferredBundle(stored, codex, preferCodexSession: true),
            "Explicit current Codex session preference");
    }

    private static void TestChatGptOAuthRefreshIsSingleFlightForSharedBundles() {
        var sharedBundle = new AuthBundle(
            OpenAICodexDefaults.Provider,
            "old-access-token",
            "old-refresh-token",
            DateTimeOffset.UtcNow.AddMinutes(-5)) {
            AccountId = "shared-refresh-account"
        };
        var store = new TestRealtimeAuthStore(sharedBundle);
        var options = new OpenAINativeOptions {
            AuthStore = store,
            LoadCodexAuthJson = false,
            PersistCodexAuthJson = false
        };
        var refreshCount = 0;
        var manager = new OpenAINativeAuthManager(
            options,
            async (_, bundle, cancellationToken) => {
                Interlocked.Increment(ref refreshCount);
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                bundle.AccessToken = "new-access-token";
                bundle.RefreshToken = "new-refresh-token";
                bundle.ExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
                return new OAuthLoginResult(bundle, new Dictionary<string, string>());
            });

        var first = manager.RefreshAsync(sharedBundle, CancellationToken.None);
        var second = manager.RefreshAsync(sharedBundle, CancellationToken.None);
        Task.WhenAll(first, second).GetAwaiter().GetResult();

        AssertEqual(1, refreshCount, "OAuth refresh call count");
        AssertEqual("new-access-token", first.Result.AccessToken, "First refreshed access token");
        AssertEqual("new-access-token", second.Result.AccessToken, "Second refreshed access token");
    }

    private sealed class RecordingRealtimeHandler : HttpMessageHandler {
        private readonly string _responseJson;

        internal RecordingRealtimeHandler(string responseJson) {
            _responseJson = responseJson;
        }

        internal string? AuthorizationScheme { get; private set; }
        internal string? AuthorizationParameter { get; private set; }
        internal string? AccountId { get; private set; }
        internal string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            AccountId = request.Headers.TryGetValues("ChatGPT-Account-ID", out var values)
                ? string.Join(",", values)
                : null;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(_responseJson, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class TestRealtimeAuthStore : IAuthBundleStore {
        private readonly AuthBundle _bundle;

        internal TestRealtimeAuthStore(AuthBundle bundle) {
            _bundle = bundle;
        }

        public Task<AuthBundle?> GetAsync(
            string provider,
            string? accountId = null,
            CancellationToken cancellationToken = default) {
            return Task.FromResult<AuthBundle?>(_bundle);
        }

        public Task<IReadOnlyList<AuthBundle>> ListAsync(
            string provider,
            CancellationToken cancellationToken = default) {
            return Task.FromResult<IReadOnlyList<AuthBundle>>(new[] { _bundle });
        }

        public Task SaveAsync(AuthBundle bundle, CancellationToken cancellationToken = default) {
            return Task.CompletedTask;
        }
    }

    private static string EncodeBase64Url(string value) {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

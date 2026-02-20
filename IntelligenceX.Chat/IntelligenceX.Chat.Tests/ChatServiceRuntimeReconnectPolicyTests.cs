using IntelligenceX.Chat.Service;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.CompatibleHttp;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Guards runtime switch reconnect policy so we keep live process behavior stable:
/// provider-client reconnect only when transport credentials/endpoint settings change,
/// while model-only changes stay in-process without reconnect.
/// </summary>
public sealed class ChatServiceRuntimeReconnectPolicyTests {
    private sealed class RuntimeClientSettings {
        public OpenAITransportKind Transport { get; set; } = OpenAITransportKind.Native;
        public string? BaseUrl { get; set; } = null;
        public OpenAICompatibleHttpAuthMode AuthMode { get; set; } = OpenAICompatibleHttpAuthMode.Bearer;
        public string? ApiKey { get; set; } = null;
        public string? BasicUsername { get; set; } = null;
        public string? BasicPassword { get; set; } = null;
        public string? AccountId { get; set; } = null;
        public bool Streaming { get; set; } = true;
        public bool InsecureHttp { get; set; } = false;
        public bool InsecureHttpNonLoopback { get; set; } = false;
        public string? Model { get; set; } = "gpt-5.3-codex";

        public RuntimeClientSettings Clone() {
            return new RuntimeClientSettings {
                Transport = Transport,
                BaseUrl = BaseUrl,
                AuthMode = AuthMode,
                ApiKey = ApiKey,
                BasicUsername = BasicUsername,
                BasicPassword = BasicPassword,
                AccountId = AccountId,
                Streaming = Streaming,
                InsecureHttp = InsecureHttp,
                InsecureHttpNonLoopback = InsecureHttpNonLoopback,
                Model = Model
            };
        }
    }

    /// <summary>
    /// Ensures unchanged runtime settings do not reconnect the provider client.
    /// </summary>
    [Fact]
    public void ResolveRuntimeClientReconfigureDecision_DoesNotReconnect_WhenSettingsUnchanged() {
        var decision = ResolveDecision();

        Assert.False(decision.ReconnectClient);
        Assert.False(decision.ModelChanged);
    }

    /// <summary>
    /// Ensures model-only changes remain in-process and avoid provider-client reconnect.
    /// </summary>
    [Fact]
    public void ResolveRuntimeClientReconfigureDecision_ModelOnlyChange_DoesNotReconnect() {
        var decision = ResolveDecision(current: settings => settings.Model = "gpt-5.3-codex-spark");

        Assert.False(decision.ReconnectClient);
        Assert.True(decision.ModelChanged);
    }

    /// <summary>
    /// Ensures transport changes require provider-client reconnect.
    /// </summary>
    [Fact]
    public void ResolveRuntimeClientReconfigureDecision_TransportChange_Reconnects() {
        var decision = ResolveDecision(current: settings => settings.Transport = OpenAITransportKind.CompatibleHttp);

        Assert.True(decision.ReconnectClient);
        Assert.False(decision.ModelChanged);
    }

    /// <summary>
    /// Ensures endpoint/auth material changes trigger provider-client reconnect.
    /// </summary>
    [Theory]
    [InlineData("base_url")]
    [InlineData("auth_mode")]
    [InlineData("api_key")]
    [InlineData("basic_username")]
    [InlineData("basic_password")]
    [InlineData("account_id")]
    [InlineData("streaming")]
    [InlineData("insecure_http")]
    [InlineData("insecure_http_non_loopback")]
    public void ResolveRuntimeClientReconfigureDecision_ClientSettingsChange_Reconnects(string changedField) {
        var decision = ResolveDecision(current: settings => {
            switch (changedField) {
                case "base_url":
                    settings.BaseUrl = "https://api.example.com/v1";
                    break;
                case "auth_mode":
                    settings.AuthMode = OpenAICompatibleHttpAuthMode.Basic;
                    break;
                case "api_key":
                    settings.ApiKey = "new-secret";
                    break;
                case "basic_username":
                    settings.BasicUsername = "bridge-user";
                    break;
                case "basic_password":
                    settings.BasicPassword = "bridge-pass";
                    break;
                case "account_id":
                    settings.AccountId = "account-2";
                    break;
                case "streaming":
                    settings.Streaming = false;
                    break;
                case "insecure_http":
                    settings.InsecureHttp = true;
                    break;
                case "insecure_http_non_loopback":
                    settings.InsecureHttpNonLoopback = true;
                    break;
                default:
                    throw new Xunit.Sdk.XunitException("Unknown field: " + changedField);
            }
        });

        Assert.True(decision.ReconnectClient);
        Assert.False(decision.ModelChanged);
    }

    private static (bool ReconnectClient, bool ModelChanged) ResolveDecision(
        Action<RuntimeClientSettings>? previous = null,
        Action<RuntimeClientSettings>? current = null) {
        var previousSettings = new RuntimeClientSettings();
        var currentSettings = previousSettings.Clone();

        previous?.Invoke(previousSettings);
        current?.Invoke(currentSettings);

        return ChatServiceSession.ResolveRuntimeClientReconfigureDecision(
            previousSettings.Transport,
            currentSettings.Transport,
            previousSettings.BaseUrl,
            currentSettings.BaseUrl,
            previousSettings.AuthMode,
            currentSettings.AuthMode,
            previousSettings.ApiKey,
            currentSettings.ApiKey,
            previousSettings.BasicUsername,
            currentSettings.BasicUsername,
            previousSettings.BasicPassword,
            currentSettings.BasicPassword,
            previousSettings.AccountId,
            currentSettings.AccountId,
            previousSettings.Streaming,
            currentSettings.Streaming,
            previousSettings.InsecureHttp,
            currentSettings.InsecureHttp,
            previousSettings.InsecureHttpNonLoopback,
            currentSettings.InsecureHttpNonLoopback,
            previousSettings.Model,
            currentSettings.Model);
    }
}

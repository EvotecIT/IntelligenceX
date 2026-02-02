using IntelligenceX.Configuration;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Configuration for creating an <see cref="IntelligenceXClient"/>.
/// </summary>
/// <example>
/// <code>
/// var options = new IntelligenceXClientOptions {
///     TransportKind = OpenAITransportKind.Native,
///     DefaultModel = "gpt-5.2-codex"
/// };
/// </code>
/// </example>
public sealed class IntelligenceXClientOptions {
    /// <summary>
    /// Options for the app-server transport.
    /// </summary>
    public AppServerOptions AppServerOptions { get; } = new();
    /// <summary>
    /// Options for the native ChatGPT transport.
    /// </summary>
    public OpenAINativeOptions NativeOptions { get; } = new();
    /// <summary>
    /// Selected transport for API calls.
    /// </summary>
    public OpenAITransportKind TransportKind { get; set; } = OpenAITransportKind.Native;
    /// <summary>
    /// Client identity metadata sent to providers.
    /// </summary>
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX", "0.1.0");
    public bool AutoInitialize { get; set; } = true;
    /// <summary>
    /// Default model name used for requests when none is provided.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-5.2-codex";
    public string? DefaultWorkingDirectory { get; set; }
    public string? DefaultApprovalPolicy { get; set; }
    public SandboxPolicy? DefaultSandboxPolicy { get; set; }

    public void Validate() {
        if (ClientInfo is null) {
            throw new ArgumentNullException(nameof(ClientInfo));
        }
        if (AppServerOptions is null) {
            throw new ArgumentNullException(nameof(AppServerOptions));
        }
        if (NativeOptions is null) {
            throw new ArgumentNullException(nameof(NativeOptions));
        }
        if (string.IsNullOrWhiteSpace(DefaultModel)) {
            throw new ArgumentException("DefaultModel cannot be null or whitespace.", nameof(DefaultModel));
        }
        if (TransportKind == OpenAITransportKind.AppServer) {
            AppServerOptions.Validate();
        } else {
            NativeOptions.Validate();
        }
    }

    public bool TryApplyConfig(string? path = null, string? baseDirectory = null) {
        if (!IntelligenceXConfig.TryLoad(out var config, path, baseDirectory)) {
            return false;
        }
        config.OpenAI.ApplyTo(this);
        return true;
    }
}

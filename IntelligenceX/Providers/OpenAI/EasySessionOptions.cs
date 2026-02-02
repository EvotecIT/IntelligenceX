using System;
using System.Threading.Tasks;
using IntelligenceX.Configuration;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Login modes used by <see cref="EasySession"/>.
/// </summary>
public enum EasyLoginMode {
    ChatGpt,
    ApiKey,
    None
}

/// <summary>
/// Options controlling <see cref="EasySession"/> behavior.
/// </summary>
/// <example>
/// <code>
/// var options = new EasySessionOptions {
///     TransportKind = OpenAITransportKind.Native,
///     Login = EasyLoginMode.ChatGpt
/// };
/// </code>
/// </example>
public sealed class EasySessionOptions {
    /// <summary>
    /// App-server transport options.
    /// </summary>
    public AppServerOptions AppServerOptions { get; } = new();
    /// <summary>
    /// Native ChatGPT transport options.
    /// </summary>
    public OpenAINativeOptions NativeOptions { get; } = new();
    /// <summary>
    /// Selected transport.
    /// </summary>
    public OpenAITransportKind TransportKind { get; set; } = OpenAITransportKind.Native;
    /// <summary>
    /// Client identity metadata sent during initialization.
    /// </summary>
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX Easy", "0.1.0");
    /// <summary>
    /// Login mode used by the session.
    /// </summary>
    public EasyLoginMode Login { get; set; } = EasyLoginMode.ChatGpt;
    /// <summary>
    /// API key used for <see cref="EasyLoginMode.ApiKey"/>.
    /// </summary>
    public string? ApiKey { get; set; }
    /// <summary>
    /// Callback invoked when a login URL is generated.
    /// </summary>
    public Action<string>? OnLoginUrl { get; set; }
    /// <summary>
    /// Prompt handler used for interactive auth flows.
    /// </summary>
    public Func<string, Task<string>>? OnPrompt { get; set; }
    /// <summary>
    /// Whether to use a local listener for OAuth redirects.
    /// </summary>
    public bool UseLocalListener { get; set; } = true;
    /// <summary>
    /// Whether to open a browser automatically for login.
    /// </summary>
    public bool OpenBrowser { get; set; } = true;
    /// <summary>
    /// Whether to print the login URL to stdout.
    /// </summary>
    public bool PrintLoginUrl { get; set; } = true;
    /// <summary>
    /// Whether to auto-initialize the client on connect.
    /// </summary>
    public bool AutoInitialize { get; set; } = true;
    /// <summary>
    /// Whether to auto-login when the session starts.
    /// </summary>
    public bool AutoLogin { get; set; } = true;
    /// <summary>
    /// Whether to validate login status before each request.
    /// </summary>
    public bool ValidateLoginOnEachRequest { get; set; } = true;
    /// <summary>
    /// Whether file access requires the workspace to be set.
    /// </summary>
    public bool RequireWorkspaceForFileAccess { get; set; } = true;
    /// <summary>
    /// Max image size (bytes) for image inputs.
    /// </summary>
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    /// <summary>
    /// Default model used for chat requests.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-5.2-codex";
    public string? WorkingDirectory { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// Validates the options for the selected transport.
    /// </summary>
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
        if (MaxImageBytes < 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxImageBytes), "MaxImageBytes cannot be negative.");
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

    public static EasySessionOptions FromConfig(string? path = null, string? baseDirectory = null) {
        var options = new EasySessionOptions();
        options.TryApplyConfig(path, baseDirectory);
        return options;
    }
}

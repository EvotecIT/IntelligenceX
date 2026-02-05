using System;
using System.Threading.Tasks;
using IntelligenceX.Configuration;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Identifies which login mode to use for easy sessions.
/// </summary>
public enum EasyLoginMode {
    /// <summary>
    /// Uses ChatGPT login flow.
    /// </summary>
    ChatGpt,
    /// <summary>
    /// Uses API key authentication.
    /// </summary>
    ApiKey,
    /// <summary>
    /// Disables login.
    /// </summary>
    None
}

/// <summary>
/// Options for creating <see cref="EasySession"/> instances.
/// </summary>
public sealed class EasySessionOptions {
    /// <summary>
    /// App-server transport options.
    /// </summary>
    public AppServerOptions AppServerOptions { get; } = new();
    /// <summary>
    /// Native transport options.
    /// </summary>
    public OpenAINativeOptions NativeOptions { get; } = new();
    /// <summary>
    /// Selected transport kind.
    /// </summary>
    public OpenAITransportKind TransportKind { get; set; } = OpenAITransportKind.Native;
    /// <summary>
    /// Client identity for native calls.
    /// </summary>
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX Easy", "0.1.0");
    /// <summary>
    /// Login mode selection.
    /// </summary>
    public EasyLoginMode Login { get; set; } = EasyLoginMode.ChatGpt;
    /// <summary>
    /// API key to use when <see cref="Login"/> is <see cref="EasyLoginMode.ApiKey"/>.
    /// </summary>
    public string? ApiKey { get; set; }
    /// <summary>
    /// Callback invoked with the login URL.
    /// </summary>
    public Action<string>? OnLoginUrl { get; set; }
    /// <summary>
    /// Callback used for prompting the user.
    /// </summary>
    public Func<string, Task<string>>? OnPrompt { get; set; }
    /// <summary>
    /// Whether to use a local listener during login.
    /// </summary>
    public bool UseLocalListener { get; set; } = true;
    /// <summary>
    /// Whether to open a browser during login.
    /// </summary>
    public bool OpenBrowser { get; set; } = true;
    /// <summary>
    /// Whether to print the login URL.
    /// </summary>
    public bool PrintLoginUrl { get; set; } = true;
    /// <summary>
    /// Whether to initialize the transport automatically.
    /// </summary>
    public bool AutoInitialize { get; set; } = true;
    /// <summary>
    /// Whether to login automatically on initialization.
    /// </summary>
    public bool AutoLogin { get; set; } = true;
    /// <summary>
    /// Whether to validate login before each request.
    /// </summary>
    public bool ValidateLoginOnEachRequest { get; set; } = true;
    /// <summary>
    /// Whether to require a workspace for file access.
    /// </summary>
    public bool RequireWorkspaceForFileAccess { get; set; } = true;
    /// <summary>
    /// Maximum allowed image size in bytes.
    /// </summary>
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    /// <summary>
    /// Default model name.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-5.3-codex";
    /// <summary>
    /// Default working directory for file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Workspace path for tool access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Whether network access is allowed.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Approval policy string passed to the app-server.
    /// </summary>
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// Validates the options and throws on invalid configuration.
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

    /// <summary>
    /// Attempts to apply configuration from disk to these options.
    /// </summary>
    /// <param name="path">Optional config path.</param>
    /// <param name="baseDirectory">Optional base directory for resolving the default config path.</param>
    /// <returns><c>true</c> when a config file was loaded.</returns>
    public bool TryApplyConfig(string? path = null, string? baseDirectory = null) {
        if (!IntelligenceXConfig.TryLoad(out var config, path, baseDirectory)) {
            return false;
        }
        config.OpenAI.ApplyTo(this);
        return true;
    }

    /// <summary>
    /// Creates new options and applies configuration from disk when available.
    /// </summary>
    /// <param name="path">Optional config path.</param>
    /// <param name="baseDirectory">Optional base directory for resolving the default config path.</param>
    public static EasySessionOptions FromConfig(string? path = null, string? baseDirectory = null) {
        var options = new EasySessionOptions();
        options.TryApplyConfig(path, baseDirectory);
        return options;
    }
}

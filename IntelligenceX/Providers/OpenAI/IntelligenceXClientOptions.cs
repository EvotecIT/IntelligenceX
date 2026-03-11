using IntelligenceX.Configuration;
using IntelligenceX.Copilot;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Configuration for creating an <see cref="IntelligenceXClient"/>.
/// </summary>
/// <example>
/// <code>
/// var options = new IntelligenceXClientOptions {
///     TransportKind = OpenAITransportKind.Native,
///     DefaultModel = OpenAIModelCatalog.DefaultModel
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
    /// Options for OpenAI-compatible HTTP transports (for example local providers such as Ollama/LM Studio).
    /// </summary>
    public OpenAICompatibleHttpOptions CompatibleHttpOptions { get; } = new();
    /// <summary>
    /// Options for the Copilot CLI transport.
    /// </summary>
    public CopilotClientOptions CopilotOptions { get; } = CreateDefaultCopilotOptions();
    /// <summary>
    /// Selected transport for API calls.
    /// </summary>
    public OpenAITransportKind TransportKind { get; set; } = OpenAITransportKind.Native;
    /// <summary>
    /// Client identity metadata sent to providers.
    /// </summary>
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX", "0.1.0");
    /// <summary>
    /// Whether to auto-initialize the transport.
    /// </summary>
    public bool AutoInitialize { get; set; } = true;
    /// <summary>
    /// Default model name used for requests when none is provided.
    /// </summary>
    public string DefaultModel { get; set; } = OpenAIModelCatalog.DefaultModel;
    /// <summary>
    /// Default working directory for file operations.
    /// </summary>
    public string? DefaultWorkingDirectory { get; set; }
    /// <summary>
    /// Default approval policy used by the app-server.
    /// </summary>
    public string? DefaultApprovalPolicy { get; set; }
    /// <summary>
    /// Default sandbox policy used by the app-server.
    /// </summary>
    public SandboxPolicy? DefaultSandboxPolicy { get; set; }
    /// <summary>
    /// Enables persistent usage telemetry for IntelligenceX-owned turns.
    /// </summary>
    public bool EnableUsageTelemetry { get; set; }
    /// <summary>
    /// Optional SQLite database path used for usage telemetry persistence.
    /// </summary>
    public string? UsageTelemetryDatabasePath { get; set; }
    /// <summary>
    /// Optional machine identifier stored with usage telemetry events.
    /// </summary>
    public string? UsageTelemetryMachineId { get; set; }
    /// <summary>
    /// Optional account label stored with usage telemetry events.
    /// </summary>
    public string? UsageTelemetryAccountLabel { get; set; }
    /// <summary>
    /// Optional provider account identifier stored with usage telemetry events.
    /// </summary>
    public string? UsageTelemetryProviderAccountId { get; set; }
    /// <summary>
    /// Optional logical source path used for the usage telemetry root.
    /// </summary>
    public string? UsageTelemetrySourcePath { get; set; }

    /// <summary>
    /// Validates the configuration and throws on invalid values.
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
        if (CompatibleHttpOptions is null) {
            throw new ArgumentNullException(nameof(CompatibleHttpOptions));
        }
        if (CopilotOptions is null) {
            throw new ArgumentNullException(nameof(CopilotOptions));
        }
        if (string.IsNullOrWhiteSpace(DefaultModel)) {
            throw new ArgumentException("DefaultModel cannot be null or whitespace.", nameof(DefaultModel));
        }
        switch (TransportKind) {
            case OpenAITransportKind.AppServer:
                AppServerOptions.Validate();
                break;
            case OpenAITransportKind.CompatibleHttp:
                CompatibleHttpOptions.Validate();
                break;
            case OpenAITransportKind.CopilotCli:
                CopilotOptions.Validate();
                break;
            default:
                NativeOptions.Validate();
                break;
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
        config.Copilot.ApplyTo(CopilotOptions);
        return true;
    }

    private static CopilotClientOptions CreateDefaultCopilotOptions() {
        return new CopilotClientOptions {
            AutoInstallCli = true,
            AutoInstallMethod = CopilotCliInstallMethod.Auto
        };
    }
}

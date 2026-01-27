using System;
using System.Threading.Tasks;
using IntelligenceX.Configuration;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI;

public enum EasyLoginMode {
    ChatGpt,
    ApiKey,
    None
}

public sealed class EasySessionOptions {
    public AppServerOptions AppServerOptions { get; } = new();
    public OpenAINativeOptions NativeOptions { get; } = new();
    public OpenAITransportKind TransportKind { get; set; } = OpenAITransportKind.Native;
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX Easy", "0.1.0");
    public EasyLoginMode Login { get; set; } = EasyLoginMode.ChatGpt;
    public string? ApiKey { get; set; }
    public Action<string>? OnLoginUrl { get; set; }
    public Func<string, Task<string>>? OnPrompt { get; set; }
    public bool UseLocalListener { get; set; } = true;
    public bool OpenBrowser { get; set; } = true;
    public bool PrintLoginUrl { get; set; } = true;
    public bool AutoInitialize { get; set; } = true;
    public bool AutoLogin { get; set; } = true;
    public bool ValidateLoginOnEachRequest { get; set; } = true;
    public bool RequireWorkspaceForFileAccess { get; set; } = true;
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
    public string DefaultModel { get; set; } = "gpt-5.2-codex";
    public string? WorkingDirectory { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    public string? ApprovalPolicy { get; set; }

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

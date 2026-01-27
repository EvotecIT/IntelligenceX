using IntelligenceX.Configuration;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI;

public sealed class IntelligenceXClientOptions {
    public AppServerOptions AppServerOptions { get; } = new();
    public OpenAINativeOptions NativeOptions { get; } = new();
    public OpenAITransportKind TransportKind { get; set; } = OpenAITransportKind.Native;
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX", "0.1.0");
    public bool AutoInitialize { get; set; } = true;
    public string DefaultModel { get; set; } = "gpt-5.1";
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

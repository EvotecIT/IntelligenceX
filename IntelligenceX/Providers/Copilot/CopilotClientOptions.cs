using System;
using System.Collections.Generic;
using IntelligenceX.Configuration;
using IntelligenceX.Utils;

namespace IntelligenceX.Copilot;

public sealed class CopilotClientOptions {
    public string? CliPath { get; set; } = "copilot";
    public List<string> CliArgs { get; } = new();
    public string? CliUrl { get; set; }
    public bool UseStdio { get; set; } = true;
    public int Port { get; set; } = 0;
    public string LogLevel { get; set; } = "info";
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; } = new();
    public bool AutoStart { get; set; } = true;
    public bool AutoInstallCli { get; set; }
    public CopilotCliInstallMethod AutoInstallMethod { get; set; } = CopilotCliInstallMethod.Auto;
    public bool AutoInstallPrerelease { get; set; }
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int ConnectRetryCount { get; set; } = 2;
    public TimeSpan ConnectRetryInitialDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ConnectRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public RpcRetryOptions RpcRetry { get; } = new();

    public void Validate() {
        if (ConnectRetryCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(ConnectRetryCount), "ConnectRetryCount cannot be negative.");
        }
        if (ConnectRetryInitialDelay < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(ConnectRetryInitialDelay), "ConnectRetryInitialDelay cannot be negative.");
        }
        if (ConnectRetryMaxDelay < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(ConnectRetryMaxDelay), "ConnectRetryMaxDelay cannot be negative.");
        }
        if (ConnectRetryMaxDelay < ConnectRetryInitialDelay) {
            throw new ArgumentException("ConnectRetryMaxDelay cannot be smaller than ConnectRetryInitialDelay.");
        }
        if (ConnectTimeout < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeout), "ConnectTimeout cannot be negative.");
        }
        if (ShutdownTimeout < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(ShutdownTimeout), "ShutdownTimeout cannot be negative.");
        }
        if (RpcRetry.InitialDelay < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(RpcRetry.InitialDelay), "RpcRetry.InitialDelay cannot be negative.");
        }
        if (RpcRetry.MaxDelay < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(RpcRetry.MaxDelay), "RpcRetry.MaxDelay cannot be negative.");
        }
        if (RpcRetry.MaxDelay < RpcRetry.InitialDelay) {
            throw new ArgumentException("RpcRetry.MaxDelay cannot be smaller than RpcRetry.InitialDelay.");
        }
        if (!AutoStart && string.IsNullOrWhiteSpace(CliUrl)) {
            throw new InvalidOperationException("AutoStart is disabled and no CliUrl was provided.");
        }
    }

    public bool TryApplyConfig(string? path = null, string? baseDirectory = null) {
        if (!IntelligenceXConfig.TryLoad(out var config, path, baseDirectory)) {
            return false;
        }
        config.Copilot.ApplyTo(this);
        return true;
    }
}

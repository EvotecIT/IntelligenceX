using System;
using System.Collections.Generic;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer;

public sealed class AppServerOptions {
    public string ExecutablePath { get; set; } = "codex";
    public string Arguments { get; set; } = "app-server";
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> Environment { get; } = new();
    public bool RedirectStandardError { get; set; } = true;
    public bool HealthCheckOnStart { get; set; }
    public string HealthCheckMethod { get; set; } = "config/read";
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int ConnectRetryCount { get; set; } = 2;
    public TimeSpan ConnectRetryInitialDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan ConnectRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(2);
    public RpcRetryOptions RpcRetry { get; } = new();

    public void Validate() {
        if (string.IsNullOrWhiteSpace(ExecutablePath)) {
            throw new ArgumentException("ExecutablePath cannot be null or whitespace.", nameof(ExecutablePath));
        }
        if (string.IsNullOrWhiteSpace(Arguments)) {
            throw new ArgumentException("Arguments cannot be null or whitespace.", nameof(Arguments));
        }
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
        if (StartTimeout < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(StartTimeout), "StartTimeout cannot be negative.");
        }
        if (HealthCheckTimeout < TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(HealthCheckTimeout), "HealthCheckTimeout cannot be negative.");
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
    }
}

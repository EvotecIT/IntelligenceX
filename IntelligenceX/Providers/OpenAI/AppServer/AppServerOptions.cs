using System;
using System.Collections.Generic;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer;

/// <summary>
/// Options for starting and configuring the app-server process.
/// </summary>
public sealed class AppServerOptions {
    /// <summary>
    /// Path to the app-server executable.
    /// </summary>
    public string ExecutablePath { get; set; } = "codex";
    /// <summary>
    /// Arguments passed to the executable.
    /// </summary>
    public string Arguments { get; set; } = "app-server";
    /// <summary>
    /// Working directory for the process.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Environment variables passed to the process.
    /// </summary>
    public Dictionary<string, string> Environment { get; } = new();
    /// <summary>
    /// Whether to redirect standard error.
    /// </summary>
    public bool RedirectStandardError { get; set; } = true;
    /// <summary>
    /// Whether to perform a health check after start.
    /// </summary>
    public bool HealthCheckOnStart { get; set; }
    /// <summary>
    /// Health check method name.
    /// </summary>
    public string HealthCheckMethod { get; set; } = "config/read";
    /// <summary>
    /// Timeout for the app-server start.
    /// </summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>
    /// Timeout for health check calls.
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>
    /// Retry count for connect attempts.
    /// </summary>
    public int ConnectRetryCount { get; set; } = 2;
    /// <summary>
    /// Initial delay for connect retries.
    /// </summary>
    public TimeSpan ConnectRetryInitialDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>
    /// Maximum delay for connect retries.
    /// </summary>
    public TimeSpan ConnectRetryMaxDelay { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>
    /// Timeout for graceful shutdown.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>
    /// Retry options for RPC calls.
    /// </summary>
    public RpcRetryOptions RpcRetry { get; } = new();

    /// <summary>
    /// Validates the configuration and throws on invalid values.
    /// </summary>
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

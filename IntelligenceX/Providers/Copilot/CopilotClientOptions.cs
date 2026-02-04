using System;
using System.IO;
using System.Collections.Generic;
using IntelligenceX.Configuration;
using IntelligenceX.Utils;

namespace IntelligenceX.Copilot;

/// <summary>
/// Options for connecting to the Copilot CLI.
/// </summary>
public sealed class CopilotClientOptions {
    /// <summary>
    /// Path to the Copilot CLI executable.
    /// </summary>
    public string? CliPath { get; set; } = "copilot";
    /// <summary>
    /// Additional CLI arguments.
    /// </summary>
    public List<string> CliArgs { get; } = new();
    /// <summary>
    /// Override URL for the Copilot CLI download.
    /// </summary>
    public string? CliUrl { get; set; }
    /// <summary>
    /// Whether to use stdio transport.
    /// </summary>
    public bool UseStdio { get; set; } = true;
    /// <summary>
    /// Port for HTTP transport when not using stdio.
    /// </summary>
    public int Port { get; set; } = 0;
    /// <summary>
    /// Log level for the CLI.
    /// </summary>
    public string LogLevel { get; set; } = "info";
    /// <summary>
    /// Working directory for the CLI process.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Environment variables for the CLI process.
    /// </summary>
    public Dictionary<string, string> Environment { get; } = new();
    /// <summary>
    /// Whether to inherit the current process environment.
    /// </summary>
    /// <remarks>
    /// When false, only <see cref="Environment"/> entries are passed to the CLI process.
    /// </remarks>
    public bool InheritEnvironment { get; set; } = true;
    /// <summary>
    /// Whether to start the CLI automatically.
    /// </summary>
    public bool AutoStart { get; set; } = true;
    /// <summary>
    /// Whether to auto-install the CLI when missing.
    /// </summary>
    public bool AutoInstallCli { get; set; }
    /// <summary>
    /// Preferred auto-install method.
    /// </summary>
    public CopilotCliInstallMethod AutoInstallMethod { get; set; } = CopilotCliInstallMethod.Auto;
    /// <summary>
    /// Whether to allow prerelease installs.
    /// </summary>
    public bool AutoInstallPrerelease { get; set; }
    /// <summary>
    /// Connection timeout.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>
    /// Retry count for connection attempts.
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
    /// Validates configuration values.
    /// </summary>
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
        if (!InheritEnvironment) {
            var cliPath = CliPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cliPath) ||
                (!Path.IsPathRooted(cliPath) &&
                 !cliPath.Contains(Path.DirectorySeparatorChar) &&
                 !cliPath.Contains(Path.AltDirectorySeparatorChar))) {
                throw new InvalidOperationException("InheritEnvironment is false; set CliPath to an absolute or relative path (with separators) so PATH lookups are not required.");
            }
        }
        if (!AutoStart && string.IsNullOrWhiteSpace(CliUrl)) {
            throw new InvalidOperationException("AutoStart is disabled and no CliUrl was provided.");
        }
    }

    /// <summary>
    /// Attempts to apply configuration from disk.
    /// </summary>
    /// <param name="path">Optional config path.</param>
    /// <param name="baseDirectory">Optional base directory for resolving the default config path.</param>
    /// <returns><c>true</c> when a config file was loaded.</returns>
    public bool TryApplyConfig(string? path = null, string? baseDirectory = null) {
        if (!IntelligenceXConfig.TryLoad(out var config, path, baseDirectory)) {
            return false;
        }
        config.Copilot.ApplyTo(this);
        return true;
    }
}

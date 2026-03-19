using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Identifies where the tray resolved GitHub credentials from.
/// </summary>
public enum GitHubCredentialSource {
    /// <summary>
    /// No GitHub credential source was available.
    /// </summary>
    None = 0,

    /// <summary>
    /// The token came from environment variables.
    /// </summary>
    Environment = 1,

    /// <summary>
    /// The token came from an existing local GitHub CLI login.
    /// </summary>
    GitHubCli = 2
}

/// <summary>
/// Holds the resolved GitHub credential state for the tray.
/// </summary>
public sealed class GitHubCredentialResolution {
    /// <summary>
    /// Initializes a credential resolution result.
    /// </summary>
    public GitHubCredentialResolution(string? token, GitHubCredentialSource source) {
        Token = NormalizeOptional(token);
        Source = string.IsNullOrWhiteSpace(Token) ? GitHubCredentialSource.None : source;
    }

    /// <summary>
    /// Gets the resolved GitHub token when one is available.
    /// </summary>
    public string? Token { get; }

    /// <summary>
    /// Gets the source that produced the token.
    /// </summary>
    public GitHubCredentialSource Source { get; }

    /// <summary>
    /// Gets whether a usable GitHub token was found.
    /// </summary>
    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Resolves GitHub credentials from supported local sources without requiring manual token entry.
/// </summary>
public static class GitHubCredentialResolver {
    private static readonly object CacheGate = new();
    private static readonly TimeSpan GitHubCliTokenTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan GitHubCliTokenCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MissingTokenCacheTtl = TimeSpan.FromMinutes(1);

    private static string? _cachedCliToken;
    private static DateTimeOffset _cachedCliTokenExpiresAtUtc;

    /// <summary>
    /// Returns a GitHub token from supported environment variables when present.
    /// </summary>
    public static string? ResolveFromEnvironment() {
        return NormalizeOptional(Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN"))
               ?? NormalizeOptional(Environment.GetEnvironmentVariable("GITHUB_TOKEN"))
               ?? NormalizeOptional(Environment.GetEnvironmentVariable("GH_TOKEN"));
    }

    /// <summary>
    /// Resolves GitHub credentials from the preferred source chain.
    /// </summary>
    public static async Task<GitHubCredentialResolution> ResolveAsync(CancellationToken cancellationToken = default) {
        var environmentToken = ResolveFromEnvironment();
        if (!string.IsNullOrWhiteSpace(environmentToken)) {
            return new GitHubCredentialResolution(environmentToken, GitHubCredentialSource.Environment);
        }

        var gitHubCliToken = await ResolveFromGitHubCliAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(gitHubCliToken)) {
            return new GitHubCredentialResolution(gitHubCliToken, GitHubCredentialSource.GitHubCli);
        }

        return new GitHubCredentialResolution(token: null, GitHubCredentialSource.None);
    }

    private static async Task<string?> ResolveFromGitHubCliAsync(CancellationToken cancellationToken) {
        var now = DateTimeOffset.UtcNow;
        lock (CacheGate) {
            if (_cachedCliTokenExpiresAtUtc > now) {
                return _cachedCliToken;
            }
        }

        var resolvedToken = await TryGetGitHubCliTokenAsync(cancellationToken).ConfigureAwait(false);
        var cacheTtl = string.IsNullOrWhiteSpace(resolvedToken)
            ? MissingTokenCacheTtl
            : GitHubCliTokenCacheTtl;

        lock (CacheGate) {
            _cachedCliToken = resolvedToken;
            _cachedCliTokenExpiresAtUtc = now.Add(cacheTtl);
        }

        return resolvedToken;
    }

    private static async Task<string?> TryGetGitHubCliTokenAsync(CancellationToken cancellationToken) {
        try {
            using var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "gh",
                    Arguments = "auth token",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start()) {
                return null;
            }

            var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd(), cancellationToken);
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd(), cancellationToken);

            var waitTask = Task.Run(() => process.WaitForExit((int)GitHubCliTokenTimeout.TotalMilliseconds), cancellationToken);
            var exited = await waitTask.ConfigureAwait(false);
            if (!exited) {
                TryTerminate(process);
                return null;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            _ = await stderrTask.ConfigureAwait(false);

            return process.ExitCode == 0
                ? NormalizeOptional(stdout)
                : null;
        } catch (OperationCanceledException) {
            throw;
        } catch {
            return null;
        }
    }

    private static void TryTerminate(Process process) {
        try {
            if (!process.HasExited) {
                process.Kill();
                process.WaitForExit();
            }
        } catch {
            // Ignore best-effort shutdown failures.
        }
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

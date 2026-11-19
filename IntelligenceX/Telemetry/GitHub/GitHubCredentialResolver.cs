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
    GitHubCli = 2,

    /// <summary>
    /// The token is a GitHub App installation token, which has a rate limit of its own.
    /// </summary>
    GitHubApp = 3
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

#if NET8_0_OR_GREATER
    /// <summary>
    /// Gets the GitHub App token source when signed in as an App. Its tokens expire after an hour, so long-running
    /// clients send requests through <see cref="WrapHandler"/> rather than keeping <see cref="Token"/>.
    /// </summary>
    public GitHubAppTokenSource? App { get; init; }

    /// <summary>
    /// Puts App signing in front of <paramref name="inner"/> when signed in as an App; otherwise returns it unchanged.
    /// </summary>
    public System.Net.Http.HttpMessageHandler WrapHandler(System.Net.Http.HttpMessageHandler inner) =>
        App is null ? inner : new GitHubAppAuthHandler(App, inner);
#else
    /// <summary>Returns <paramref name="inner"/>; App sign-in needs .NET 8 or newer.</summary>
    public System.Net.Http.HttpMessageHandler WrapHandler(System.Net.Http.HttpMessageHandler inner) => inner;
#endif

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
#if NET8_0_OR_GREATER
        // A configured GitHub App comes first: it is what someone sets up to stop sharing the user's hourly budget.
        if (ResolveApp() is { } app) {
            try {
                return new GitHubCredentialResolution(await app.GetTokenAsync(cancellationToken).ConfigureAwait(false), GitHubCredentialSource.GitHubApp) { App = app };
            } catch (System.Net.Http.HttpRequestException) {
                // An App that can't sign in falls back to the user token, so a bad key never stops GitHub reads.
            } catch (System.IO.IOException) {
            } catch (System.Security.Cryptography.CryptographicException) {
            } catch (ArgumentException) {
            } catch (UnauthorizedAccessException) {
            }
        }
#endif
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

#if NET8_0_OR_GREATER
    private static GitHubAppTokenSource? _app;
    private static string? _appKey;

    /// <summary>One token source per App settings, so every client shares one cached installation token.</summary>
    private static GitHubAppTokenSource? ResolveApp() {
        var settings = GitHubAppSettings.Load();
        if (settings is null) return null;
        var key = settings.AppId + "/" + settings.InstallationId + "/" + settings.PrivateKeyPath + "/" + settings.ApiBaseUrl;
        lock (CacheGate) {
            if (_app is null || _appKey != key) {
                _app = new GitHubAppTokenSource(settings);
                _appKey = key;
            }
            return _app;
        }
    }

#endif
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

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

    /// <summary>The user token a request falls back to when the App can't renew its token.</summary>
    public string? AppFallbackToken { get; init; }

    /// <summary>
    /// Puts App signing in front of <paramref name="inner"/> when signed in as an App; otherwise returns it unchanged.
    /// </summary>
    public System.Net.Http.HttpMessageHandler WrapHandler(System.Net.Http.HttpMessageHandler inner) =>
        App is null ? inner : new GitHubAppAuthHandler(App, inner, AppFallbackToken);
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
    /// <summary>
    /// Credentials for reading repositories (pull requests, checks, reviews, projects), which a GitHub App installation
    /// can do with a rate limit of its own. A configured App that signs in is used, with the user token as its fallback;
    /// otherwise this is the user token. Callers that need a person (<c>/user</c>, notifications, "my" repositories)
    /// keep <see cref="ResolveAsync"/>.
    /// </summary>
    public static async Task<GitHubCredentialResolution> ResolveRepositoryAccessAsync(CancellationToken cancellationToken = default) {
        var user = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (ResolveApp() is not { } app) return user;
        try {
            var appToken = await app.GetTokenAsync(cancellationToken).ConfigureAwait(false);
            return new GitHubCredentialResolution(user.Token ?? appToken, user.HasToken ? user.Source : GitHubCredentialSource.GitHubApp) {
                App = app, AppFallbackToken = user.Token
            };
        } catch (Exception error) when (GitHubAppTokenSource.IsSignInFailure(error, cancellationToken)) {
            // An App that can't sign in now leaves the user token in charge, so a bad App setup never stops GitHub reads.
            // With a user token to fall back to, the App stays attached and long-running clients try it again later.
            return user.HasToken ? new GitHubCredentialResolution(user.Token, user.Source) { App = app, AppFallbackToken = user.Token } : user;
        }
    }

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

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.OpenAI.Native;

internal sealed class OpenAINativeAuthManager {
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> RefreshLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly OpenAINativeOptions _options;
    private readonly OAuthLoginService _oauth = new();
    private readonly Func<OAuthConfig, AuthBundle, CancellationToken, Task<OAuthLoginResult>> _refreshOAuthAsync;

    public OpenAINativeAuthManager(OpenAINativeOptions options)
        : this(options, null) {
    }

    internal OpenAINativeAuthManager(
        OpenAINativeOptions options,
        Func<OAuthConfig, AuthBundle, CancellationToken, Task<OAuthLoginResult>>? refreshOAuthAsync) {
        _options = options;
        _refreshOAuthAsync = refreshOAuthAsync ?? _oauth.RefreshAsync;
    }

    public async Task<AuthBundle?> TryGetValidBundleAsync(CancellationToken cancellationToken) {
        var bundle = await TryGetCurrentBundleAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null) {
            return null;
        }

        if (IsExpiring(bundle)) {
            bundle = await RefreshAsync(bundle, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(bundle.AccountId)) {
            bundle.AccountId = JwtDecoder.TryGetAccountId(bundle.AccessToken);
        }

        return bundle;
    }

    public async Task<AuthBundle> LoginAsync(Action<string>? onAuthUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
        var prompt = onPrompt ?? (p => DefaultPromptAsync(p, cancellationToken));
        var loginOptions = new OAuthLoginOptions(_options.OAuth) {
            OnAuthUrl = url => {
                onAuthUrl?.Invoke(url);
                return Task.CompletedTask;
            },
            OnPrompt = prompt,
            UseLocalListener = useLocalListener && _options.UseLocalListener,
            Timeout = timeout,
            CancellationToken = cancellationToken
        };

        var result = await _oauth.LoginAsync(loginOptions).ConfigureAwait(false);
        await SaveBundleAsync(result.Bundle, cancellationToken).ConfigureAwait(false);
        return result.Bundle;
    }

    public async Task<AuthBundle> RefreshAsync(AuthBundle bundle, CancellationToken cancellationToken) {
        var accessTokenBeforeLock = bundle.AccessToken;
        var refreshLock = RefreshLocks.GetOrAdd(
            BuildRefreshLockKey(bundle),
            _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var current = await TryGetCurrentBundleAsync(cancellationToken).ConfigureAwait(false);
            if (current is not null &&
                !string.Equals(current.AccessToken, accessTokenBeforeLock, StringComparison.Ordinal) &&
                !IsExpiring(current)) {
                return current;
            }

            var refreshCandidate = current ?? bundle;
            if (string.IsNullOrWhiteSpace(refreshCandidate.RefreshToken)) {
                throw new InvalidOperationException("Refresh token is missing. Re-run the ChatGPT login.");
            }

            var refreshed = await _refreshOAuthAsync(_options.OAuth, refreshCandidate, cancellationToken)
                .ConfigureAwait(false);
            await SaveBundleAsync(refreshed.Bundle, cancellationToken).ConfigureAwait(false);
            return refreshed.Bundle;
        } finally {
            refreshLock.Release();
        }
    }

    private async Task SaveBundleAsync(AuthBundle bundle, CancellationToken cancellationToken) {
        bundle.AccountId ??= JwtDecoder.TryGetAccountId(bundle.AccessToken);
        await _options.AuthStore.SaveAsync(bundle, cancellationToken).ConfigureAwait(false);

        if (_options.PersistCodexAuthJson && !string.IsNullOrWhiteSpace(bundle.IdToken)) {
            try {
                CodexAuthStore.WriteAuthJson(bundle, _options.CodexHome);
            } catch {
                // Codex auth export is best-effort; ignore failures.
            }
        }
    }

    private static bool IsExpiring(AuthBundle bundle) {
        if (!bundle.ExpiresAt.HasValue) {
            return false;
        }
        var now = DateTimeOffset.UtcNow;
        return now >= bundle.ExpiresAt.Value.Subtract(ExpirySkew);
    }

    private AuthBundle? TryGetCodexBundle() {
        if (!_options.LoadCodexAuthJson) {
            return null;
        }

        var bundle = CodexAuthStore.TryReadBundle(CodexAuthStore.ResolveAuthPath(_options.CodexHome));
        if (bundle is null || string.IsNullOrWhiteSpace(_options.AuthAccountId)) {
            return bundle;
        }

        return string.Equals(bundle.AccountId, _options.AuthAccountId, StringComparison.OrdinalIgnoreCase)
            ? bundle
            : null;
    }

    internal static AuthBundle? SelectPreferredBundle(
        AuthBundle? storedBundle,
        AuthBundle? codexBundle,
        bool preferCodexSession = false) {
        if (storedBundle is null) {
            return codexBundle;
        }
        if (codexBundle is null) {
            return storedBundle;
        }
        if (preferCodexSession) {
            return codexBundle;
        }
        if (
            string.IsNullOrWhiteSpace(storedBundle.AccountId) ||
            !string.Equals(storedBundle.AccountId, codexBundle.AccountId, StringComparison.OrdinalIgnoreCase)) {
            return storedBundle;
        }

        var storedExpiry = storedBundle.ExpiresAt ?? DateTimeOffset.MinValue;
        var codexExpiry = codexBundle.ExpiresAt ?? DateTimeOffset.MinValue;
        return codexExpiry >= storedExpiry ? codexBundle : storedBundle;
    }

    private async Task<AuthBundle?> TryGetCurrentBundleAsync(CancellationToken cancellationToken) {
        var storedBundle = await _options.AuthStore
            .GetAsync(OpenAICodexDefaults.Provider, _options.AuthAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (storedBundle is not null && string.IsNullOrWhiteSpace(storedBundle.AccountId)) {
            storedBundle.AccountId = JwtDecoder.TryGetAccountId(storedBundle.AccessToken);
        }
        return SelectPreferredBundle(
            storedBundle,
            TryGetCodexBundle(),
            _options.PreferCurrentCodexSession && string.IsNullOrWhiteSpace(_options.AuthAccountId));
    }

    private string BuildRefreshLockKey(AuthBundle bundle) {
        var accountId = (_options.AuthAccountId ?? bundle.AccountId)?.Trim();
        return OpenAICodexDefaults.Provider + "|" +
               (string.IsNullOrWhiteSpace(accountId) ? "default" : accountId);
    }

    private static Task<string> DefaultPromptAsync(string prompt, CancellationToken cancellationToken) {
        if (Console.IsInputRedirected) {
            throw new InvalidOperationException(
                "ChatGPT login requires user input. Provide a prompt handler or run interactively.");
        }

        const int maxAttempts = 5;
        Console.WriteLine(prompt);
        for (var attempt = 1; attempt <= maxAttempts; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            Console.Write("> ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input)) {
                var value = input.Trim();
                if (string.Equals(value, "cancel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "exit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "quit", StringComparison.OrdinalIgnoreCase)) {
                    // Distinguish user-cancel from token cancellation.
                    throw new OpenAIUserCanceledLoginException();
                }
                return Task.FromResult(value);
            }

            Console.WriteLine("Authorization code was not provided. Paste the redirect URL or authorization code.");
        }

        throw new InvalidOperationException("Authorization code was not provided.");
    }
}

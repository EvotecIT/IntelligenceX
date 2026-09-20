using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.OpenAI.Native;

internal sealed class OpenAINativeAuthManager {
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(1);

    private readonly OpenAINativeOptions _options;
    private readonly OAuthLoginService _oauth = new();

    public OpenAINativeAuthManager(OpenAINativeOptions options) {
        _options = options;
    }

    public async Task<AuthBundle?> TryGetValidBundleAsync(CancellationToken cancellationToken) {
        var bundle = await _options.AuthStore.GetAsync(OpenAICodexDefaults.Provider, _options.AuthAccountId, cancellationToken)
            .ConfigureAwait(false);
        if (bundle is null) {
            return null;
        }

        return await GetValidBundleAsync(bundle, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Refreshes a discovered bundle in its original store without selecting another account.
    /// Codex auth export remains governed by the caller's persistence option.
    /// </summary>
    internal async Task<AuthBundle> GetValidBundleAsync(AuthBundle bundle, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
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
        if (string.IsNullOrWhiteSpace(bundle.RefreshToken)) {
            throw new InvalidOperationException("Refresh token is missing. Re-run the ChatGPT login.");
        }
        var previousRefreshToken = bundle.RefreshToken;
        var previousAccountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        if (_options.AuthStore is FileAuthBundleStore fileStore) {
            var refreshedBundle = await fileStore.RefreshAsync(bundle, async (current, token) => {
                var result = await _oauth.RefreshAsync(_options.OAuth, current, token).ConfigureAwait(false);
                result.Bundle.AccountId ??= current.AccountId ?? JwtDecoder.TryGetAccountId(result.Bundle.AccessToken);
                return result.Bundle;
            }, cancellationToken).ConfigureAwait(false);
            ExportCodexBundle(refreshedBundle, previousRefreshToken, previousAccountId);
            return refreshedBundle;
        }
        var refreshed = await _oauth.RefreshAsync(_options.OAuth, bundle, cancellationToken).ConfigureAwait(false);
        await SaveBundleAsync(refreshed.Bundle, cancellationToken, previousRefreshToken, previousAccountId).ConfigureAwait(false);
        return refreshed.Bundle;
    }

    private async Task SaveBundleAsync(AuthBundle bundle, CancellationToken cancellationToken,
        string? previousRefreshToken = null, string? previousAccountId = null) {
        bundle.AccountId ??= JwtDecoder.TryGetAccountId(bundle.AccessToken);
        await _options.AuthStore.SaveAsync(bundle, cancellationToken).ConfigureAwait(false);
        ExportCodexBundle(bundle, previousRefreshToken, previousAccountId);
    }

    private void ExportCodexBundle(AuthBundle bundle, string? previousRefreshToken, string? previousAccountId) {
        if (_options.PersistCodexAuthJson && !string.IsNullOrWhiteSpace(bundle.IdToken)) {
            try {
                if (_options.PreserveCodexLoginOnRefresh) {
                    CodexAuthStore.UpdateMatchingAuthJson(bundle, previousAccountId, previousRefreshToken, _options.CodexHome);
                } else {
                    CodexAuthStore.WriteAuthJson(bundle, _options.CodexHome);
                }
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

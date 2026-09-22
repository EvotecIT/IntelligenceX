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
    private volatile bool _signedOut;
    private string? _selectedAccountId;
    private string? SelectedAccountId => _options.AuthAccountId ?? Volatile.Read(ref _selectedAccountId);
    private int _logoutGeneration;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _storeGate;
    private readonly AuthStoreCoordination.CredentialEpoch _storeEpoch;
    private readonly OAuthLoginService _oauth = new();
    private readonly Func<OAuthConfig, AuthBundle, CancellationToken, Task<OAuthLoginResult>> _refreshOAuthAsync;
    private readonly Func<OAuthLoginOptions, Task<OAuthLoginResult>> _loginOAuthAsync;

    public OpenAINativeAuthManager(OpenAINativeOptions options)
        : this(options, null) {
    }

    internal OpenAINativeAuthManager(
        OpenAINativeOptions options,
        Func<OAuthConfig, AuthBundle, CancellationToken, Task<OAuthLoginResult>>? refreshOAuthAsync,
        Func<OAuthLoginOptions, Task<OAuthLoginResult>>? loginOAuthAsync = null) {
        _options = options;
        _storeGate = AuthStoreCoordination.GetGate(options.AuthStore);
        _storeEpoch = AuthStoreCoordination.GetEpoch(_storeGate, OpenAICodexDefaults.Provider);
        _refreshOAuthAsync = refreshOAuthAsync ?? _oauth.RefreshAsync;
        _loginOAuthAsync = loginOAuthAsync ?? _oauth.LoginAsync;
    }

    public async Task<AuthBundle?> TryGetValidBundleAsync(CancellationToken cancellationToken) {
        var bundle = await TryGetCurrentBundleAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is null) {
            return null;
        }
        if (IsExpiring(bundle)) {
            bundle = await RefreshAsync(bundle, cancellationToken).ConfigureAwait(false);
        }
        bundle.AccountId ??= JwtDecoder.TryGetAccountId(bundle.AccessToken);
        return bundle;
    }

    /// <summary>
    /// Refreshes a discovered bundle in its original store without selecting another account.
    /// Codex auth export remains governed by the caller's persistence option.
    /// </summary>
    internal async Task<AuthBundle> GetValidBundleAsync(AuthBundle bundle, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_options.AuthStore is FileAuthBundleStore fileStore)
            bundle = await fileStore.SelectPreferredOpenAiAliasAsync(bundle, cancellationToken).ConfigureAwait(false);
        if (IsExpiring(bundle)) {
            bundle = await RefreshSpecificBundleAsync(bundle, cancellationToken).ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(bundle.AccountId)) {
            bundle.AccountId = JwtDecoder.TryGetAccountId(bundle.AccessToken);
        }

        return bundle;
    }

    private async Task<AuthBundle> RefreshSpecificBundleAsync(AuthBundle bundle, CancellationToken cancellationToken) {
        if (_options.AuthStore is FileAuthBundleStore fileStore) {
            bundle = await ReconcileAmbientCredentialAsync(fileStore, bundle, cancellationToken).ConfigureAwait(false);
            if (!IsExpiring(bundle)) return bundle;
            var fileRefreshToken = bundle.RefreshToken;
            var fileAccountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
            var refreshed = await fileStore.RefreshAsync(bundle, async (current, token) => {
                var result = await _refreshOAuthAsync(_options.OAuth, current, token).ConfigureAwait(false);
                result.Bundle.AccountId ??= current.AccountId ?? JwtDecoder.TryGetAccountId(result.Bundle.AccessToken);
                return result.Bundle;
            }, cancellationToken).ConfigureAwait(false);
            ExportCodexBundle(refreshed, fileAccountId, fileRefreshToken, refresh: true);
            return refreshed;
        }

        var previousAccountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        var previousRefreshToken = bundle.RefreshToken;
        var result = await _refreshOAuthAsync(_options.OAuth, bundle, cancellationToken).ConfigureAwait(false);
        result.Bundle.AccountId ??= previousAccountId;
        if (!string.Equals(result.Bundle.AccountId, previousAccountId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Token refresh returned a different account identity.");
        await _options.AuthStore.SaveAsync(result.Bundle, cancellationToken).ConfigureAwait(false);
        ExportCodexBundle(result.Bundle, previousAccountId, previousRefreshToken, refresh: true);
        return result.Bundle;
    }

    public async Task<AuthBundle> LoginAsync(Action<string>? onAuthUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
        var operation = CaptureOperation();
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

        var result = await _loginOAuthAsync(loginOptions).ConfigureAwait(false);
        result.Bundle.AccountId ??= JwtDecoder.TryGetAccountId(result.Bundle.AccessToken);
        await _storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            EnsureCurrentOperation(operation, result.Bundle.AccountId, cancellationToken);
            await SaveBundleAsync(result.Bundle, operation, cancellationToken).ConfigureAwait(false);
            lock (_stateLock) {
                EnsureCurrentOperation(operation, result.Bundle.AccountId, cancellationToken);
                _selectedAccountId = result.Bundle.AccountId;
                _signedOut = false;
            }
        } finally { _storeGate.Release(); }
        return result.Bundle;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock) {
            _logoutGeneration++;
            _signedOut = true;
        }
        // Wait for any non-cooperative store write before removing the selected account.
        await _storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var selected = await ReadCurrentBundleAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _storeEpoch.Invalidate(selected?.AccountId ?? SelectedAccountId);
            if (selected is not null) {
                if (_options.AuthStore is FileAuthBundleStore fileStore) {
                    // Refresh migration and logout share the file transaction, so a legacy
                    // alias cannot turn into a canonical credential between removals.
                    await fileStore.RemoveOpenAiIdentityAsync(selected, cancellationToken).ConfigureAwait(false);
                } else {
                    foreach (var provider in new[] { OpenAICodexDefaults.Provider, "openai", "chatgpt" }) {
                        foreach (var stored in await _options.AuthStore.ListAsync(provider, cancellationToken).ConfigureAwait(false)) {
                            if (SameSelectedAccount(stored, selected))
                                await _options.AuthStore.RemoveAsync(provider, stored.AccountId, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        } finally { _storeGate.Release(); }
    }

    public Task<AuthBundle> RefreshAsync(AuthBundle bundle, CancellationToken cancellationToken) =>
        RefreshAsync(bundle, CaptureOperation(), cancellationToken);

    internal async Task<AuthBundle> RefreshAsync(AuthBundle bundle, (int Generation, long Epoch) operation, CancellationToken cancellationToken) {
        if (_signedOut) throw new OpenAIAuthenticationRequiredException("ChatGPT is signed out. Sign in before refreshing credentials.");
        var accessTokenBeforeLock = bundle.AccessToken;
        var refreshLock = RefreshLocks.GetOrAdd(
            BuildRefreshLockKey(bundle),
            _ => new SemaphoreSlim(1, 1));
        await refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await _storeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                EnsureCurrentOperation(operation, _options.AuthAccountId ?? bundle.AccountId, cancellationToken);
                var current = await TryGetCurrentBundleAsync(cancellationToken).ConfigureAwait(false);
                if (current is null || !SameSelectedAccount(current, bundle))
                    throw new OpenAIAuthenticationRequiredException("The selected ChatGPT credential was removed or replaced. Sign in before retrying.");
                if (!string.Equals(current.AccessToken, accessTokenBeforeLock, StringComparison.Ordinal) &&
                    !IsExpiring(current)) {
                    EnsureCurrentOperation(operation, current.AccountId, cancellationToken);
                    return current;
                }

                var refreshCandidate = current;
                if (string.IsNullOrWhiteSpace(refreshCandidate.RefreshToken)) {
                    var storedBundle = await GetStoredBundleAsync(cancellationToken).ConfigureAwait(false);
                    refreshCandidate = SelectRefreshCandidate(refreshCandidate, storedBundle);
                }
                if (string.IsNullOrWhiteSpace(refreshCandidate.RefreshToken)) {
                    throw new InvalidOperationException("Refresh token is missing. Re-run the ChatGPT login.");
                }

                if (_options.AuthStore is FileAuthBundleStore fileStore) {
                    // Foreground chat and Tray must use the same per-account transaction.
                    // An ambient Codex login can precede the IX store; seed it only if
                    // another process has not already stored that account.
                    var stored = await GetStoredBundleAsync(cancellationToken).ConfigureAwait(false);
                    var fileCandidate = stored ?? await fileStore.SeedIfMissingAsync(refreshCandidate, cancellationToken)
                        .ConfigureAwait(false);
                    if (!SameSelectedAccount(fileCandidate, refreshCandidate))
                        throw new OpenAIAuthenticationRequiredException("The selected ChatGPT credential was replaced. Sign in before retrying.");
                    // Codex may have rotated this same account since IX last saved it.
                    // Import the newer ambient generation under the file store's
                    // per-account transaction, so a concurrent IX login still wins.
                    fileCandidate = await ReconcileAmbientCredentialAsync(fileStore, fileCandidate, cancellationToken)
                        .ConfigureAwait(false);
                    if (!IsExpiring(fileCandidate) && !string.Equals(fileCandidate.AccessToken, current.AccessToken, StringComparison.Ordinal)) {
                        EnsureCurrentOperation(operation, fileCandidate.AccountId, cancellationToken);
                        return fileCandidate;
                    }

                    var fileAccountId = fileCandidate.AccountId ?? JwtDecoder.TryGetAccountId(fileCandidate.AccessToken);
                    var fileRefreshToken = string.IsNullOrWhiteSpace(fileCandidate.RefreshToken)
                        ? refreshCandidate.RefreshToken : fileCandidate.RefreshToken;
                    var synchronized = await fileStore.RefreshAsync(fileCandidate, async (candidate, token) => {
                        var source = string.IsNullOrWhiteSpace(candidate.RefreshToken)
                            ? SelectRefreshCandidate(candidate, refreshCandidate) : candidate;
                        var result = await _refreshOAuthAsync(_options.OAuth, source, token).ConfigureAwait(false);
                        result.Bundle.AccountId ??= candidate.AccountId ?? JwtDecoder.TryGetAccountId(result.Bundle.AccessToken);
                        return result.Bundle;
                    }, cancellationToken).ConfigureAwait(false);
                    EnsureCurrentOperation(operation, synchronized.AccountId, cancellationToken);
                    ExportCodexBundle(synchronized, fileAccountId, fileRefreshToken, refresh: true);
                    return synchronized;
                }

                // OAuth refresh can mutate the candidate in place; retain the pre-rotation
                // identity for compare-and-swap export to Codex auth.json.
                var previousAccountId = refreshCandidate.AccountId;
                var previousRefreshToken = refreshCandidate.RefreshToken;
                var refreshed = await _refreshOAuthAsync(_options.OAuth, refreshCandidate, cancellationToken)
                    .ConfigureAwait(false);
                EnsureCurrentOperation(operation, refreshed.Bundle.AccountId, cancellationToken);
                await SaveBundleAsync(refreshed.Bundle, operation, cancellationToken,
                    previousAccountId, previousRefreshToken).ConfigureAwait(false);
                EnsureCurrentOperation(operation, refreshed.Bundle.AccountId, cancellationToken);
                return refreshed.Bundle;
            } finally { _storeGate.Release(); }
        } finally {
            refreshLock.Release();
        }
    }

    private async Task SaveBundleAsync(AuthBundle bundle, (int Generation, long Epoch) operation, CancellationToken cancellationToken,
        string? previousAccountId = null, string? previousRefreshToken = null) {
        bundle.AccountId ??= JwtDecoder.TryGetAccountId(bundle.AccessToken);
        await _options.AuthStore.SaveAsync(bundle, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateLock) {
            EnsureCurrentOperation(operation, bundle.AccountId, cancellationToken);
            ExportCodexBundle(bundle, previousAccountId, previousRefreshToken, refresh: previousRefreshToken is not null);
        }
    }

    private void ExportCodexBundle(AuthBundle bundle, string? previousAccountId, string? previousRefreshToken, bool refresh = false) {
        if (!_options.PersistCodexAuthJson || string.IsNullOrWhiteSpace(bundle.IdToken)) return;
        try {
            if (refresh || _options.PreserveCodexLoginOnRefresh)
                CodexAuthStore.UpdateMatchingAuthJson(bundle, previousAccountId, previousRefreshToken, _options.CodexHome);
            else
                CodexAuthStore.WriteAuthJson(bundle, _options.CodexHome);
        } catch {
            // Codex auth export is best-effort; ignore failures.
        }
    }

    private static bool IsExpiring(AuthBundle bundle) {
        if (!bundle.ExpiresAt.HasValue) {
            return false;
        }
        var now = DateTimeOffset.UtcNow;
        return now >= bundle.ExpiresAt.Value.Subtract(ExpirySkew);
    }

    private async Task<AuthBundle> ReconcileAmbientCredentialAsync(FileAuthBundleStore fileStore,
        AuthBundle stored, CancellationToken cancellationToken) {
        var ambient = TryGetCodexBundle();
        if (ambient is null || !SameSelectedAccount(stored, ambient)
            || string.IsNullOrWhiteSpace(ambient.AccountId)
            || string.IsNullOrWhiteSpace(ambient.RefreshToken)
            || (ambient.ExpiresAt ?? DateTimeOffset.MinValue) <= (stored.ExpiresAt ?? DateTimeOffset.MinValue)
            || string.Equals(ambient.RefreshToken, stored.RefreshToken, StringComparison.Ordinal)) return stored;

        // A new IX login wins if it replaced the expected generation. The file
        // transaction also serializes this import with other cross-process refreshes.
        return await fileStore.RefreshAsync(stored, (latest, _) => Task.FromResult(
            SameSelectedAccount(latest, ambient)
            && (ambient.ExpiresAt ?? DateTimeOffset.MinValue) > (latest.ExpiresAt ?? DateTimeOffset.MinValue)
                ? ambient : latest), cancellationToken).ConfigureAwait(false);
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

    internal static AuthBundle SelectRefreshCandidate(AuthBundle selectedBundle, AuthBundle? storedBundle) {
        if (!string.IsNullOrWhiteSpace(selectedBundle.RefreshToken) ||
            storedBundle is null ||
            string.IsNullOrWhiteSpace(storedBundle.RefreshToken)) {
            return selectedBundle;
        }

        selectedBundle.AccountId ??= JwtDecoder.TryGetAccountId(selectedBundle.AccessToken);
        storedBundle.AccountId ??= JwtDecoder.TryGetAccountId(storedBundle.AccessToken);
        return !string.IsNullOrWhiteSpace(selectedBundle.AccountId) &&
               string.Equals(selectedBundle.AccountId, storedBundle.AccountId, StringComparison.OrdinalIgnoreCase)
            ? storedBundle
            : selectedBundle;
    }

    private async Task<AuthBundle?> GetStoredBundleAsync(CancellationToken cancellationToken) {
        string? accountId = SelectedAccountId;
        AuthBundle? storedBundle = null;
        foreach (var provider in new[] { OpenAICodexDefaults.Provider, "openai", "chatgpt" }) {
            var candidate = await _options.AuthStore.GetAsync(provider, accountId, cancellationToken).ConfigureAwait(false);
            if (candidate is null && !string.IsNullOrWhiteSpace(accountId)) {
                // Older saves may have a provider-only key and JWT-derived identity.
                foreach (var legacy in await _options.AuthStore.ListAsync(provider, cancellationToken).ConfigureAwait(false)) {
                    if (string.IsNullOrWhiteSpace(legacy.AccountId)
                        && string.Equals(JwtDecoder.TryGetAccountId(legacy.AccessToken), accountId, StringComparison.OrdinalIgnoreCase)) {
                        candidate = legacy;
                        break;
                    }
                }
            }
            if (candidate is not null && (storedBundle is null
                || (SameSelectedAccount(candidate, storedBundle)
                    && (candidate.ExpiresAt ?? DateTimeOffset.MinValue) > (storedBundle.ExpiresAt ?? DateTimeOffset.MinValue))))
                storedBundle = candidate;
            // Without a selected identity, only compare aliases of the first saved login.
            if (storedBundle is not null && string.IsNullOrWhiteSpace(accountId)) {
                accountId = storedBundle.AccountId ?? JwtDecoder.TryGetAccountId(storedBundle.AccessToken);
                if (string.IsNullOrWhiteSpace(accountId)) break;
            }
        }
        if (storedBundle is not null && string.IsNullOrWhiteSpace(storedBundle.AccountId)) {
            storedBundle = new AuthBundle(storedBundle.Provider, storedBundle.AccessToken, storedBundle.RefreshToken, storedBundle.ExpiresAt) {
                AccountId = JwtDecoder.TryGetAccountId(storedBundle.AccessToken), IdToken = storedBundle.IdToken,
                TokenType = storedBundle.TokenType, Scope = storedBundle.Scope
            };
        }
        return storedBundle;
    }

    private async Task<AuthBundle?> TryGetCurrentBundleAsync(CancellationToken cancellationToken) {
        if (_signedOut) return null;
        var bundle = await ReadCurrentBundleAsync(cancellationToken).ConfigureAwait(false);
        return _signedOut ? null : bundle;
    }

    private async Task<AuthBundle?> ReadCurrentBundleAsync(CancellationToken cancellationToken) {
        var storedBundle = await GetStoredBundleAsync(cancellationToken).ConfigureAwait(false);
        var codexBundle = TryGetCodexBundle();
        lock (_stateLock) {
            // Selection belongs to this authentication lifetime. Missing credentials must not
            // switch an existing conversation to another stored or ambient account on retry.
            string? accountId = SelectedAccountId;
            AuthBundle? Match(AuthBundle? bundle) => string.IsNullOrWhiteSpace(accountId)
                || string.Equals(bundle?.AccountId, accountId, StringComparison.OrdinalIgnoreCase) ? bundle : null;
            var selected = SelectPreferredBundle(Match(storedBundle), Match(codexBundle),
                _options.PreferCurrentCodexSession && string.IsNullOrWhiteSpace(_options.AuthAccountId));
            if (selected is not null) _selectedAccountId ??= selected.AccountId;
            return selected;
        }
    }

    // A request captures this before sending; a delayed 401 must not start a new authentication lifetime.
    internal (int Generation, long Epoch) CaptureOperation() => (Volatile.Read(ref _logoutGeneration), _storeEpoch.Capture());

    private static bool SameSelectedAccount(AuthBundle current, AuthBundle previous) {
        string? currentId = current.AccountId ?? JwtDecoder.TryGetAccountId(current.AccessToken);
        string? previousId = previous.AccountId ?? JwtDecoder.TryGetAccountId(previous.AccessToken);
        return !string.IsNullOrWhiteSpace(currentId) && !string.IsNullOrWhiteSpace(previousId)
            ? string.Equals(currentId, previousId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(current.AccessToken, previous.AccessToken, StringComparison.Ordinal);
    }

    private void EnsureCurrentOperation((int Generation, long Epoch) operation, string? accountId, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Generation != Volatile.Read(ref _logoutGeneration) || !_storeEpoch.IsCurrent(operation.Epoch, accountId))
            throw new OpenAIAuthenticationRequiredException("Authentication was superseded by logout.");
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

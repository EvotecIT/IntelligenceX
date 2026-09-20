using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

public sealed partial class FileAuthBundleStore {
    private Task<FileStream> AcquireTransactionAsync(CancellationToken cancellationToken) =>
        AuthFileTransaction.AcquireAsync(_path + ".lock", cancellationToken);

    /// <summary>
    /// Serializes refreshers for one account across processes, without holding the store lock during network I/O.
    /// Reuses credentials already replaced by another caller instead of replaying an old refresh token.
    /// A deleted account is never silently restored by a background refresh.
    /// </summary>
    internal async Task<AuthBundle> RefreshAsync(AuthBundle expected,
        Func<AuthBundle, CancellationToken, Task<AuthBundle>> refresh, CancellationToken cancellationToken) {
        var key = BuildKey(expected.Provider, expected.AccountId);
        using var hash = SHA256.Create();
        var suffix = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key.ToUpperInvariant()))).Replace("-", "");
        using var refreshLock = await AuthFileTransaction.AcquireAsync(_path + ".refresh-" + suffix + ".lock", cancellationToken).ConfigureAwait(false);
        AuthBundle current;
        using (var transaction = await AcquireTransactionAsync(cancellationToken).ConfigureAwait(false)) {
            var before = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
            current = RequireAccount(before, key);
        }
        if (!SameCredentials(current, expected.AccessToken, expected.RefreshToken)) {
            return current;
        }
        // OAuth implementations may mutate the supplied bundle in place.
        var accessToken = current.AccessToken;
        var refreshToken = current.RefreshToken;
        var currentAccountId = current.AccountId ?? JwtDecoder.TryGetAccountId(current.AccessToken);
        var updated = await refresh(current, cancellationToken).ConfigureAwait(false);
        // Once credentials rotate, do not discard them because of late cancellation.
        using var commit = await AcquireTransactionAsync(CancellationToken.None).ConfigureAwait(false);
        var file = await ReadFileAsync(CancellationToken.None).ConfigureAwait(false);
        var latest = RequireAccount(file, key);
        if (!SameCredentials(latest, accessToken, refreshToken)) {
            return latest; // A new login wins over the in-flight refresh.
        }
        var updatedKey = BuildKey(updated.Provider, updated.AccountId);
        if (!string.Equals(updated.Provider, expected.Provider, StringComparison.OrdinalIgnoreCase)
            || (currentAccountId is not null && !string.Equals(updated.AccountId, currentAccountId, StringComparison.Ordinal))
            || (!string.Equals(updatedKey, key, StringComparison.OrdinalIgnoreCase) && file!.Bundles.ContainsKey(updatedKey))) {
            throw new InvalidOperationException("Token refresh returned a different account identity.");
        }
        // Legacy entries may not have an account id until the provider reports one.
        file!.Bundles.Remove(key);
        file.Bundles[updatedKey] = updated;
        // Once the provider returns rotated credentials, persist them even if the
        // caller canceled meanwhile. Cancellation must not discard a usable token.
        await WriteFileAsync(file, CancellationToken.None).ConfigureAwait(false);
        return updated;
    }

    private static AuthBundle RequireAccount(AuthBundleFile? file, string key) =>
        file is not null && file.Bundles.TryGetValue(key, out var account) ? account
            : throw new InvalidOperationException("The saved account was removed. Recheck sign-in before refreshing.");

    private static bool SameCredentials(AuthBundle bundle, string accessToken, string refreshToken) =>
        string.Equals(bundle.AccessToken, accessToken, StringComparison.Ordinal)
        && string.Equals(bundle.RefreshToken, refreshToken, StringComparison.Ordinal);
}

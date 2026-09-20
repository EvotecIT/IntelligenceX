using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

public sealed partial class FileAuthBundleStore {
    // The stable sidecar must not be removed: deleting it while a waiter has it open
    // would let later processes lock a different inode for the same store.
    private async Task<FileStream> AcquireTransactionAsync(CancellationToken cancellationToken) {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var elapsed = Stopwatch.StartNew();
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30)) {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Serializes refresh with other readers, writers, and refreshers across processes.
    /// Reuses credentials already replaced by another caller instead of replaying an old refresh token.
    /// A deleted account is never silently restored by a background refresh.
    /// </summary>
    internal async Task<AuthBundle> RefreshAsync(AuthBundle expected,
        Func<AuthBundle, CancellationToken, Task<AuthBundle>> refresh, CancellationToken cancellationToken) {
        using var transaction = await AcquireTransactionAsync(cancellationToken).ConfigureAwait(false);
        var file = await ReadFileAsync(cancellationToken).ConfigureAwait(false);
        var key = BuildKey(expected.Provider, expected.AccountId);
        if (file is null || !file.Bundles.TryGetValue(key, out var current)) {
            throw new InvalidOperationException("The saved account was removed. Recheck sign-in before refreshing.");
        }
        if (!string.Equals(current.RefreshToken, expected.RefreshToken, StringComparison.Ordinal)
            || !string.Equals(current.AccessToken, expected.AccessToken, StringComparison.Ordinal)) {
            return current;
        }
        var currentAccountId = current.AccountId ?? JwtDecoder.TryGetAccountId(current.AccessToken);
        var updated = await refresh(current, cancellationToken).ConfigureAwait(false);
        var updatedKey = BuildKey(updated.Provider, updated.AccountId);
        if (!string.Equals(updated.Provider, expected.Provider, StringComparison.OrdinalIgnoreCase)
            || (currentAccountId is not null && !string.Equals(updated.AccountId, currentAccountId, StringComparison.Ordinal))
            || (!string.Equals(updatedKey, key, StringComparison.OrdinalIgnoreCase) && file.Bundles.ContainsKey(updatedKey))) {
            throw new InvalidOperationException("Token refresh returned a different account identity.");
        }
        // Legacy entries may not have an account id until the provider reports one.
        file.Bundles.Remove(key);
        file.Bundles[updatedKey] = updated;
        // Once the provider returns rotated credentials, persist them even if the
        // caller canceled meanwhile. Cancellation must not discard a usable token.
        await WriteFileAsync(file, CancellationToken.None).ConfigureAwait(false);
        return updated;
    }
}

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>Coordinates credential read/renew/write operations independently of atomic file writes.</summary>
internal static class AuthStoreCoordination {
    private static readonly ConditionalWeakTable<IAuthBundleStore, SemaphoreSlim> InstanceGates = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileGates = new(
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<SemaphoreSlim, ConcurrentDictionary<string, CredentialEpoch>> Epochs = new();

    internal static CredentialEpoch GetEpoch(SemaphoreSlim gate, string provider) => Epochs.GetValue(gate,
        _ => new ConcurrentDictionary<string, CredentialEpoch>(StringComparer.OrdinalIgnoreCase))
        .GetOrAdd(provider, _ => new CredentialEpoch());

    internal static SemaphoreSlim GetGate(IAuthBundleStore? store) => store switch {
        FileAuthBundleStore file => FileGates.GetOrAdd(file.CoordinationPath, _ => new SemaphoreSlim(1, 1)),
        null => new SemaphoreSlim(1, 1),
        _ => InstanceGates.GetValue(store, _ => new SemaphoreSlim(1, 1))
    };

    /// <summary>Invalidates work started before account removal, including work in another owner using the same store.</summary>
    internal sealed class CredentialEpoch {
        private readonly object _sync = new();
        private readonly System.Collections.Generic.Dictionary<string, long> _accounts = new(StringComparer.OrdinalIgnoreCase);
        private long _version;
        private long _allAccounts;

        internal long Capture() { lock (_sync) return _version; }

        internal void Invalidate(string? accountId) {
            lock (_sync) {
                long version = ++_version;
                if (string.IsNullOrWhiteSpace(accountId)) _allAccounts = version;
                else _accounts[accountId!] = version;
            }
        }

        internal bool IsCurrent(long version, string? accountId) {
            lock (_sync) return version >= _allAccounts && (string.IsNullOrWhiteSpace(accountId)
                ? version >= _version : !_accounts.TryGetValue(accountId!, out long removed) || version >= removed);
        }
    }
}

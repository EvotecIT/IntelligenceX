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

    internal static SemaphoreSlim GetGate(IAuthBundleStore? store) => store switch {
        FileAuthBundleStore file => FileGates.GetOrAdd(file.CoordinationPath, _ => new SemaphoreSlim(1, 1)),
        null => new SemaphoreSlim(1, 1),
        _ => InstanceGates.GetValue(store, _ => new SemaphoreSlim(1, 1))
    };
}

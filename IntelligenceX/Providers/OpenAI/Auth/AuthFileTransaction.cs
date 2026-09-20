using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>Coordinates cooperating auth-file callers without replacing credential files or their permissions.</summary>
internal static class AuthFileTransaction {
    // Never remove a sidecar: a waiter may still hold the old inode on Unix.
    internal static async Task<FileStream> AcquireAsync(string lockPath, CancellationToken cancellationToken) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath))!);
        var elapsed = Stopwatch.StartNew();
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            } catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(30)) {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

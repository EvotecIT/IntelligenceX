using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App.Launch;

/// <summary>
/// Creates immutable, content-addressed runtime copies of the packaged chat service.
/// </summary>
internal sealed class ChatServiceRuntimeStager {
    private string? _stagedDirectory;
    private int _cleanupInFlight;

    /// <summary>
    /// Stages a service payload and returns its launch directory.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The source directory is missing.</exception>
    /// <exception cref="InvalidDataException">The source or staged directory has no service payload.</exception>
    public string Stage(string sourceDirectory) {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory)) {
            throw new DirectoryNotFoundException("The chat service source directory was not found.");
        }
        if (!ChatServiceRuntimeLocator.HasServicePayload(sourceDirectory)) {
            throw new InvalidDataException("The chat service source directory contains no launchable payload.");
        }

        var runtimeRoot = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "service-runtime");
        var stageKey = BuildStageKey(sourceDirectory);
        var stagedDirectory = Path.Combine(runtimeRoot, stageKey);

        if (PathsEqual(_stagedDirectory, stagedDirectory)
            && ChatServiceRuntimeLocator.HasServicePayload(_stagedDirectory)) {
            TouchDirectory(_stagedDirectory);
            return _stagedDirectory!;
        }

        Directory.CreateDirectory(runtimeRoot);
        if (!ChatServiceRuntimeLocator.HasServicePayload(stagedDirectory)) {
            var temporaryDirectory = stagedDirectory + ".tmp-" + Guid.NewGuid().ToString("N");
            try {
                CopyDirectory(sourceDirectory, temporaryDirectory);
                if (!Directory.Exists(stagedDirectory)) {
                    Directory.Move(temporaryDirectory, stagedDirectory);
                }
            } finally {
                TryDeleteDirectory(temporaryDirectory);
            }
        }

        if (!ChatServiceRuntimeLocator.HasServicePayload(stagedDirectory)) {
            throw new InvalidDataException("The staged chat service directory contains no launchable payload.");
        }

        _stagedDirectory = stagedDirectory;
        TouchDirectory(stagedDirectory);
        QueueStaleCleanup(runtimeRoot, stagedDirectory);
        return stagedDirectory;
    }

    /// <summary>
    /// Releases the current staging association without deleting shared staged payloads.
    /// </summary>
    public void Reset() => _stagedDirectory = null;

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory) {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)) {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var targetPath = Path.Combine(destinationDirectory, relativePath);
            var targetParent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetParent)) {
                Directory.CreateDirectory(targetParent);
            }
            File.Copy(file, targetPath, overwrite: true);
        }
    }

    /// <summary>
    /// Builds a deterministic cache key from the full service payload shape.
    /// </summary>
    internal static string BuildStageKey(string sourceDirectory) {
        var normalizedSourceDirectory = Path.GetFullPath(sourceDirectory);
        var fingerprint = new StringBuilder(normalizedSourceDirectory.Length + 256);
        fingerprint.Append(normalizedSourceDirectory.ToUpperInvariant());

        try {
            foreach (var file in Directory.EnumerateFiles(normalizedSourceDirectory, "*", SearchOption.AllDirectories)
                         .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)) {
                var relativePath = Path.GetRelativePath(normalizedSourceDirectory, file);
                var info = new FileInfo(file);
                fingerprint.Append('|');
                fingerprint.Append(relativePath.Replace(Path.DirectorySeparatorChar, '/').ToUpperInvariant());
                fingerprint.Append('|');
                fingerprint.Append(info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0");
                fingerprint.Append('|');
                fingerprint.Append(info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0");
            }
        } catch {
            // Fall back to the normalized root path when enumeration is unavailable.
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint.ToString()));
        return "v1-" + Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static bool PathsEqual(string? left, string right) {
        if (string.IsNullOrWhiteSpace(left)) {
            return false;
        }
        try {
            var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }

    private static void TouchDirectory(string? directory) {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) {
            return;
        }
        try {
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
        } catch {
            // Staging remains usable when the access-time hint cannot be updated.
        }
    }

    private void QueueStaleCleanup(string runtimeRoot, string keepDirectory) {
        if (Interlocked.CompareExchange(ref _cleanupInFlight, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(() => {
            try {
                CleanupStaleDirectories(runtimeRoot, keepDirectory);
            } finally {
                Interlocked.Exchange(ref _cleanupInFlight, 0);
            }
        });
    }

    private static void CleanupStaleDirectories(string runtimeRoot, string keepDirectory) {
        try {
            if (!Directory.Exists(runtimeRoot)) {
                return;
            }

            var keepPath = Path.GetFullPath(keepDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directories = new List<DirectoryInfo>(new DirectoryInfo(runtimeRoot).EnumerateDirectories());
            directories.Sort(static (left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

            var retained = 0;
            foreach (var directory in directories) {
                var fullPath = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (directory.Name.Contains(".tmp-", StringComparison.OrdinalIgnoreCase)) {
                    if ((DateTime.UtcNow - directory.LastWriteTimeUtc) > TimeSpan.FromMinutes(10)) {
                        TryDeleteDirectory(fullPath);
                    }
                    continue;
                }

                if (string.Equals(fullPath, keepPath, StringComparison.OrdinalIgnoreCase) || retained < 3) {
                    retained++;
                    continue;
                }

                TryDeleteDirectory(fullPath);
            }
        } catch {
            // Cleanup is best effort and must never prevent the service from launching.
        }
    }

    private static void TryDeleteDirectory(string directory) {
        try {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        } catch {
            // Cleanup is best effort.
        }
    }
}

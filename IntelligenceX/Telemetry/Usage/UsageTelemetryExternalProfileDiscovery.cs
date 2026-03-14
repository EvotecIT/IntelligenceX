using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IntelligenceX.Telemetry.Usage;

internal sealed class UsageTelemetryExternalProfileDiscovery {
    private const int WslListTimeoutMs = 1500;

    private readonly Func<IReadOnlyList<UsageTelemetryExternalProfile>>? _profilesFactory;

    public static UsageTelemetryExternalProfileDiscovery Default { get; } = new();

    internal UsageTelemetryExternalProfileDiscovery(Func<IReadOnlyList<UsageTelemetryExternalProfile>>? profilesFactory = null) {
        _profilesFactory = profilesFactory;
    }

    public IReadOnlyList<UsageTelemetryExternalProfile> DiscoverProfiles() {
        var profiles = _profilesFactory?.Invoke() ?? DiscoverDefaultProfiles();
        return (profiles ?? Array.Empty<UsageTelemetryExternalProfile>())
            .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.ProfilePath))
            .GroupBy(
                static profile => profile.SourceKind + "|" + UsageTelemetryIdentity.NormalizePath(profile.ProfilePath),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<UsageTelemetryExternalProfile> DiscoverDefaultProfiles() {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            return Array.Empty<UsageTelemetryExternalProfile>();
        }

        var profiles = new List<UsageTelemetryExternalProfile>();
        AddWindowsOldProfiles(profiles);
        AddWslProfiles(profiles);
        return profiles;
    }

    private static void AddWindowsOldProfiles(ICollection<UsageTelemetryExternalProfile> profiles) {
        foreach (var drive in DriveInfo.GetDrives()) {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) {
                continue;
            }

            var usersRoot = Path.Combine(drive.RootDirectory.FullName, "Windows.old", "Users");
            foreach (var profilePath in EnumerateDirectories(usersRoot)) {
                profiles.Add(new UsageTelemetryExternalProfile(
                    UsageSourceKind.RecoveredFolder,
                    profilePath,
                    PlatformHint: "windows-old"));
            }
        }
    }

    private static void AddWslProfiles(ICollection<UsageTelemetryExternalProfile> profiles) {
        foreach (var distro in EnumerateWslDistros()) {
            var shareRoot = ResolveWslShareRoot(distro);
            if (string.IsNullOrWhiteSpace(shareRoot)) {
                continue;
            }

            foreach (var homePath in EnumerateDirectories(Path.Combine(shareRoot, "home"))) {
                profiles.Add(new UsageTelemetryExternalProfile(
                    UsageSourceKind.LocalLogs,
                    homePath,
                    PlatformHint: "wsl",
                    MachineLabel: distro));
            }

            var rootProfile = Path.Combine(shareRoot, "root");
            if (Directory.Exists(rootProfile)) {
                profiles.Add(new UsageTelemetryExternalProfile(
                    UsageSourceKind.LocalLogs,
                    rootProfile,
                    PlatformHint: "wsl",
                    MachineLabel: distro));
            }
        }
    }

    private static IEnumerable<string> EnumerateWslDistros() {
        Process? process = null;
        try {
            process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "wsl.exe",
                    Arguments = "--list --quiet",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            if (!process.Start()) {
                return Array.Empty<string>();
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(WslListTimeoutMs)) {
                try {
                    process.Kill();
                } catch {
                    // Best-effort timeout cleanup only.
                }

                return Array.Empty<string>();
            }

            if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output)) {
                return Array.Empty<string>();
            }

            return (output + Environment.NewLine + error)
                .Replace("\0", string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(static line => line.Trim().Trim('\uFEFF'))
                .Where(static line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch {
            return Array.Empty<string>();
        } finally {
            process?.Dispose();
        }
    }

    private static string? ResolveWslShareRoot(string distro) {
        if (string.IsNullOrWhiteSpace(distro)) {
            return null;
        }

        var candidates = new[] {
            @"\\wsl$\" + distro.Trim(),
            @"\\wsl.localhost\" + distro.Trim()
        };

        foreach (var candidate in candidates) {
            if (Directory.Exists(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDirectories(string path) {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) {
            return Array.Empty<string>();
        }

        try {
            return Directory
                .EnumerateDirectories(path)
                .Select(UsageTelemetryIdentity.NormalizePath)
                .ToArray();
        } catch {
            return Array.Empty<string>();
        }
    }
}

internal sealed record UsageTelemetryExternalProfile(
    UsageSourceKind SourceKind,
    string ProfilePath,
    string? PlatformHint = null,
    string? MachineLabel = null);

internal sealed record UsageTelemetryDiscoveredRootCandidate(
    UsageSourceKind SourceKind,
    string Path,
    string? PlatformHint = null,
    string? MachineLabel = null);

internal static class UsageTelemetryRootDiscoverySupport {
    public static IReadOnlyList<SourceRootRecord> BuildRoots(
        string providerId,
        IEnumerable<UsageTelemetryDiscoveredRootCandidate> candidates) {
        return (candidates ?? Array.Empty<UsageTelemetryDiscoveredRootCandidate>())
            .Where(static candidate => candidate is not null && !string.IsNullOrWhiteSpace(candidate.Path))
            .Select(static candidate => new {
                Candidate = candidate,
                NormalizedPath = UsageTelemetryIdentity.NormalizePath(candidate.Path)
            })
            .Where(static entry => Directory.Exists(entry.NormalizedPath))
            .GroupBy(
                static entry => entry.Candidate.SourceKind + "|" + entry.NormalizedPath,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var entry = group.First();
                return new SourceRootRecord(
                    SourceRootRecord.CreateStableId(providerId, entry.Candidate.SourceKind, entry.NormalizedPath),
                    providerId,
                    entry.Candidate.SourceKind,
                    entry.NormalizedPath) {
                    PlatformHint = NormalizeOptional(entry.Candidate.PlatformHint),
                    MachineLabel = NormalizeOptional(entry.Candidate.MachineLabel)
                };
            })
            .ToArray();
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

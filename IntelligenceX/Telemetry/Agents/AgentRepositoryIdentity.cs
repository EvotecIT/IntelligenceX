using System;
using System.IO;
using System.Runtime.InteropServices;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Agents;

internal static class AgentRepositoryIdentity {
    internal static bool CanResolveWorkspace(SourceRootRecord root) => root.SourceKind == UsageSourceKind.LocalLogs
        && (root.MachineLabel is null || string.Equals(root.MachineLabel, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        && !(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && string.Equals(root.PlatformHint, "wsl", StringComparison.OrdinalIgnoreCase));

    internal static string? FromRemote(string? remote) {
        if (string.IsNullOrWhiteSpace(remote)) return null;
        string path;
        if (remote!.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) {
            path = remote.Substring("git@github.com:".Length);
        } else if (Uri.TryCreate(remote, UriKind.Absolute, out var uri) &&
                   string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) {
            path = uri.AbsolutePath.Trim('/');
        } else {
            return null;
        }
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
        var parts = path.Split('/');
        return parts.Length == 2 && ValidPart(parts[0]) && ValidPart(parts[1]) ? path : null;
    }

    private static bool ValidPart(string value) {
        if (value.Length == 0 || value == "." || value == "..") return false;
        foreach (var c in value) {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.') return false;
        }
        return true;
    }

    internal static AgentSessionState Enrich(AgentSessionState session) {
        if (string.IsNullOrEmpty(session.WorkingDirectory) || (session.Repository is not null && session.Branch is not null)) return session;
        try {
            var directory = new DirectoryInfo(session.WorkingDirectory);
            while (directory is not null) {
                var marker = Path.Combine(directory.FullName, ".git");
                var gitDirectory = Directory.Exists(marker) ? marker : null;
                if (gitDirectory is null && File.Exists(marker)) {
                    var value = ReadSmallFile(marker)?.Trim();
                    if (value?.StartsWith("gitdir: ", StringComparison.Ordinal) == true) {
                        gitDirectory = Path.GetFullPath(Path.Combine(directory.FullName, value.Substring(8).Trim()));
                    }
                }
                if (gitDirectory is not null) {
                    var head = ReadSmallFile(Path.Combine(gitDirectory, "HEAD"))?.Trim();
                    var commonDirectory = ReadSmallFile(Path.Combine(gitDirectory, "commondir"))?.Trim();
                    var configDirectory = commonDirectory is null ? gitDirectory : Path.GetFullPath(Path.Combine(gitDirectory, commonDirectory));
                    var config = ReadSmallFile(Path.Combine(configDirectory, "config"));
                    var origin = false;
                    string? repository = null;
                    foreach (var raw in (config ?? string.Empty).Split('\n')) {
                        var line = raw.Trim();
                        if (line.StartsWith("[", StringComparison.Ordinal)) origin = line.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                        var equals = line.IndexOf('=');
                        if (origin && equals > 0 && line.Substring(0, equals).Trim().Equals("url", StringComparison.OrdinalIgnoreCase)) {
                            repository = FromRemote(line.Substring(equals + 1).Trim());
                        }
                    }
                    return session with {
                        Repository = session.Repository ?? repository,
                        Branch = session.Branch ?? (head?.StartsWith("ref: refs/heads/", StringComparison.Ordinal) == true ? head.Substring(16) : null)
                    };
                }
                directory = directory.Parent;
            }
        } catch (IOException) {
        } catch (UnauthorizedAccessException) {
        } catch (ArgumentException) {
        }
        return session;
    }

    private static string? ReadSmallFile(string path) => File.Exists(path) && new FileInfo(path).Length <= 1024 * 1024
        ? File.ReadAllText(path) : null;
}

namespace IntelligenceX.Chat.Abstractions.Storage;

/// <summary>
/// Resolves the single per-user directory used by IntelligenceX Chat state across desktop and service surfaces.
/// </summary>
public static class ChatStatePaths {
    private const string ApplicationDirectoryName = "IntelligenceX.Chat";

    /// <summary>
    /// Resolves the durable per-user state directory.
    /// </summary>
    /// <exception cref="InvalidOperationException">No secure per-user data root is available.</exception>
    public static string GetDefaultDirectory() {
        return ResolveDefaultDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>
    /// Resolves a file beneath the durable per-user state directory.
    /// </summary>
    public static string GetDefaultPath(string fileName) {
        return ResolveDefaultPath(
            fileName,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>
    /// Determines whether a file resolves inside the durable per-user state directory.
    /// </summary>
    public static bool IsPathInDefaultDirectory(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        try {
            var directory = GetDefaultDirectory();
            return IsPathWithinDirectory(directory, path)
                   && HasNoReparsePointsBelowDirectory(directory, path);
        } catch {
            return false;
        }
    }

    internal static string ResolveDefaultPath(
        string fileName,
        string? localApplicationData,
        string? xdgDataHome,
        string? userProfile) {
        var normalizedFileName = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (normalizedFileName.Length == 0
            || normalizedFileName is "." or "..") {
            throw new ArgumentException("A state file name is required.", nameof(fileName));
        }

        var directory = ResolveDefaultDirectory(localApplicationData, xdgDataHome, userProfile);
        var path = Path.GetFullPath(Path.Combine(directory, normalizedFileName));
        var parent = Path.GetDirectoryName(path);
        if (!string.Equals(parent, directory, PathComparison)) {
            throw new ArgumentException("The state file must resolve directly beneath the shared state directory.", nameof(fileName));
        }
        if (!HasNoReparsePointsBelowDirectory(directory, path)) {
            throw new InvalidOperationException("The state file path cannot traverse a symbolic link or reparse point.");
        }

        return path;
    }

    internal static string ResolveDefaultDirectory(
        string? localApplicationData,
        string? xdgDataHome,
        string? userProfile) {
        if (TryNormalizeAbsoluteRoot(localApplicationData, out var root)
            || TryNormalizeAbsoluteRoot(xdgDataHome, out root)) {
            return Path.Combine(root, ApplicationDirectoryName);
        }

        if (TryNormalizeAbsoluteRoot(userProfile, out root)) {
            return Path.Combine(root, ".local", "share", ApplicationDirectoryName);
        }

        throw new InvalidOperationException(
            "IntelligenceX Chat cannot resolve a durable per-user state directory. Configure a user profile or an absolute XDG_DATA_HOME path.");
    }

    internal static bool IsPathWithinDirectory(string directory, string path) {
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullDirectory, PathComparison);
    }

    internal static bool HasNoReparsePointsBelowDirectory(string directory, string path) {
        var fullDirectory = Path.GetFullPath(directory);
        var directoryPrefix = fullDirectory
                                  .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(directoryPrefix, PathComparison)) {
            return false;
        }

        var relativePath = fullPath[directoryPrefix.Length..];
        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var currentPath = fullDirectory;
        foreach (var segment in segments) {
            currentPath = Path.Combine(currentPath, segment);
            FileAttributes attributes;
            try {
                attributes = File.GetAttributes(currentPath);
            } catch (FileNotFoundException) {
                break;
            } catch (DirectoryNotFoundException) {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0) {
                return false;
            }
        }

        return true;
    }

    private static bool TryNormalizeAbsoluteRoot(string? candidate, out string root) {
        root = string.Empty;
        var normalized = (candidate ?? string.Empty).Trim();
        if (normalized.Length == 0 || !Path.IsPathFullyQualified(normalized)) {
            return false;
        }

        try {
            root = Path.GetFullPath(normalized);
            return root.Length > 0;
        } catch {
            root = string.Empty;
            return false;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

using System;
using System.IO;

namespace IntelligenceX.Cli.Ci;

internal static class CiPathSafety {
    private static bool IsLinkOrReparsePoint(string path) {
        try {
            FileSystemInfo fsi;
            if (Directory.Exists(path)) {
                fsi = new DirectoryInfo(path);
            } else {
                fsi = new FileInfo(path);
            }
            if (!string.IsNullOrWhiteSpace(fsi.LinkTarget)) {
                return true;
            }
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        } catch {
            return false;
        }
    }

    internal static bool IsUnderRoot(string path, string root) {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) {
            return false;
        }
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        var trimmedPath = Path.TrimEndingDirectorySeparator(fullPath);
        var trimmedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Consider the root directory itself as "under root" for containment checks.
        if (string.Equals(trimmedPath, trimmedRoot, comparison)) {
            return true;
        }

        var normalizedRoot = trimmedRoot + Path.DirectorySeparatorChar;
        return trimmedPath.StartsWith(normalizedRoot, comparison);
    }

	    internal static bool IsUnderRootPhysical(string path, string root) {
	        if (!IsUnderRoot(path, root)) {
	            return false;
	        }
	        try {
	            var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
	            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
	            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
	
	            if (Directory.Exists(normalizedRoot)) {
	                if (IsLinkOrReparsePoint(normalizedRoot)) {
	                    return false;
	                }
	            } else {
	                return false;
	            }

            // Check each existing directory segment under the root for symlinks/junctions.
            // This is best-effort and reduces the risk of writing outside the workspace via reparse points.
	            var dirToCheck = Directory.Exists(normalizedPath)
	                ? normalizedPath
	                : (Path.GetDirectoryName(normalizedPath) ?? normalizedRoot);
	            dirToCheck = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirToCheck));
	            while (!string.IsNullOrWhiteSpace(dirToCheck) &&
	                   !Directory.Exists(dirToCheck) &&
	                   !string.Equals(dirToCheck, normalizedRoot, comparison)) {
	                var parent = Path.GetDirectoryName(dirToCheck);
	                if (string.IsNullOrWhiteSpace(parent)) {
	                    break;
	                }
	                dirToCheck = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
	            }

	            var relative = Path.GetRelativePath(normalizedRoot, dirToCheck);
	            if (string.Equals(relative, ".", StringComparison.Ordinal)) {
	                relative = string.Empty;
	            }
	            if (relative.StartsWith("..", comparison)) {
	                return false;
	            }

	            var current = normalizedRoot;
	            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
	                if (string.Equals(segment, ".", StringComparison.Ordinal)) {
	                    continue;
	                }
	                current = Path.Combine(current, segment);
	                if (File.Exists(current)) {
	                    return false;
	                }
	                if (!Directory.Exists(current)) {
	                    return false;
	                }
	                if (IsLinkOrReparsePoint(current)) {
	                    return false;
	                }
	            }

	            var leafExists = File.Exists(normalizedPath) || Directory.Exists(normalizedPath);
	            if (!leafExists) {
	                return true;
	            }
	            // If the leaf exists (file or directory), ensure it isn't itself a symlink/junction/reparse point.
	            if (IsLinkOrReparsePoint(normalizedPath)) {
	                return false;
	            }

	            return true;
	        } catch {
	            return false;
	        }
    }

	    internal static bool TryEnsureSafeDirectory(string directoryPath, string root, out string error) {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(root)) {
            error = "Invalid directory path.";
            return false;
        }
        try {
            var fullDir = Path.GetFullPath(directoryPath);
            var fullRoot = Path.GetFullPath(root);
            if (!IsUnderRoot(fullDir, fullRoot)) {
                error = $"Directory must be within the workspace. dir={fullDir} workspace={fullRoot}";
                return false;
            }

	            var trimmedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
	            if (Directory.Exists(trimmedRoot)) {
	                if (IsLinkOrReparsePoint(trimmedRoot)) {
	                    error = "Workspace root is a symlink/junction (reparse point).";
	                    return false;
	                }
	            } else {
	                error = "Workspace root not found.";
	                return false;
	            }

            var relative = Path.GetRelativePath(trimmedRoot, fullDir);
            if (relative.StartsWith("..", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
                error = $"Directory must be within the workspace. dir={fullDir} workspace={trimmedRoot}";
                return false;
            }

            var current = trimmedRoot;
            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
                current = Path.Combine(current, segment);
                if (File.Exists(current)) {
                    error = $"Path segment is a file, not a directory: {current}";
                    return false;
                }
	                if (!Directory.Exists(current)) {
	                    Directory.CreateDirectory(current);
	                }
	                if (IsLinkOrReparsePoint(current)) {
	                    error = $"Path contains a symlink/junction component: {current}";
	                    return false;
	                }
	            }

            return true;
        } catch (Exception ex) {
            error = ex.Message;
            return false;
        }
    }
}

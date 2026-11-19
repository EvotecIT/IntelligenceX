namespace IntelligenceX.Ledger.Tests;

internal static class LedgerTestPaths {
    // The OS can return a temporary directory through an alias such as macOS /var.
    // Enroll plain fixture paths without relaxing the catalog's linked-source policy.
    internal static string NewDirectory(string name) {
        var temporary = Path.GetFullPath(Path.GetTempPath());
        var root = Path.GetPathRoot(temporary)!;
        var physical = root;
        foreach (var component in temporary[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)) {
            var directory = new DirectoryInfo(Path.Combine(physical, component));
            physical = (directory.Attributes & FileAttributes.ReparsePoint) != 0
                ? directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("The temporary directory alias cannot be resolved.")
                : directory.FullName;
        }
        return Path.Combine(physical, "ix-ledger-" + name + "-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Deletes a served test's directory after its process was killed. A killed service leaves its SQLite WAL and
    /// shared-memory files, and Windows can still hold them for a moment after the process has exited.
    /// </summary>
    internal static async Task DeleteServedDirectoryAsync(string directory) {
        for (var attempt = 1; ; attempt++) {
            try { Directory.Delete(directory, recursive: true); return; }
            catch (Exception error) when (attempt < 20 && error is IOException or UnauthorizedAccessException) { await Task.Delay(250); }
        }
    }
}

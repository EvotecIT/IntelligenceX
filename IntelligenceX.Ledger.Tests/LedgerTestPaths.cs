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
}

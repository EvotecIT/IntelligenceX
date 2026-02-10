using System;
using System.IO;
using IntelligenceX.Utils;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class PathSafetyTests {
    [Fact]
    public void EnsureUnderRoot_BlocksSymlinkTraversal_WhenSupported() {
        var root = Path.Combine(Path.GetTempPath(), $"ix-workspace-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"ix-outside-{Guid.NewGuid():N}");
        try {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);

            var outsideFile = Path.Combine(outside, "outside.txt");
            File.WriteAllText(outsideFile, "x");

            var linkDir = Path.Combine(root, "link");
            try {
                Directory.CreateSymbolicLink(linkDir, outside);
            } catch {
                // Symlink creation can be restricted (notably on some Windows environments).
                // Treat as non-actionable.
                return;
            }

            var escapedPath = Path.Combine(linkDir, "outside.txt");
            Assert.True(File.Exists(escapedPath));
            Assert.Throws<InvalidOperationException>(() => PathSafety.EnsureUnderRoot(escapedPath, root));
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
            try {
                if (Directory.Exists(outside)) {
                    Directory.Delete(outside, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }
}

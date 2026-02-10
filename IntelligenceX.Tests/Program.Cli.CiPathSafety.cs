#if !NET472
using System;
using System.IO;
using IntelligenceX.Cli.Ci;
#endif

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestCiPathSafetyUnderRootPhysicalAllowsNonexistentLeaf() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var newDir = Path.Combine(root, "artifacts");
            AssertEqual(false, Directory.Exists(newDir), "artifacts directory does not exist");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(newDir, root), "IsUnderRootPhysical allows non-existent leaf");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void TestCiChangedFilesWritesIntoNewDirectory() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-changed-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var outputPath = Path.Combine(root, "artifacts", "changed-files.txt");
            AssertEqual(false, File.Exists(outputPath), "changed-files output does not exist");

            var exit = CiChangedFilesCommand.RunAsync(new[] { "--workspace", root, "--out", "artifacts/changed-files.txt" })
                .GetAwaiter().GetResult();
            AssertEqual(0, exit, "changed-files exit code");
            AssertEqual(true, File.Exists(outputPath), "changed-files output exists");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
#endif
}


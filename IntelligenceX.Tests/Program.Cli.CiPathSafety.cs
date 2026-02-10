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

    private static void TestCiPathSafetyUnderRootPhysicalTrailingSeparators() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-path-seps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var artifacts = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(artifacts);

            var sep = Path.DirectorySeparatorChar.ToString();
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts, root), "physical under root baseline");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts + sep, root), "path trailing sep");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts, root + sep), "root trailing sep");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(artifacts + sep, root + sep), "both trailing sep");
            AssertEqual(true, CiPathSafety.IsUnderRootPhysical(root + sep, root), "root self trailing sep");
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

    private static void TestCiChangedFilesStrictFailsWhenDiffFailsEvenIfFallbackSucceeds() {
        var root = Path.Combine(Path.GetTempPath(), "ix-ci-changed-files-strict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            File.WriteAllText(Path.Combine(root, "a.txt"), "x\n");
            var (initExit, _, _) = GitCli.RunAsync(root, "init").GetAwaiter().GetResult();
            AssertEqual(0, initExit, "git init exit");

            var (addExit, _, _) = GitCli.RunAsync(root, "add", "a.txt").GetAwaiter().GetResult();
            AssertEqual(0, addExit, "git add exit");

            var exit = CiChangedFilesCommand.RunAsync(new[] {
                    "--workspace", root,
                    "--out", "artifacts/changed-files.txt",
                    "--base", "deadbeef",
                    "--strict"
                })
                .GetAwaiter().GetResult();
            AssertEqual(1, exit, "changed-files strict exit code");

            var outputPath = Path.Combine(root, "artifacts", "changed-files.txt");
            AssertEqual(true, File.Exists(outputPath), "changed-files strict output exists");
        } finally {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
#endif
}

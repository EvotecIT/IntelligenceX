using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Engines.FileSystem;
using IntelligenceX.Engines.PowerShell;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EngineQueryBehaviorTests {
    [Fact]
    public void FileSystemList_NonRecursive_DirectoryEntriesAreUnique() {
        var root = Path.Combine(Path.GetTempPath(), "ix-fs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));

        try {
            var result = FileSystemQuery.List(new FileSystemListRequest {
                Path = root,
                Recursive = false,
                IncludeDirectories = true,
                IncludeFiles = false,
                MaxResults = 100
            });

            var dirs = result.Entries
                .Where(static entry => string.Equals(entry.Type, "dir", StringComparison.OrdinalIgnoreCase))
                .Select(static entry => entry.Path)
                .ToArray();

            Assert.Equal(2, dirs.Length);
            Assert.Equal(dirs.Length, dirs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        } finally {
            try {
                Directory.Delete(root, recursive: true);
            } catch {
                // Ignore cleanup failure.
            }
        }
    }

    [Fact]
    public async Task PowerShellExecuteAsync_ReturnsWithoutBlockingCaller() {
        if (PowerShellCommandQueryExecutor.GetAvailableHosts().Count == 0) {
            return;
        }

        var request = new PowerShellCommandQueryRequest {
            Script = "Start-Sleep -Milliseconds 1500; 'done'",
            TimeoutMs = 5_000,
            MaxOutputChars = 4_000,
            IncludeErrorStream = true
        };

        var task = PowerShellCommandQueryExecutor.ExecuteAsync(request);
        Assert.False(task.IsCompleted, "ExecuteAsync should return a pending task for long-running commands.");

        var result = await task;
        Assert.Contains("done", result.Output, StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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

            Assert.Equal(3, dirs.Length);
            Assert.Equal(Path.GetFullPath(root), dirs[0]);
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
    public void FileSystemList_Recursive_DirectoryEntriesRespectMaxResults() {
        var root = Path.Combine(Path.GetTempPath(), "ix-fs-test-" + Guid.NewGuid().ToString("N"));
        var childA = Path.Combine(root, "a");
        var childB = Path.Combine(root, "b");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(childA);
        Directory.CreateDirectory(childB);

        try {
            var result = FileSystemQuery.List(new FileSystemListRequest {
                Path = root,
                Recursive = true,
                IncludeDirectories = true,
                IncludeFiles = false,
                MaxResults = 2
            });

            var dirs = result.Entries
                .Where(static entry => string.Equals(entry.Type, "dir", StringComparison.OrdinalIgnoreCase))
                .Select(static entry => entry.Path)
                .ToArray();

            Assert.True(result.Truncated);
            Assert.Equal(2, result.Count);
            Assert.Equal(2, dirs.Length);
            Assert.Equal(Path.GetFullPath(root), dirs[0]);
            Assert.Contains(dirs[1], new[] { childA, childB });
        } finally {
            try {
                Directory.Delete(root, recursive: true);
            } catch {
                // Ignore cleanup failure.
            }
        }
    }

    [Fact]
    public void FileSystemList_Recursive_DirectoryEntriesAreUniqueAcrossTraversal() {
        var root = Path.Combine(Path.GetTempPath(), "ix-fs-test-" + Guid.NewGuid().ToString("N"));
        var childA = Path.Combine(root, "a");
        var childB = Path.Combine(root, "b");
        var childA1 = Path.Combine(childA, "a1");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(childA);
        Directory.CreateDirectory(childB);
        Directory.CreateDirectory(childA1);

        try {
            var result = FileSystemQuery.List(new FileSystemListRequest {
                Path = root,
                Recursive = true,
                IncludeDirectories = true,
                IncludeFiles = false,
                MaxResults = 100
            });

            var dirs = result.Entries
                .Where(static entry => string.Equals(entry.Type, "dir", StringComparison.OrdinalIgnoreCase))
                .Select(static entry => entry.Path)
                .ToArray();

            Assert.Equal(dirs.Length, dirs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(Path.GetFullPath(root), dirs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(childA, dirs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(childB, dirs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(childA1, dirs, StringComparer.OrdinalIgnoreCase);
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

    [Fact]
    public async Task PowerShellTryExecuteAsync_CanceledTokenIsNotReportedAsTimeout() {
        if (PowerShellCommandQueryExecutor.GetAvailableHosts().Count == 0) {
            return;
        }

        var request = new PowerShellCommandQueryRequest {
            Script = "Start-Sleep -Seconds 10; 'done'",
            TimeoutMs = 10_000,
            MaxOutputChars = 2_000,
            IncludeErrorStream = true
        };

        using var cts = new CancellationTokenSource(millisecondsDelay: 150);
        var result = await PowerShellCommandQueryExecutor.TryExecuteAsync(request, cts.Token);

        Assert.False(result.Success);
        Assert.NotNull(result.Failure);
        Assert.Equal(PowerShellCommandQueryFailureCode.Cancelled, result.Failure!.Code);
    }

    [Fact]
    public void FileSystemReadText_TruncatedUtf8_DoesNotReturnReplacementCharacter() {
        var root = Path.Combine(Path.GetTempPath(), "ix-fs-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "utf8.txt");
        File.WriteAllText(filePath, "abc😀def", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try {
            var result = FileSystemQuery.ReadText(new FileTextReadRequest {
                Path = filePath,
                MaxBytes = 6
            });

            Assert.True(result.Truncated);
            Assert.Equal(6, result.BytesRead);
            Assert.DoesNotContain('\uFFFD', result.Text);
            Assert.Equal("abc", result.Text);
        } finally {
            try {
                Directory.Delete(root, recursive: true);
            } catch {
                // Ignore cleanup failure.
            }
        }
    }
}

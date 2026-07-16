using System;
using System.IO;
using IntelligenceX.Chat.App.Launch;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Protects the shared desktop service discovery and staging contracts.
/// </summary>
public sealed class ChatServiceRuntimeHostingTests {
    /// <summary>
    /// Ensures discovery selects the newest valid packaged service near the app.
    /// </summary>
    [Fact]
    public void ResolveSourceDirectory_SelectsNewestValidPayload() {
        var root = CreateTemporaryDirectory();
        try {
            var appDirectory = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            var appServiceDirectory = Directory.CreateDirectory(Path.Combine(appDirectory, "service")).FullName;
            var siblingServiceDirectory = Directory.CreateDirectory(Path.Combine(root, "service")).FullName;
            var olderPayload = Path.Combine(appServiceDirectory, "IntelligenceX.Chat.Service.dll");
            var newerPayload = Path.Combine(siblingServiceDirectory, "IntelligenceX.Chat.Service.dll");
            File.WriteAllText(olderPayload, "older");
            File.WriteAllText(newerPayload, "newer");
            File.SetLastWriteTimeUtc(olderPayload, DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(newerPayload, DateTime.UtcNow);

            var resolved = ChatServiceRuntimeLocator.ResolveSourceDirectory(appDirectory);

            Assert.Equal(Path.GetFullPath(siblingServiceDirectory), resolved);
        } finally {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>
    /// Ensures plugin path resolution never emits relative paths for root source directories.
    /// </summary>
    [Fact]
    public void ResolvePluginPaths_RootSourceDirectory_DoesNotEmitRelativePaths() {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        Assert.False(string.IsNullOrWhiteSpace(root));

        var paths = ChatServiceRuntimeLocator.ResolvePluginPaths(root!);

        Assert.All(paths, static path => Assert.True(Path.IsPathRooted(path)));
        Assert.DoesNotContain(paths, static path => string.Equals(path.Trim(), "plugins", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures built-in tool probe resolution never emits relative paths for root source directories.
    /// </summary>
    [Fact]
    public void ResolveBuiltInToolProbePaths_RootSourceDirectory_DoesNotEmitRelativePaths() {
        var root = Path.GetPathRoot(AppContext.BaseDirectory);
        Assert.False(string.IsNullOrWhiteSpace(root));

        var paths = ChatServiceRuntimeLocator.ResolveBuiltInToolProbePaths(root!);

        Assert.All(paths, static path => Assert.True(Path.IsPathRooted(path)));
        Assert.DoesNotContain(paths, static path => string.Equals(path.Trim(), "tools", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures workspace output probing is used only when no explicit packaged probe paths exist.
    /// </summary>
    [Fact]
    public void ShouldEnableWorkspaceBuiltInToolOutputProbing_DependsOnExplicitProbePaths() {
        Assert.False(ChatServiceRuntimeLocator.ShouldEnableWorkspaceBuiltInToolOutputProbing(
            new[] { @"C:\service", @"C:\service\tools" }));
        Assert.True(ChatServiceRuntimeLocator.ShouldEnableWorkspaceBuiltInToolOutputProbing(Array.Empty<string>()));
        Assert.True(ChatServiceRuntimeLocator.ShouldEnableWorkspaceBuiltInToolOutputProbing(null));
    }

    /// <summary>
    /// Ensures staging copies a complete payload and reuses its content-addressed directory.
    /// </summary>
    [Fact]
    public void Stage_CopiesPayloadAndReusesContentAddressedDirectory() {
        var sourceRoot = CreateTemporaryDirectory();
        string? stagedDirectory = null;
        try {
            File.WriteAllText(Path.Combine(sourceRoot, "IntelligenceX.Chat.Service.dll"), "service");
            var assetsDirectory = Directory.CreateDirectory(Path.Combine(sourceRoot, "assets")).FullName;
            File.WriteAllText(Path.Combine(assetsDirectory, "catalog.json"), "{}");
            var stager = new ChatServiceRuntimeStager();

            stagedDirectory = stager.Stage(sourceRoot);
            var secondStage = stager.Stage(sourceRoot);

            Assert.Equal(stagedDirectory, secondStage);
            Assert.True(File.Exists(Path.Combine(stagedDirectory, "IntelligenceX.Chat.Service.dll")));
            Assert.True(File.Exists(Path.Combine(stagedDirectory, "assets", "catalog.json")));
        } finally {
            TryDeleteDirectory(sourceRoot);
            if (!string.IsNullOrWhiteSpace(stagedDirectory)) {
                TryDeleteDirectory(stagedDirectory);
            }
        }
    }

    private static string CreateTemporaryDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string directory) {
        try {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        } catch {
            // Test cleanup is best effort on Windows when background file scanners hold handles briefly.
        }
    }
}

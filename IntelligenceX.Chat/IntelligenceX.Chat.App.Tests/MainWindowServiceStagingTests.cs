using System;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies staged sidecar cache keys reflect the full runtime payload, not only the service executable.
/// </summary>
public sealed class MainWindowServiceStagingTests : IDisposable {
    private static readonly MethodInfo BuildServiceStageKeyMethod = typeof(MainWindow).GetMethod(
        "BuildServiceStageKey",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildServiceStageKey method was not found.");

    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "IntelligenceX.Chat.App.Tests",
        nameof(MainWindowServiceStagingTests),
        Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Ensures the staging key is deterministic for an unchanged service payload.
    /// </summary>
    [Fact]
    public void BuildServiceStageKey_RemainsStable_WhenPayloadIsUnchanged() {
        Directory.CreateDirectory(_tempRoot);
        WritePayloadFile("IntelligenceX.Chat.Service.dll", "service", new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc));
        WritePayloadFile("IntelligenceX.Chat.Tooling.dll", "tooling-v1", new DateTime(2026, 3, 6, 12, 0, 1, DateTimeKind.Utc));
        WritePayloadFile("IntelligenceX.Chat.Service.deps.json", "{ \"runtimeTarget\": \"v1\" }", new DateTime(2026, 3, 6, 12, 0, 2, DateTimeKind.Utc));

        var firstKey = BuildServiceStageKey(_tempRoot);
        var secondKey = BuildServiceStageKey(_tempRoot);

        Assert.Equal(firstKey, secondKey);
    }

    /// <summary>
    /// Ensures tooling or dependency graph updates invalidate the staged sidecar cache key.
    /// </summary>
    [Fact]
    public void BuildServiceStageKey_Changes_WhenToolingPayloadChanges() {
        Directory.CreateDirectory(_tempRoot);
        WritePayloadFile("IntelligenceX.Chat.Service.dll", "service", new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc));
        WritePayloadFile("IntelligenceX.Chat.Tooling.dll", "tooling-v1", new DateTime(2026, 3, 6, 12, 0, 1, DateTimeKind.Utc));
        WritePayloadFile("IntelligenceX.Chat.Service.deps.json", "{ \"runtimeTarget\": \"v1\" }", new DateTime(2026, 3, 6, 12, 0, 2, DateTimeKind.Utc));

        var before = BuildServiceStageKey(_tempRoot);

        WritePayloadFile("IntelligenceX.Chat.Tooling.dll", "tooling-v2-with-bootstrap-fix", new DateTime(2026, 3, 6, 12, 5, 0, DateTimeKind.Utc));
        var afterToolingChange = BuildServiceStageKey(_tempRoot);

        WritePayloadFile("IntelligenceX.Chat.Service.deps.json", "{ \"runtimeTarget\": \"v2\" }", new DateTime(2026, 3, 6, 12, 10, 0, DateTimeKind.Utc));
        var afterDepsChange = BuildServiceStageKey(_tempRoot);

        Assert.NotEqual(before, afterToolingChange);
        Assert.NotEqual(afterToolingChange, afterDepsChange);
    }

    /// <summary>
    /// Deletes the temporary payload directory created for each test.
    /// </summary>
    public void Dispose() {
        try {
            if (Directory.Exists(_tempRoot)) {
                Directory.Delete(_tempRoot, recursive: true);
            }
        } catch {
            // Ignore test cleanup failures.
        }
    }

    private void WritePayloadFile(string relativePath, string content, DateTime lastWriteTimeUtc) {
        var fullPath = Path.Combine(_tempRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        File.SetLastWriteTimeUtc(fullPath, lastWriteTimeUtc);
    }

    private static string BuildServiceStageKey(string serviceSourceDir) {
        return Assert.IsType<string>(BuildServiceStageKeyMethod.Invoke(null, new object[] { serviceSourceDir }));
    }
}

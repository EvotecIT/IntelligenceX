using System;
using System.IO;

namespace IntelligenceX.Chat.App.Tests;

internal static class TempPathTestHelper {
    internal static string CreateTempDirectoryPath(string prefix) {
        return Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
    }

    internal static string CreateTempDirectory(string prefix) {
        var path = CreateTempDirectoryPath(prefix);
        Directory.CreateDirectory(path);
        return path;
    }
}

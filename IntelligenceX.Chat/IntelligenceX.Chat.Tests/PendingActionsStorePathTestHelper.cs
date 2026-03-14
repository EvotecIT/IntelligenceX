using System;
using System.IO;
using IntelligenceX.Chat.Service;

namespace IntelligenceX.Chat.Tests;

internal static class PendingActionsStorePathTestHelper {
    internal static string CreateAllowedPendingActionsStorePath(string namePrefix, out string root) {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) {
            localAppData = ".";
        }

        root = Path.Combine(
            localAppData,
            "IntelligenceX.Chat",
            "tests",
            namePrefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "pending-actions.json");
    }

    internal static ServiceOptions CreateIsolatedServiceOptions(string namePrefix, out string root) {
        return new ServiceOptions {
            PendingActionsStorePath = CreateAllowedPendingActionsStorePath(namePrefix, out root)
        };
    }
}

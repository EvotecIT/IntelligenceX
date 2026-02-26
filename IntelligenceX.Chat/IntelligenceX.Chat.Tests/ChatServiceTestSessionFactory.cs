using System;
using System.IO;
using IntelligenceX.Chat.Service;

namespace IntelligenceX.Chat.Tests;

internal static class ChatServiceTestSessionFactory {
    private const string PendingActionsStoreFileName = "pending-actions.json";

    internal static ServiceOptions CreateIsolatedOptions() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData)) {
            localAppData = ".";
        }

        var isolatedDirectory = Path.Combine(
            localAppData,
            "IntelligenceX.Chat",
            "tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDirectory);

        return new ServiceOptions {
            PendingActionsStorePath = Path.Combine(isolatedDirectory, PendingActionsStoreFileName)
        };
    }

    internal static (ServiceOptions Options, string PendingActionsStorePath, string PersistenceDirectory) CreateIsolatedPersistenceOptions() {
        var options = CreateIsolatedOptions();
        var path = options.PendingActionsStorePath ?? throw new InvalidOperationException("PendingActionsStorePath was not initialized.");
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) {
            throw new InvalidOperationException("PendingActionsStorePath did not include a directory.");
        }

        return (options, path, directory);
    }

    internal static ChatServiceSession CreateIsolatedSession() {
        return new ChatServiceSession(CreateIsolatedOptions(), Stream.Null);
    }
}

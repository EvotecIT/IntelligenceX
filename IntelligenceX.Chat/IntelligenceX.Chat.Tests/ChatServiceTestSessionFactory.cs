using System;
using System.IO;
using IntelligenceX.Chat.Service;

namespace IntelligenceX.Chat.Tests;

internal static class ChatServiceTestSessionFactory {
    internal static ServiceOptions CreateIsolatedOptions() {
        return PendingActionsStorePathTestHelper.CreateIsolatedServiceOptions("ix-chat-session-factory", out _);
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

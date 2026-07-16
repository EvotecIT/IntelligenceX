using System.Text.Json;
using IntelligenceX.Chat.Service.Persistence;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceJsonFileStoreTests {
    [Fact]
    public void ResolveDefaultPath_UsesTemporaryDirectoryWhenLocalApplicationDataIsUnavailable() {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));

        var path = ChatServiceJsonFileStore.ResolveDefaultPath(
            "state.json",
            localApplicationData: " ",
            temporaryPath: temporaryRoot);

        Assert.Equal(Path.Combine(temporaryRoot, "IntelligenceX.Chat", "state.json"), path);
    }

    [Fact]
    public void WriteAndRead_RoundTripsThroughAtomicSnapshot() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            var expected = new TestStore { Version = 2, Value = "ready" };

            ChatServiceJsonFileStore.Write(
                path,
                expected,
                static value => JsonSerializer.Serialize(value),
                "Test store");
            ChatServiceJsonFileStore.Write(
                path,
                new TestStore { Version = 2, Value = "updated" },
                static value => JsonSerializer.Serialize(value),
                "Test store");

            var result = ChatServiceJsonFileStore.Read<TestStore>(
                path,
                maximumBytes: 1024,
                static json => JsonSerializer.Deserialize<TestStore>(json),
                static value => value.Version == 2,
                normalize: null,
                "Test store");

            Assert.Equal(ChatServiceJsonFileReadState.Loaded, result.State);
            Assert.NotNull(result.Value);
            Assert.Equal("updated", result.Value.Value);
            Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Read_RejectsSnapshotsAboveTheDomainLimit() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new TestStore { Version = 2, Value = "too-large" }));

            var result = ChatServiceJsonFileStore.Read<TestStore>(
                path,
                maximumBytes: 4,
                static _ => throw new InvalidOperationException("Oversized snapshots must not be deserialized."),
                static _ => true,
                normalize: null,
                "Test store");

            Assert.Equal(ChatServiceJsonFileReadState.Invalid, result.State);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Read_DistinguishesMissingAndInvalidSnapshots() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            var missing = Read(path);

            File.WriteAllText(path, "{not-json}");
            var invalid = Read(path);

            Assert.Equal(ChatServiceJsonFileReadState.Empty, missing.State);
            Assert.Equal(ChatServiceJsonFileReadState.Invalid, invalid.State);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadOrCreate_RejectsUnsupportedVersions() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new TestStore { Version = 1, Value = "stale" }));

            var result = ChatServiceJsonFileStore.ReadOrCreate(
                path,
                maximumBytes: 1024,
                static json => JsonSerializer.Deserialize<TestStore>(json),
                static value => value.Version == 2,
                normalize: null,
                static () => new TestStore { Version = 2 },
                "Test store");

            Assert.Equal(2, result.Version);
            Assert.Equal(string.Empty, result.Value);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Delete_RemovesExistingSnapshot() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            File.WriteAllText(path, "{}");

            ChatServiceJsonFileStore.Delete(path, "Test store");

            Assert.False(File.Exists(path));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ChatServiceJsonFileReadResult<TestStore> Read(string path) {
        return ChatServiceJsonFileStore.Read(
            path,
            maximumBytes: 1024,
            static json => JsonSerializer.Deserialize<TestStore>(json),
            static value => value.Version == 2,
            normalize: null,
            "Test store");
    }

    private static string CreateRoot() {
        var root = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class TestStore {
        public int Version { get; set; }
        public string Value { get; set; } = string.Empty;
    }
}

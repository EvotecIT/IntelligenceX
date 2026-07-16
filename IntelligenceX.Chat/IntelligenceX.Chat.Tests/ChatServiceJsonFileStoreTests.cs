using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Storage;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Service.Persistence;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ChatServiceJsonFileStoreTests {
    [Fact]
    public void ResolveDefaultPath_UsesDurableUserProfileWhenPlatformDataRootsAreUnavailable() {
        var userProfile = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));

        var path = ChatStatePaths.ResolveDefaultPath(
            "state.json",
            localApplicationData: " ",
            xdgDataHome: " ",
            userProfile: userProfile);

        Assert.Equal(Path.Combine(userProfile, ".local", "share", "IntelligenceX.Chat", "state.json"), path);
    }

    [Fact]
    public void ResolveDefaultPath_RejectsMissingPerUserRoots() {
        Assert.Throws<InvalidOperationException>(() => ChatStatePaths.ResolveDefaultPath(
            "state.json",
            localApplicationData: " ",
            xdgDataHome: "relative/path",
            userProfile: " "));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("nested/..")]
    public void ResolveDefaultPath_RejectsDotSegments(string fileName) {
        var userProfile = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));

        Assert.Throws<ArgumentException>(() => ChatStatePaths.ResolveDefaultPath(
            fileName,
            localApplicationData: " ",
            xdgDataHome: " ",
            userProfile: userProfile));
    }

    [Fact]
    public void ServiceDefaults_UseTheSharedStatePathOwner() {
        Assert.Equal(ChatStatePaths.GetDefaultPath("state.db"), ServiceOptions.GetDefaultStateDbPath());
        Assert.Equal(
            ChatStatePaths.GetDefaultPath("tooling-bootstrap-cache-v1.json"),
            ServiceOptions.GetDefaultToolingBootstrapCachePath());
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
    public void Write_DoesNotChangeArbitraryUnixDirectoryPermissions() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            var originalMode = UnixFileMode.UserRead
                               | UnixFileMode.UserWrite
                               | UnixFileMode.UserExecute
                               | UnixFileMode.GroupRead
                               | UnixFileMode.GroupExecute
                               | UnixFileMode.OtherRead
                               | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(root, originalMode);

            ChatServiceJsonFileStore.Write(
                path,
                new TestStore { Version = 2, Value = "private" },
                static value => JsonSerializer.Serialize(value),
                "Test store");

            Assert.Equal(originalMode, File.GetUnixFileMode(root));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Write_HardensExplicitDedicatedUnixDirectory() {
        if (OperatingSystem.IsWindows()) {
            return;
        }

        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            File.SetUnixFileMode(
                root,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            ChatJsonFileStore.Write(path, "{}", hardenExistingDirectory: true);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(root));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
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
    public void Read_TreatsWhitespaceOnlySnapshotAsInvalid() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "state.json");
            File.WriteAllText(path, "   \r\n\t");

            var result = Read(path);

            Assert.Equal(ChatServiceJsonFileReadState.Invalid, result.State);
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

    [Theory]
    [InlineData("../escape.json")]
    [InlineData("nested/escape.json")]
    [InlineData(".")]
    [InlineData("..")]
    public void ResolvePathOverrideWithinDefaultDirectory_RejectsUnsafeRelativePaths(string candidate) {
        var expected = ChatStatePaths.GetDefaultPath("pending-actions.json");

        var actual = ChatServiceJsonFileStore.ResolvePathOverrideWithinDefaultDirectory(
            candidate,
            "pending-actions.json");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ResolvePathOverrideWithinDefaultDirectory_AllowsAbsoluteDescendant() {
        var expected = Path.Combine(ChatStatePaths.GetDefaultDirectory(), "tests", "pending-actions.json");

        var actual = ChatServiceJsonFileStore.ResolvePathOverrideWithinDefaultDirectory(
            expected,
            "pending-actions.json");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsPathWithinDirectory_UsesPlatformCaseRules() {
        var parent = Path.Combine(Path.GetTempPath(), "ix-path-case-" + Guid.NewGuid().ToString("N"));
        var caseChangedParent = parent.ToUpperInvariant();
        var candidate = Path.Combine(caseChangedParent, "state.json");

        Assert.Equal(
            OperatingSystem.IsWindows(),
            ChatStatePaths.IsPathWithinDirectory(parent, candidate));
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

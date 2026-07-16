using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupToolHealthCacheTests {
    [Fact]
    public void DeserializeStartupToolHealthCache_SkipsMalformedEntriesWithoutDiscardingValidBackoff() {
        const string Json = """
                            {
                              "Entries": [
                                null,
                                { "Key": " " },
                                {
                                  "Key": "open_source|system|system_pack_info",
                                  "ErrorCode": " tool_timeout ",
                                  "Error": " timed out ",
                                  "LastFailedUtc": "2026-07-16T18:00:00Z",
                                  "NextProbeUtc": "2026-07-16T18:10:00Z",
                                  "ConsecutiveFailures": 100
                                }
                              ]
                            }
                            """;

        var entries = ChatServiceSession.DeserializeStartupToolHealthCache(Json);

        var entry = Assert.Single(entries).Value;
        Assert.Equal("tool_timeout", entry.ErrorCode);
        Assert.Equal("timed out", entry.Error);
        Assert.Equal(64, entry.ConsecutiveFailures);
    }

    [Fact]
    public void ApplyStartupToolHealthCacheMutations_PreservesUpdatesFromSeparateSessions() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "startup-tool-health-cache.json");
            var firstObservedUtc = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc);
            var secondObservedUtc = firstObservedUtc.AddSeconds(1);

            ChatServiceSession.ApplyStartupToolHealthCacheMutations(path, [
                CreateFailureMutation("open_source|first|pack_info", firstObservedUtc)
            ]);
            ChatServiceSession.ApplyStartupToolHealthCacheMutations(path, [
                CreateFailureMutation("open_source|second|pack_info", secondObservedUtc)
            ]);

            var entries = ChatServiceSession.LoadStartupToolHealthCache(path);

            Assert.Equal(2, entries.Count);
            Assert.Contains("open_source|first|pack_info", entries.Keys);
            Assert.Contains("open_source|second|pack_info", entries.Keys);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ApplyStartupToolHealthCacheMutations_DoesNotRestoreFailureOlderThanSuccess() {
        var root = CreateRoot();
        try {
            var path = Path.Combine(root, "startup-tool-health-cache.json");
            const string CacheKey = "open_source|system|system_pack_info";
            var initialFailureUtc = new DateTime(2026, 7, 16, 18, 0, 0, DateTimeKind.Utc);
            var staleFailureUtc = initialFailureUtc.AddMinutes(1);
            var successfulProbeUtc = staleFailureUtc.AddMinutes(1);

            ChatServiceSession.ApplyStartupToolHealthCacheMutations(path, [
                CreateFailureMutation(CacheKey, initialFailureUtc)
            ]);
            ChatServiceSession.ApplyStartupToolHealthCacheMutations(path, [
                new ChatServiceSession.StartupToolHealthCacheMutation(CacheKey, Entry: null, successfulProbeUtc)
            ]);
            ChatServiceSession.ApplyStartupToolHealthCacheMutations(path, [
                CreateFailureMutation(CacheKey, staleFailureUtc)
            ]);

            Assert.Empty(ChatServiceSession.LoadStartupToolHealthCache(path));
            Assert.Contains("\"Successful\":true", File.ReadAllText(path), StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ChatServiceSession.StartupToolHealthCacheMutation CreateFailureMutation(
        string key,
        DateTime observedUtc) {
        var entry = new ChatServiceSession.StartupToolHealthCacheEntry(
            ErrorCode: "tool_timeout",
            Error: "timed out",
            LastFailedUtc: observedUtc,
            NextProbeUtc: observedUtc.AddMinutes(5),
            ConsecutiveFailures: 1);
        return new ChatServiceSession.StartupToolHealthCacheMutation(key, entry, observedUtc);
    }

    private static string CreateRoot() {
        var root = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

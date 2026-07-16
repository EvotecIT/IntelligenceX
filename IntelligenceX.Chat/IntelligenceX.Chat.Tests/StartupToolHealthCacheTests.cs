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
}

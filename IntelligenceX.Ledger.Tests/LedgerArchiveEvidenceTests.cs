using DBAClientX;
using IntelligenceX.Codex;
using IntelligenceX.Telemetry.Usage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerArchiveEvidenceTests {
    [Theory]
    [InlineData(true, 1, true, true)]
    [InlineData(true, 0, true, false)]
    [InlineData(true, null, false, false)]
    [InlineData(false, null, false, false)]
    public async Task ArchiveObservationRequiresAnExplicitNonNullProviderFlag(bool hasColumn, int? flag, bool known, bool archived) {
        var root = Path.Combine(Path.GetTempPath(), "ledger-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            using var database = new SQLite();
            await using (var session = await database.OpenSessionAsync(Path.Combine(root, "state_5.sqlite"))) {
                await session.ExecuteNonQueryAsync(hasColumn ? "CREATE TABLE threads(id TEXT,archived INTEGER)" : "CREATE TABLE threads(id TEXT)");
                await session.ExecuteNonQueryAsync(hasColumn ? "INSERT INTO threads VALUES ('synthetic-session',@flag)" :
                    "INSERT INTO threads VALUES ('synthetic-session')", hasColumn ? new Dictionary<string, object?> { ["flag"] = flag } : null);
            }
            var source = new SourceRootRecord("synthetic-root", "codex", UsageSourceKind.LocalLogs, root);

            var observation = Assert.Single(await CodexAgentSessionMetadataReader.ReadAsync(source));

            Assert.Equal(known, observation.ArchiveStateKnown);
            Assert.Equal(archived, observation.IsArchived);
        } finally { Directory.Delete(root, recursive: true); }
    }
}

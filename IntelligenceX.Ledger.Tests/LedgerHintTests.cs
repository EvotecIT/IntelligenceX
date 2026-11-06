using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerHintTests {
    [Fact]
    public async Task ArgumentHintPersistsMetadataWithoutTranscriptContent() {
        var directory = Path.Combine(Path.GetTempPath(), "ledger-hint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var spoolDirectory = Path.Combine(directory, "outbox");
            var config = Path.Combine(directory, "reporter.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new ReporterSettings {
                Endpoint = "http://127.0.0.1:1/", Token = new string('w', 32),
                MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", SpoolDirectory = spoolDirectory
            }, LedgerJson.Options));
            var session = Guid.NewGuid().ToString("D");
            const string content = "Synthetic transcript content stays outside the ledger.";
            var payload = JsonSerializer.Serialize(new Dictionary<string, string> {
                ["thread-id"] = session, ["last-assistant-message"] = content
            });

            var result = await Program.Main(["hook", "--config", config, payload]);

            Assert.Equal(0, result);
            using var spool = new ReporterSpool(spoolDirectory, 1024 * 1024);
            var hint = Assert.Single(spool.Pending());
            Assert.Equal(LedgerScope.SessionItemId("synthetic-machine", "codex", session), hint.ItemId);
            Assert.Equal("hook", hint.Source);
            Assert.Equal("codex", hint.Provider);
            Assert.Equal("synthetic-reporter", hint.ReporterId);
            Assert.Null(hint.Session);
            Assert.Equal("hook-hint-requires-reconciliation", Assert.Single(hint.Coverage!));
            Assert.DoesNotContain(content, JsonSerializer.Serialize(hint, LedgerJson.Options));
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }
}

using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestBankedResetInventoryRoundTripAndCorruptionSafety() {
        var root = Path.Combine(Path.GetTempPath(), "ix-reset-inventory-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "inventory.json");
        try {
            var first = new BankedResetInventoryStore(path);
            var second = new BankedResetInventoryStore(path);
            var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
            first.Save(new BankedResetCredit("one", "codex", "a", ResetCreditScope.Full,
                ResetCreditEvidence.Manual, now));
            second.Save(new BankedResetCredit("two", "codex", "b", ResetCreditScope.Weekly,
                ResetCreditEvidence.ProviderReported, now, now.AddDays(2)));
            var records = first.Load();
            AssertEqual(2, records.Count, "separate store instances preserve accounts");
            AssertEqual(true, records[0].ExpiresAtUtc is null, "unknown expiry round trips");
            AssertEqual(ResetCreditEvidence.Manual, records[0].Evidence, "manual provenance round trips");
            AssertEqual(now.AddDays(2), records[1].ExpiresAtUtc, "known expiry round trips");
            first.Remove("codex", "b", "one");
            AssertEqual(2, first.Load().Count, "remove cannot cross account scope");
            first.Remove("codex", "a", "one");
            AssertEqual("b", first.Load().Single().AccountId, "exact entry removed");

            using (new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                var blocked = false;
                try { second.Load(); } catch (IOException) { blocked = true; }
                AssertEqual(true, blocked, "competing transaction fails visibly");
            }
            foreach (var invalid in new[] { "{", "{}", "{\"Version\":99,\"Credits\":[]}" }) {
                File.WriteAllText(path, invalid);
                var rejected = false;
                try {
                    first.Save(new BankedResetCredit("three", "codex", "c", ResetCreditScope.Full,
                        ResetCreditEvidence.Manual, now));
                } catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) {
                    rejected = true;
                }
                AssertEqual(true, rejected, "invalid inventory rejects writes");
                AssertEqual(invalid, File.ReadAllText(path), "invalid inventory is preserved for recovery");
            }
        } finally {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}

using System.Text.Json;
using IntelligenceX.Codex;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Ledger.Reporting;

internal static class ArchiveCleanupCommand {
    internal const string Usage = """
        IntelligenceX.Ledger archive-cleanup ready --config <reporter-json-file> --thread <id> [--worktree <path>] [--scratch <path>...]
        IntelligenceX.Ledger archive-cleanup status --config <reporter-json-file> --thread <id>
        """;

    internal static async Task<int> RunAsync(string[] args) {
        if (args.Length < 6 || args[1] is not ("ready" or "status") || args[2] != "--config" ||
            args[4] != "--thread" || !Guid.TryParse(args[5], out var id) || id == Guid.Empty || (args.Length - 6) % 2 != 0)
            return InvalidArguments();

        string? worktree = null;
        var scratch = new List<string>();
        for (var i = 6; i < args.Length; i += 2) {
            if (args[i] == "--worktree" && worktree is null) worktree = args[i + 1];
            else if (args[i] == "--scratch") scratch.Add(args[i + 1]);
            else return InvalidArguments();
        }

        try {
            var settings = JsonSerializer.Deserialize<ReporterSettings>(File.ReadAllText(args[3]), LedgerJson.Options)
                ?? throw new InvalidDataException("Missing local reporter configuration.");
            var store = new ArchiveCleanupStore(settings);
            if (args[1] == "status") {
                if (args.Length != 6) return InvalidArguments();
                var (registration, receipt) = store.Read(id);
                if (registration is null) {
                    Console.Error.WriteLine("No archive cleanup registration for this chat.");
                    return 1;
                }
                Console.WriteLine(JsonSerializer.Serialize(new { registration, receipt }, LedgerJson.Options));
                return 0;
            }

            var roots = LocalAgentWorkStateService.DiscoverRoots().Where(root =>
                root.SourceKind == UsageSourceKind.LocalLogs && root.ProviderId == "codex");
            var observed = await CodexAgentSessionMetadataReader.ReadAvailableAsync(roots).ConfigureAwait(false);
            var matching = observed.Sessions.Where(session => Guid.TryParse(session.Id, out var actualId) && actualId == id)
                .Take(2).ToArray();
            if (matching.Length != 1) throw new InvalidDataException("The exact chat could not be resolved to one local Codex source.");
            var registered = store.Register(id, matching[0], worktree, scratch, DateTimeOffset.UtcNow);
            Console.WriteLine(JsonSerializer.Serialize(registered, LedgerJson.Options));
            return 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException) {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static int InvalidArguments() {
        Console.Error.WriteLine(Usage);
        return 2;
    }
}

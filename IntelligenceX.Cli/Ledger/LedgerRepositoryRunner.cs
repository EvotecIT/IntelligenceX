using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Cli.Ledger;

internal static class LedgerRepositoryRunner {
    internal static async Task<int> RunAsync(string[] args) {
        if (args.Length < 4 || args[^2] != "--config") return 2;
        try {
            using var client = new LedgerReadClient(LedgerClientSettings.Load(args[^1]));
            var command = args[..^2];
            if (command[0] is not ("repo_activity" or "overlap")) return 2;
            string? branch = null, text = null;
            var recent24h = false;
            var details = false;
            var files = new List<string>();
            for (var i = 2; i < command.Length; i++) {
                var option = command[i];
                if (option == "--recent24h") { recent24h = true; continue; }
                if (option == "--details") { details = true; continue; }
                if (command[0] != "overlap") return 2;
                if (++i >= command.Length) return 2;
                switch (option) {
                    case "--branch": branch = command[i]; break;
                    case "--text": text = command[i]; break;
                    case "--files":
                        files.Add(command[i]);
                        while (i + 1 < command.Length && !command[i + 1].StartsWith("--", StringComparison.Ordinal)) files.Add(command[++i]);
                        break;
                    default: return 2;
                }
            }
            if (command[0] == "repo_activity")
                Console.WriteLine(JsonSerializer.Serialize(await client.RepoActivityAsync(command[1], recent24h: recent24h, details: details), LedgerReadJson.Default.RepositoryActivity));
            else
                Console.WriteLine(JsonSerializer.Serialize(await client.OverlapAsync(command[1], branch, files.ToArray(), text, recent24h: recent24h, details: details), LedgerReadJson.Default.RepositoryOverlap));
            return 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            Console.Error.WriteLine("Invalid Ledger repository query or local configuration."); return 2;
        } catch (HttpRequestException) { Console.Error.WriteLine("Ledger repository query failed."); return 1; }
        catch (TaskCanceledException) { Console.Error.WriteLine("Ledger repository query timed out."); return 1; }
    }
}

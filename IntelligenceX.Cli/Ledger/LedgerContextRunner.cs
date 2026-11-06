using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Cli.Ledger;

internal static class LedgerContextRunner {
    internal static async Task<int> RunAsync(string[] args) {
        if (args.Length < 3 || args[^2] != "--config") return 2;
        try {
            using var client = new LedgerReadClient(LedgerClientSettings.Load(args[^1]));
            var command = args[..^2];
            var json = command switch {
                ["work"] => JsonSerializer.Serialize(await client.WorkAsync(), LedgerReadJson.Default.LedgerWorkView),
                ["projects"] => JsonSerializer.Serialize(await client.ProjectsAsync(), LedgerReadJson.Default.ProjectDirectoryView),
                ["context", var project] => JsonSerializer.Serialize(await client.ProjectAsync(project), LedgerReadJson.Default.ProjectContextView),
                ["context", var project, var query] => JsonSerializer.Serialize(await client.ProjectAsync(project, query), LedgerReadJson.Default.ProjectContextView),
                ["search", var project, var query] => JsonSerializer.Serialize(await client.SearchAsync(project, query), LedgerReadJson.Default.KnowledgeSearchView),
                _ => null
            };
            if (json is null) return 2;
            Console.WriteLine(json);
            return 0;
        } catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or ArgumentException) {
            Console.Error.WriteLine("Invalid local Ledger read configuration or response.");
            return 2;
        } catch (HttpRequestException) {
            Console.Error.WriteLine("Ledger read request failed.");
            return 1;
        } catch (TaskCanceledException) {
            Console.Error.WriteLine("Ledger read request timed out.");
            return 1;
        }
    }
}

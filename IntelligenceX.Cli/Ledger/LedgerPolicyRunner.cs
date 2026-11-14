using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Cli.Ledger;

/// <summary>Displays policy with read credentials and edits it with an enrolled operator credential.</summary>
internal static class LedgerPolicyRunner {
    internal static async Task<int> RunAsync(string[] args) {
        var configuration = Array.IndexOf(args, "--config");
        if (configuration != args.Length - 2 || configuration < 2 ||
            !(args[1] == "show" && configuration == 2 || args[1] == "set" && configuration == 4)) return 2;
        try {
            using var file = JsonDocument.Parse(File.ReadAllText(args[^1]));
            var isOperator = file.RootElement.TryGetProperty("token", out _);
            if (args[1] == "set" && !isOperator) {
                Console.Error.WriteLine("Policy changes require an enrolled operator configuration.");
                return 2;
            }
            using var client = isOperator
                ? LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(args[^1]))
                : new LedgerReadClient(LedgerClientSettings.Load(args[^1]));
            var policy = isOperator ? await client.PolicyAsync().ConfigureAwait(false) : await client.ReadPolicyAsync().ConfigureAwait(false);
            if (args[1] == "set") {
                JsonElement value;
                try {
                    using var document = JsonDocument.Parse(args[3]);
                    value = document.RootElement.Clone();
                } catch (JsonException) {
                    value = JsonSerializer.SerializeToElement(args[3]);
                }
                policy = await client.SetPolicyValueAsync(new(args[2], value, policy.Revision)).ConfigureAwait(false);
            }
            Console.WriteLine(JsonSerializer.Serialize(policy, LedgerReadJson.Default.CoordinationPolicy));
            return 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or HttpRequestException or InvalidOperationException or TaskCanceledException) {
            Console.Error.WriteLine("Ledger policy could not be read or changed. Check the configuration, revision and value.");
            return 1;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Cli.Ledger;

/// <summary>Lists, adds and removes the repositories every agent may ask about, beyond those in the service configuration.
/// The change takes effect at once, without a restart, and is recorded in the policy history.</summary>
internal static class LedgerProjectRunner {
    internal static async Task<int> RunAsync(string[] args) {
        var configuration = Array.IndexOf(args, "--config");
        var listing = args.Length == 4 && args[1] == "list" && configuration == 2;
        var change = args.Length == 5 && args[1] is "add" or "remove" && configuration == 3;
        if (!listing && !change) return 2;
        try {
            using var client = LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(args[^1]));
            var policy = await client.PolicyAsync().ConfigureAwait(false);
            if (change) {
                var name = args[2].Trim();
                var projects = args[1] == "add"
                    ? policy.Projects.Contains(name, StringComparer.Ordinal) ? policy.Projects : [.. policy.Projects, name]
                    : policy.Projects.Where(project => !string.Equals(project, name, StringComparison.Ordinal)).ToArray();
                if (projects.Length != policy.Projects.Length)
                    policy = await client.SetPolicyValueAsync(new("projects",
                        JsonSerializer.SerializeToElement(projects.Order(StringComparer.Ordinal).ToArray()), policy.Revision)).ConfigureAwait(false);
            }
            Console.WriteLine(JsonSerializer.Serialize(new { revision = policy.Revision, projects = policy.Projects }));
            return 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or HttpRequestException or InvalidOperationException or TaskCanceledException or
            CoordinationCommandRejectedException) {
            Console.Error.WriteLine("The project list could not be read or changed. An enrolled operator configuration is required. " + error.Message);
            return 1;
        }
    }
}

using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Cli.Ledger;

internal static class LedgerLeaseRunner {
    internal static async Task<int> RunAsync(string[] args) {
        if (args.Length < 3 || args[^2] != "--config") return 2;
        var command = args[..^2];
        try {
            var write = command[0] is "lease_acquire" or "lease_renew" or "lease_release";
            using var client = write ? LedgerReadClient.ForLeases(LeaseClientSettings.Load(args[^1])) : new LedgerReadClient(LedgerClientSettings.Load(args[^1]));
            var json = command switch {
                ["who", var key] => JsonSerializer.Serialize(await client.WhoAsync(key), LedgerReadJson.Default.LeaseQuery),
                ["who", var key, "--prefix"] => JsonSerializer.Serialize(await client.WhoAsync(key, true), LedgerReadJson.Default.LeaseQuery),
                ["machine_status", var id] => JsonSerializer.Serialize(await client.MachineStatusAsync(id), LedgerReadJson.Default.MachineStatus),
                ["budget_status"] => JsonSerializer.Serialize(await client.BudgetStatusAsync(), LedgerReadJson.Default.BudgetStatus),
                ["lease_acquire", var key, var holder, var machine, var purpose, var ttl] when TryTtl(ttl, out var seconds) =>
                    JsonSerializer.Serialize(await client.AcquireLeaseAsync(new(key, holder, machine, purpose, seconds)), LedgerReadJson.Default.LeaseReply),
                ["lease_renew", var key, var leaseId, var ttl] when TryTtl(ttl, out var seconds) =>
                    JsonSerializer.Serialize(await client.RenewLeaseAsync(new(key, leaseId, seconds)), LedgerReadJson.Default.LeaseReply),
                ["lease_release", var key, var leaseId] => JsonSerializer.Serialize(await client.ReleaseLeaseAsync(new(key, leaseId)), LedgerReadJson.Default.LeaseReply),
                _ => null
            };
            if (json is null) return 2;
            Console.WriteLine(json);
            return 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException) {
            Console.Error.WriteLine("Invalid Ledger lease command or local configuration."); return 2;
        } catch (HttpRequestException) { Console.Error.WriteLine("Ledger lease request failed."); return 1; }
        catch (TaskCanceledException) { Console.Error.WriteLine("Ledger lease request timed out."); return 1; }
    }

    private static bool TryTtl(string value, out int seconds) {
        if (int.TryParse(value, out seconds)) return seconds is >= 1 and <= 86400;
        seconds = 0;
        if (value.Length < 2 || !int.TryParse(value[..^1], out var number) || number is < 1 or > 86400) return false;
        var multiplier = value[^1] switch { 's' => 1, 'm' => 60, 'h' => 3600, _ => 0 };
        if (multiplier == 0 || number > 86400 / multiplier) return false;
        seconds = number * multiplier;
        return true;
    }
}

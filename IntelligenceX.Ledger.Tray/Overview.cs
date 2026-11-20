using System.Globalization;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Tray;

/// <summary>Everything one poll read: the service's health, every machine's sign-in facts and changes, usage and the
/// GitHub budget, and the supervisor's files when this machine hosts the Ledger. A read that failed is null with why.</summary>
internal sealed record Overview(DateTimeOffset At, LedgerHealth? Health, string? HealthError, AuthStateView? Auth, BudgetStatus? Budget,
    GitHubBudgetView? GitHub, Supervisor? Local);

/// <summary>The Windows supervisor's own record on the Ledger host: its last check, failures in a row, restarts and the
/// deployed commit.</summary>
internal sealed record Supervisor(string? Verdict, int Failures, DateTimeOffset? CheckedAtUtc, string? Commit, int RestartsLastDay,
    DateTimeOffset? LastRestartUtc, string? LastRestartReason) {
    /// <summary>Reads the supervisor files in the Ledger folder; null when this machine doesn't run the service.</summary>
    public static Supervisor? Read(string ledgerDirectory, DateTimeOffset now) {
        var health = Path.Combine(ledgerDirectory, "service-health.json");
        if (!File.Exists(health)) return null;
        try {
            using var json = JsonDocument.Parse(File.ReadAllText(health));
            var root = json.RootElement;
            var verdict = root.TryGetProperty("verdict", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var failures = root.TryGetProperty("failures", out var f) && f.TryGetInt32(out var count) ? count : 0;
            DateTimeOffset? checkedAt = root.TryGetProperty("checkedAtUtc", out var c) && c.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(c.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;
            string? commit = null;
            var deployment = Path.Combine(ledgerDirectory, "deployment.json");
            if (File.Exists(deployment)) {
                using var deployed = JsonDocument.Parse(File.ReadAllText(deployment));
                if (deployed.RootElement.TryGetProperty("sourceCommit", out var s) && s.ValueKind == JsonValueKind.String) commit = s.GetString();
            }
            var (restarts, last, reason) = Restarts(Path.Combine(ledgerDirectory, "service-restarts.jsonl"), now);
            return new(verdict, failures, checkedAt, commit, restarts, last, reason);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) {
            return new("unreadable", 0, null, null, 0, null, null);
        }
    }

    private static (int Count, DateTimeOffset? Last, string? Reason) Restarts(string path, DateTimeOffset now) {
        if (!File.Exists(path)) return (0, null, null);
        var count = 0;
        DateTimeOffset? last = null;
        string? reason = null;
        foreach (var line in File.ReadLines(path).TakeLast(200)) {
            try {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var atText = new[] { "atUtc", "restartedAtUtc", "at", "time" }
                    .Select(name => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                    .FirstOrDefault(text => text is not null);
                if (atText is null || !DateTimeOffset.TryParse(atText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)) continue;
                if (now - at <= TimeSpan.FromDays(1)) count++;
                if (last is null || at > last) {
                    last = at;
                    reason = root.TryGetProperty("reason", out var why) && why.ValueKind == JsonValueKind.String ? why.GetString() : null;
                }
            } catch (JsonException) { }
        }
        return (count, last, reason);
    }
}

/// <summary>Reads one overview from the Ledger: five small local reads, no GitHub requests.</summary>
internal sealed class LedgerPoller(string clientConfig, string ledgerDirectory) {
    public async Task<Overview> ReadAsync(CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        LedgerClientSettings settings;
        try { settings = LedgerClientSettings.Load(clientConfig); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException) {
            var why = File.Exists(clientConfig) ? "Ledger read settings can't be used: " : "No Ledger read settings at ";
            return new(now, null, why + clientConfig, null, null, null, Supervisor.Read(ledgerDirectory, now));
        }
        using var client = new LedgerReadClient(settings);
        LedgerHealth? health = null;
        string? healthError = null;
        try { health = await client.HealthAsync(token).ConfigureAwait(false); }
        catch (Exception error) when (Failed(error, token)) { healthError = Describe(error); }
        // A Ledger that doesn't answer its health check won't answer the rest either.
        var auth = health is null ? null : await TryAsync(() => client.AuthStateAsync(changes: 100, token: token), token).ConfigureAwait(false);
        var budget = health is null ? null : await TryAsync(() => client.BudgetStatusAsync(token), token).ConfigureAwait(false);
        var github = health is null ? null : await TryAsync(() => client.GitHubBudgetAsync(token), token).ConfigureAwait(false);
        return new(now, health, healthError, auth, budget, github, Supervisor.Read(ledgerDirectory, now));
    }

    private static async Task<T?> TryAsync<T>(Func<Task<T>> read, CancellationToken token) where T : class {
        try { return await read().ConfigureAwait(false); }
        catch (Exception error) when (Failed(error, token)) { return null; }
    }

    private static bool Failed(Exception error, CancellationToken token) => !token.IsCancellationRequested &&
        error is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or JsonException;

    internal static string Describe(Exception error) => error switch {
        HttpRequestException { StatusCode: { } status } => "Ledger answered " + (int)status,
        HttpRequestException => "Ledger isn't answering",
        TaskCanceledException => "Ledger didn't answer in time",
        _ => "Ledger answer couldn't be read"
    };
}

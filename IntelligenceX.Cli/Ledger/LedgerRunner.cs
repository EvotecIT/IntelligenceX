using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ledger;

/// <summary>Read-only HTTP view of service-evaluated state; never runs local verdict rules.</summary>
internal static class LedgerRunner {
    internal static async Task<int> RunAsync(string[] args) {
        if (args.Length > 0 && args[0] == "declare") return LedgerDeclarationRunner.Run(args);
        if (args.Length == 0 || args[0] is "--help" or "-h" || args[0] is not ("status" or "needs-you" or "stuck" or "item" or "report" or "mark")) {
            Console.WriteLine("ix ledger status|needs-you|stuck|item <id> [--config <local-client-json>]");
            Console.WriteLine("Set IX_LEDGER_ENDPOINT and IX_LEDGER_READ_TOKEN locally. Output is JSON from the read API.");
            Console.WriteLine("ix ledger declare <codex|claude> <source-session-id> <declaration-json> --config <reporter-json> [--validate]");
            return args.Length == 0 ? 2 : 0;
        }
        var address = Environment.GetEnvironmentVariable("IX_LEDGER_ENDPOINT");
        var token = Environment.GetEnvironmentVariable("IX_LEDGER_READ_TOKEN");
        string? machine = null, reporter = null;
        string? cursor = null;
        if (args.Length >= 3 && args[^2] == "--config") {
            try {
                using var config = JsonDocument.Parse(File.ReadAllText(args[^1]));
                var root = config.RootElement;
                address = root.TryGetProperty("endpoint", out var configuredEndpoint) ? configuredEndpoint.GetString() :
                    "http://127.0.0.1:" + root.GetProperty("port").GetInt32() + "/";
                token = root.GetProperty(args[0] == "mark" ? "token" : "readToken").GetString();
                if (args[0] == "mark") { machine = root.GetProperty("machineId").GetString(); reporter = root.GetProperty("reporterId").GetString(); }
                args = args[..^2];
            } catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException) {
                Console.Error.WriteLine("Invalid local Ledger configuration.");
                return 2;
            }
        }
        if (args.Length >= 3 && args[^2] == "--cursor") { cursor = args[^1]; args = args[..^2]; }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint) || string.IsNullOrEmpty(token) ||
            endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)) {
            Console.Error.WriteLine("Configure the ledger endpoint and read token locally."); return 2;
        }
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
        using var client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (args[0] == "mark") {
            if (args.Length != 3 || !long.TryParse(args[1], out var transition) || transition < 1 ||
                args[2] is not ("right" or "wrong") || machine is null || reporter is null) return 2;
            using var content = new StringContent(JsonSerializer.Serialize(new[] { new {
                schemaVersion = 1, eventId = Guid.NewGuid(), epoch = Guid.NewGuid(), sequence = 1,
                machineId = machine, reporterId = reporter, itemId = "review:" + transition, provider = "review",
                source = "evidence", sourceVersion = "1", observedAtUtc = DateTimeOffset.UtcNow,
                reviewMark = new { transitionId = transition, mark = args[2] }
            } }), System.Text.Encoding.UTF8, "application/json");
            using var marked = await client.PostAsync("api/ledger/v1/events", content).ConfigureAwait(false);
            Console.WriteLine(marked.IsSuccessStatusCode ? "Shadow report mark saved." : "Shadow report mark was not saved.");
            return marked.IsSuccessStatusCode ? 0 : 1;
        }
        var route = args[0] switch {
            "status" when args.Length == 1 => "status",
            "needs-you" when args.Length == 1 => "items?verdict=needs-you" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)),
            "stuck" when args.Length == 1 => "items?verdict=stuck" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)),
            "item" when args.Length == 2 => "items/" + Uri.EscapeDataString(args[1]),
            "report" when args.Length == 2 => "reports/daily?date=" + Uri.EscapeDataString(args[1]),
            _ => null
        };
        if (route is null) return 2;
        try {
            using var response = await client.GetAsync("api/ledger/v1/" + route).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) { Console.Error.WriteLine("Ledger returned HTTP " + (int)response.StatusCode); return 1; }
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            Console.WriteLine(JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        } catch (HttpRequestException) { Console.Error.WriteLine("Ledger is unavailable."); return 1; }
        catch (TaskCanceledException) { Console.Error.WriteLine("Ledger request timed out."); return 1; }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Codex;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Cli.Agents;

internal static class AgentsRunner {
    public static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || args.Any(static arg => arg is "--help" or "-h" or "help")) {
            Console.WriteLine("Usage: intelligencex agents status [--json] [--github] [--login USER] [--stale-days N] [--codex-root PATH] [--claude-root PATH]");
            Console.WriteLine("Explicit roots replace discovery. GitHub is opt-in and reuses existing local credentials; no sign-in is started.");
            return 0;
        }
        if (args[0] != "status") return UsageError();
        var json = false;
        var github = false;
        var days = 7;
        string? login = null;
        var roots = new List<SourceRootRecord>();
        for (var i = 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--json": json = true; break;
                case "--github": github = true; break;
                case "--login" when i + 1 < args.Length: login = args[++i]; break;
                case "--stale-days" when i + 1 < args.Length:
                    if (!int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out days) || days <= 0 || days > 36500) return UsageError();
                    break;
                case "--codex-root" when i + 1 < args.Length:
                case "--claude-root" when i + 1 < args.Length:
                    var provider = args[i] == "--codex-root" ? "codex" : "claude";
                    var path = args[++i];
                    if (string.IsNullOrWhiteSpace(path)) return UsageError();
                    try {
                        roots.Add(new SourceRootRecord(SourceRootRecord.CreateStableId(provider, UsageSourceKind.LocalLogs, path), provider, UsageSourceKind.LocalLogs, path));
                    } catch (ArgumentException) { return UsageError();
                    } catch (NotSupportedException) { return UsageError(); }
                    break;
                default: return UsageError();
            }
        }
        if (login is not null && !github) return UsageError();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try {
            var selectedRoots = roots.Count == 0 ? LocalAgentWorkStateService.DiscoverRoots() : roots;
            var metadata = new List<AgentSessionState>();
            var diagnostics = new List<AgentSourceDiagnostic>();
            foreach (var root in selectedRoots.Where(static root => root.Enabled && root.ProviderId == "codex")) {
                try { metadata.AddRange(await CodexAgentSessionMetadataReader.ReadAsync(root, timeout.Token).ConfigureAwait(false)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { diagnostics.Add(new AgentSourceDiagnostic { Provider = "codex", Code = "unreadable-metadata" }); }
            }
            var local = LocalAgentWorkStateService.ReadLocal(selectedRoots, codexMetadata: metadata, cancellationToken: timeout.Token);
            diagnostics.AddRange(local.Diagnostics);
            var snapshot = AgentWorkStateRules.Evaluate(local.Sessions, Array.Empty<AgentPullRequestState>(), local.CapturedAtUtc,
                TimeSpan.FromDays(days), diagnostics);
            if (github) {
                try {
                    var credentials = await GitHubCredentialResolver.ResolveAsync(timeout.Token).ConfigureAwait(false);
                    if (credentials.HasToken) {
                        using var service = new GitHubDashboardService(credentials.Token!);
                        snapshot = await service.EnrichAgentWorkAsync(snapshot, login, TimeSpan.FromDays(days), timeout.Token).ConfigureAwait(false);
                    } else {
                        diagnostics.Add(new AgentSourceDiagnostic { Provider = "github", Code = "credentials-unavailable" });
                    }
                } catch (OperationCanceledException) { diagnostics.Add(new AgentSourceDiagnostic { Provider = "github", Code = "request-timeout" }); }
                catch (Exception) { diagnostics.Add(new AgentSourceDiagnostic { Provider = "github", Code = "request-failed" }); }
            }
            if (json) Console.WriteLine(AgentWorkJson.Serialize(snapshot));
            else {
                foreach (var session in snapshot.Sessions) Console.WriteLine($"{session.Provider}: {session.Title ?? session.Id} [{session.State}] {session.Repository} {session.Branch}");
                foreach (var item in snapshot.NeedsAttention) Console.WriteLine($"Needs attention: {item.Kind}: {item.Title ?? item.SessionId} {item.Url}");
                foreach (var diagnostic in snapshot.Diagnostics) Console.Error.WriteLine($"{diagnostic.Provider}: {diagnostic.Code} ({diagnostic.Count})");
            }
            return snapshot.Diagnostics.Count == 0 ? 0 : 2;
        } catch (OperationCanceledException) {
            Console.Error.WriteLine("Agent status collection timed out.");
            return 2;
        } catch (Exception) {
            Console.Error.WriteLine("Agent status collection failed.");
            return 2;
        }
    }

    private static int UsageError() {
        Console.Error.WriteLine("Invalid agents status arguments. Run intelligencex agents --help.");
        return 1;
    }
}

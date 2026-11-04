using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            Console.WriteLine("Usage: intelligencex agents status [--json] [--github] [--since 2d | --all-history] [--no-cache | --cache-path PATH] [--login USER] [--stale-days N] [--codex-root PATH] [--claude-root PATH]");
            Console.WriteLine("Explicit roots replace discovery. GitHub is opt-in and reuses existing local credentials; no sign-in is started.");
            return 0;
        }
        if (args[0] != "status") return UsageError();
        var json = false;
        var github = false;
        var days = 7;
        TimeSpan? activeWithin = TimeSpan.FromDays(2);
        var windowSpecified = false;
        var noCache = false;
        string? cachePath = null;
        string? login = null;
        var roots = new List<SourceRootRecord>();
        for (var i = 1; i < args.Length; i++) {
            switch (args[i]) {
                case "--json": json = true; break;
                case "--github": github = true; break;
                case "--no-cache": noCache = true; break;
                case "--cache-path" when i + 1 < args.Length:
                    cachePath = args[++i];
                    if (string.IsNullOrWhiteSpace(cachePath)) return UsageError();
                    break;
                case "--all-history":
                    if (windowSpecified) return UsageError();
                    windowSpecified = true;
                    activeWithin = null;
                    break;
                case "--since" when i + 1 < args.Length:
                    if (windowSpecified || !TryReadSince(args[++i], out var age)) return UsageError();
                    windowSpecified = true;
                    activeWithin = age;
                    break;
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
        if ((login is not null && !github) || (noCache && cachePath is not null)) return UsageError();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try {
            var selectedRoots = roots.Count == 0 ? LocalAgentWorkStateService.DiscoverRoots() : roots;
            var metadata = new List<AgentSessionState>();
            var diagnostics = new List<AgentSourceDiagnostic>();
            using var cache = OpenCache(noCache, cachePath, diagnostics);
            var available = await CodexAgentSessionMetadataReader.ReadAvailableAsync(
                selectedRoots.Where(static root => root.Enabled && root.ProviderId == "codex"), timeout.Token).ConfigureAwait(false);
            metadata.AddRange(available.Sessions);
            diagnostics.AddRange(available.Diagnostics);
            var local = LocalAgentWorkStateService.ReadLocal(selectedRoots, codexMetadata: metadata, cancellationToken: timeout.Token,
                options: new AgentWorkOptions { ActiveWithin = activeWithin, Cache = cache });
            diagnostics.AddRange(local.Diagnostics);
            var snapshot = AgentWorkStateRules.Evaluate(local.Sessions, Array.Empty<AgentPullRequestState>(), local.CapturedAtUtc,
                TimeSpan.FromDays(days), diagnostics) with { Scan = local.Scan };
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

    private static SqliteRawArtifactStore? OpenCache(bool disabled, string? path, List<AgentSourceDiagnostic> diagnostics) {
        if (disabled) return null;
        try {
            path = Path.GetFullPath(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntelligenceX", "agents", "scan-cache.db"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new SqliteRawArtifactStore(path);
        } catch (Exception) {
            diagnostics.Add(new AgentSourceDiagnostic { Provider = "local", Code = "cache-unavailable" });
            return null;
        }
    }

    private static bool TryReadSince(string value, out TimeSpan age) {
        age = default;
        if (value.Length < 2 || !int.TryParse(value.Substring(0, value.Length - 1), NumberStyles.None,
                CultureInfo.InvariantCulture, out var count) || count <= 0) return false;
        var minutes = value[value.Length - 1] switch { 'd' => 1440L, 'h' => 60L, 'm' => 1L, _ => 0L };
        if (minutes == 0 || count * minutes > 36500L * 1440) return false;
        age = TimeSpan.FromMinutes(count * minutes);
        return true;
    }

    private static int UsageError() {
        Console.Error.WriteLine("Invalid agents status arguments. Run intelligencex agents --help.");
        return 1;
    }
}

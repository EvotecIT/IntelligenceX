using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {

    private static async Task HandleCompareCommandAsync(string? arg, ReplOptions baseOptions, CancellationToken cancellationToken) {
        if (!TryParseCompareArguments(arg, out var profileNames, out var prompt, out var error)) {
            Console.WriteLine(error ?? "Invalid compare command. Try /help.");
            return;
        }

        var dbPath = string.IsNullOrWhiteSpace(baseOptions.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : baseOptions.StateDbPath!.Trim();
        using var store = new SqliteServiceProfileStore(dbPath);

        var profiles = new List<(string Name, ServiceProfile Profile)>(profileNames.Count);
        foreach (var profileName in profileNames) {
            var profile = await store.GetAsync(profileName, cancellationToken).ConfigureAwait(false);
            if (profile is null) {
                Console.WriteLine($"Profile not found: {profileName}");
                return;
            }
            profiles.Add((profileName, profile));
        }

        Console.WriteLine($"Running compare across {profiles.Count} profiles with shared tool catalog from current session...");

        var runs = new List<CompareProfileRun>(profiles.Count);
        foreach (var (name, profile) in profiles) {
            cancellationToken.ThrowIfCancellationRequested();
            Console.WriteLine($"> compare: {name}");
            var run = await RunCompareProfileAsync(baseOptions, name, profile, prompt, cancellationToken).ConfigureAwait(false);
            runs.Add(run);
        }

        Console.WriteLine();
        WriteCompareTable(runs);

        try {
            var artifactPath = WriteCompareArtifact(prompt, runs, baseOptions);
            Console.WriteLine();
            Console.WriteLine($"Benchmark artifact: {artifactPath}");
        } catch (Exception ex) {
            Console.WriteLine();
            Console.WriteLine($"Failed to write benchmark artifact: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<CompareProfileRun> RunCompareProfileAsync(ReplOptions baseOptions, string profileName, ServiceProfile profile,
        string prompt, CancellationToken cancellationToken) {
        var compareOptions = baseOptions.Clone();
        ApplyCompareProfile(compareOptions, profileName, profile);
        // Keep compare output compact and deterministic.
        compareOptions.LiveProgress = false;
        compareOptions.EchoToolOutputs = false;

        IntelligenceXClient? client = null;
        var startedAtUtc = DateTime.UtcNow;
        try {
            var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(BuildRuntimePolicyOptions(compareOptions));
            var packs = BuildPacks(compareOptions, runtimePolicyContext);
            var clientOptions = new IntelligenceXClientOptions {
                TransportKind = compareOptions.OpenAITransport,
                DefaultModel = compareOptions.Model
            };

            var instructions = LoadInstructions(compareOptions);
            var shaped = ApplyRuntimeShaping(instructions, compareOptions);
            if (clientOptions.TransportKind == OpenAITransportKind.Native && !string.IsNullOrWhiteSpace(shaped)) {
                clientOptions.NativeOptions.Instructions = shaped;
            }

            if (clientOptions.TransportKind == OpenAITransportKind.CompatibleHttp) {
                clientOptions.CompatibleHttpOptions.BaseUrl = compareOptions.OpenAIBaseUrl;
                clientOptions.CompatibleHttpOptions.ApiKey = compareOptions.OpenAIApiKey;
                clientOptions.CompatibleHttpOptions.Streaming = compareOptions.OpenAIStreaming;
                clientOptions.CompatibleHttpOptions.AllowInsecureHttp = compareOptions.OpenAIAllowInsecureHttp;
                clientOptions.CompatibleHttpOptions.AllowInsecureHttpNonLoopback = compareOptions.OpenAIAllowInsecureHttpNonLoopback;
            }

            var authPath = ResolveAuthPath(compareOptions);
            if (!string.IsNullOrWhiteSpace(authPath)) {
                clientOptions.NativeOptions.AuthStore = new FileAuthBundleStore(authPath);
            }

            client = await IntelligenceXClient.ConnectAsync(clientOptions).ConfigureAwait(false);
            if (client.TransportKind == OpenAITransportKind.Native) {
                if (!compareOptions.ForceLogin && await TryUseCachedChatGptLoginAsync(client, cancellationToken).ConfigureAwait(false)) {
                    // Cached login is available.
                } else {
                    await client.LoginChatGptAndWaitAsync(
                        onUrl: url => {
                            Console.WriteLine("ChatGPT login required. Open this URL in a browser:");
                            Console.WriteLine(url);
                            Console.WriteLine();
                        },
                        onPrompt: promptText => PromptForAuthAsync(promptText, cancellationToken),
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }

            var registry = new ToolRegistry {
                RequireExplicitRoutingMetadata = runtimePolicyContext.Options.RequireExplicitRoutingMetadata
            };
            ToolPackBootstrap.RegisterAll(registry, packs);
            _ = ToolRuntimePolicyBootstrap.ApplyToRegistry(registry, runtimePolicyContext);
            var session = new ReplSession(
                client,
                registry,
                compareOptions,
                shaped,
                status: null,
                orchestrationCatalog: ToolOrchestrationCatalog.Build(registry.GetDefinitions()));
            var turn = await session.AskWithMetricsAsync(prompt, cancellationToken).ConfigureAwait(false);

            return new CompareProfileRun {
                Profile = profileName,
                Model = compareOptions.Model,
                Outcome = "ok",
                StartedAtUtc = turn.Metrics.StartedAtUtc,
                FirstDeltaAtUtc = turn.Metrics.FirstDeltaAtUtc,
                CompletedAtUtc = turn.Metrics.CompletedAtUtc,
                DurationMs = turn.Metrics.DurationMs,
                TtftMs = turn.Metrics.TtftMs,
                PromptTokens = turn.Metrics.Usage?.InputTokens,
                CompletionTokens = turn.Metrics.Usage?.OutputTokens,
                TotalTokens = turn.Metrics.Usage?.TotalTokens,
                CachedPromptTokens = turn.Metrics.Usage?.CachedInputTokens,
                ReasoningTokens = turn.Metrics.Usage?.ReasoningTokens,
                ToolCallsCount = turn.Metrics.ToolCallsCount,
                ToolRounds = turn.Metrics.ToolRounds
            };
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            var completedAtUtc = DateTime.UtcNow;
            return new CompareProfileRun {
                Profile = profileName,
                Model = compareOptions.Model,
                Outcome = "error",
                Error = $"{ex.GetType().Name}: {ex.Message}",
                StartedAtUtc = startedAtUtc,
                CompletedAtUtc = completedAtUtc,
                DurationMs = (long)Math.Max(0, (completedAtUtc - startedAtUtc).TotalMilliseconds)
            };
        } finally {
            if (client is not null) {
                try {
                    await client.DisposeAsync().ConfigureAwait(false);
                } catch {
                    // Ignore.
                }
            }
        }
    }

    private static void ApplyCompareProfile(ReplOptions options, string profileName, ServiceProfile profile) {
        options.ProfileName = profileName;

        if (!string.IsNullOrWhiteSpace(profile.Model)) {
            options.Model = profile.Model.Trim();
        }
        options.OpenAITransport = profile.OpenAITransport;
        options.OpenAIBaseUrl = profile.OpenAIBaseUrl;
        options.OpenAIApiKey = profile.OpenAIApiKey;
        options.OpenAIStreaming = profile.OpenAIStreaming;
        options.OpenAIAllowInsecureHttp = profile.OpenAIAllowInsecureHttp;
        options.OpenAIAllowInsecureHttpNonLoopback = profile.OpenAIAllowInsecureHttpNonLoopback;
        options.ReasoningEffort = profile.ReasoningEffort;
        options.ReasoningSummary = profile.ReasoningSummary;
        options.TextVerbosity = profile.TextVerbosity;
        options.Temperature = profile.Temperature;
    }

    private static bool TryParseCompareArguments(string? arg, out IReadOnlyList<string> profileNames, out string prompt, out string? error) {
        profileNames = Array.Empty<string>();
        prompt = string.Empty;
        error = null;

        var raw = (arg ?? string.Empty).Trim();
        if (raw.Length == 0) {
            error = "Usage: /compare <profile1,profile2,...> -- <prompt>";
            return false;
        }

        var marker = raw.IndexOf("--", StringComparison.Ordinal);
        if (marker < 0) {
            error = "Usage: /compare <profile1,profile2,...> -- <prompt>";
            return false;
        }

        var namesPart = raw.Substring(0, marker).Trim();
        prompt = raw.Substring(marker + 2).Trim();
        if (namesPart.Length == 0 || prompt.Length == 0) {
            error = "Usage: /compare <profile1,profile2,...> -- <prompt>";
            return false;
        }

        var names = namesPart
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (names.Length < 2) {
            error = "Compare mode requires at least 2 profiles.";
            return false;
        }

        profileNames = names;
        return true;
    }

    private static void WriteCompareTable(IReadOnlyList<CompareProfileRun> runs) {
        if (runs is null || runs.Count == 0) {
            Console.WriteLine("(no compare results)");
            return;
        }

        var rows = new List<string[]>(runs.Count);
        foreach (var run in runs) {
            rows.Add(new[] {
                run.Profile ?? string.Empty,
                run.Model ?? string.Empty,
                FormatDuration(run.TtftMs),
                FormatDuration(run.DurationMs),
                FormatUsage(run.PromptTokens, run.CompletionTokens, run.TotalTokens),
                $"{run.ToolCallsCount}/{run.ToolRounds}",
                string.Equals(run.Outcome, "ok", StringComparison.OrdinalIgnoreCase) ? "ok" : (run.Error ?? "error")
            });
        }

        var headers = new[] { "Profile", "Model", "TTFT", "Total", "Tokens(P/C/T)", "Tools(C/R)", "Outcome" };
        var widths = new int[headers.Length];
        for (var i = 0; i < headers.Length; i++) {
            widths[i] = headers[i].Length;
        }
        foreach (var row in rows) {
            for (var i = 0; i < row.Length; i++) {
                var len = row[i]?.Length ?? 0;
                if (len > widths[i]) {
                    widths[i] = len;
                }
            }
        }

        WriteRow(headers, widths);
        WriteRow(widths.Select(static w => new string('-', w)).ToArray(), widths);
        foreach (var row in rows) {
            WriteRow(row, widths);
        }
    }

    private static void WriteRow(IReadOnlyList<string> cells, IReadOnlyList<int> widths) {
        for (var i = 0; i < cells.Count; i++) {
            if (i > 0) {
                Console.Write("  ");
            }
            Console.Write((cells[i] ?? string.Empty).PadRight(widths[i]));
        }
        Console.WriteLine();
    }

    private static string FormatDuration(long? valueMs) {
        if (!valueMs.HasValue) {
            return "n/a";
        }
        var value = Math.Max(0, valueMs.Value);
        if (value >= 1000) {
            return (value / 1000d).ToString("0.00") + "s";
        }
        return value + "ms";
    }

    private static string FormatUsage(long? promptTokens, long? completionTokens, long? totalTokens) {
        return $"{FormatToken(promptTokens)}/{FormatToken(completionTokens)}/{FormatToken(totalTokens)}";
    }

    private static string FormatToken(long? value) {
        return value.HasValue ? Math.Max(0, value.Value).ToString() : "-";
    }

    private static string WriteCompareArtifact(string prompt, IReadOnlyList<CompareProfileRun> runs, ReplOptions options) {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        var folder = Path.Combine(root, "IntelligenceX.Chat", "benchmarks");
        Directory.CreateDirectory(folder);

        var timestamp = DateTime.UtcNow;
        var path = Path.Combine(folder, $"compare-{timestamp:yyyyMMdd-HHmmss}.json");
        var artifact = new CompareArtifact {
            CreatedAtUtc = timestamp,
            Prompt = prompt,
            ActiveProfile = options.ProfileName,
            ActiveModel = options.Model,
            Runs = runs
        };
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        File.WriteAllText(path, json);
        return path;
    }

    private sealed class CompareArtifact {
        public required DateTime CreatedAtUtc { get; init; }
        public required string Prompt { get; init; }
        public string? ActiveProfile { get; init; }
        public string? ActiveModel { get; init; }
        public required IReadOnlyList<CompareProfileRun> Runs { get; init; }
    }

    private sealed class CompareProfileRun {
        public string? Profile { get; init; }
        public string? Model { get; init; }
        public required string Outcome { get; init; }
        public string? Error { get; init; }
        public required DateTime StartedAtUtc { get; init; }
        public DateTime? FirstDeltaAtUtc { get; init; }
        public required DateTime CompletedAtUtc { get; init; }
        public long DurationMs { get; init; }
        public long? TtftMs { get; init; }
        public long? PromptTokens { get; init; }
        public long? CompletionTokens { get; init; }
        public long? TotalTokens { get; init; }
        public long? CachedPromptTokens { get; init; }
        public long? ReasoningTokens { get; init; }
        public int ToolCallsCount { get; init; }
        public int ToolRounds { get; init; }
    }
}

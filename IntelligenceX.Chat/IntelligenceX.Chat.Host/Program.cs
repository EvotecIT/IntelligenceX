using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Profiles;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    public static async Task<int> Main(string[] args) {
        var options = ReplOptions.Parse(args, out var error);
        if (!string.IsNullOrWhiteSpace(error)) {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            WriteHelp();
            return 2;
        }

        if (options.ShowHelp) {
            WriteHelp();
            return 0;
        }

        Console.WriteLine("IntelligenceX Chat Host (REPL)");
        Console.WriteLine($"Profile: {(string.IsNullOrWhiteSpace(options.ProfileName) ? "(none)" : options.ProfileName)}");
        Console.WriteLine($"Model: {options.Model}");
        Console.WriteLine($"Parallel tool calls: {options.ParallelToolCalls}");
        Console.WriteLine($"Allow mutating parallel calls: {options.AllowMutatingParallelToolCalls}");
        Console.WriteLine($"Max tool rounds: {options.MaxToolRounds}");
        Console.WriteLine($"Turn timeout: {(options.TurnTimeoutSeconds <= 0 ? "(none)" : $"{options.TurnTimeoutSeconds}s")}");
        Console.WriteLine($"Tool timeout: {(options.ToolTimeoutSeconds <= 0 ? "(none)" : $"{options.ToolTimeoutSeconds}s")}");
        if (options.ReasoningEffort.HasValue) {
            Console.WriteLine($"Reasoning effort: {options.ReasoningEffort.Value}");
        }
        if (options.ReasoningSummary.HasValue) {
            Console.WriteLine($"Reasoning summary: {options.ReasoningSummary.Value}");
        }
        if (options.TextVerbosity.HasValue) {
            Console.WriteLine($"Text verbosity: {options.TextVerbosity.Value}");
        }
        if (options.Temperature.HasValue) {
            Console.WriteLine($"Temperature: {options.Temperature.Value}");
        }
        Console.WriteLine($"Max table rows: {(options.MaxTableRows <= 0 ? "(none)" : options.MaxTableRows)}");
        Console.WriteLine($"Max sample items: {(options.MaxSample <= 0 ? "(none)" : options.MaxSample)}");
        Console.WriteLine($"Redaction: {(options.Redact ? "on" : "off")}");
        Console.WriteLine($"IX.PowerShell allow write: {(options.PowerShellAllowWrite ? "on" : "off")}");
        Console.WriteLine($"Pack overrides (enable): {(options.EnabledPackIds.Count == 0 ? "(none)" : string.Join(", ", options.EnabledPackIds))}");
        Console.WriteLine($"Pack overrides (disable): {(options.DisabledPackIds.Count == 0 ? "(none)" : string.Join(", ", options.DisabledPackIds))}");
        Console.WriteLine($"Write governance mode: {ToolRuntimePolicyBootstrap.FormatWriteGovernanceMode(options.WriteGovernanceMode)}");
        Console.WriteLine($"Write audit sink mode: {ToolRuntimePolicyBootstrap.FormatWriteAuditSinkMode(options.WriteAuditSinkMode)}");
        Console.WriteLine($"Auth runtime preset: {ToolRuntimePolicyBootstrap.FormatAuthenticationRuntimePreset(options.AuthenticationRuntimePreset)}");
        Console.WriteLine($"Require explicit routing metadata: {(options.RequireExplicitRoutingMetadata ? "on" : "off")}");
        Console.WriteLine($"Allowed roots: {(options.AllowedRoots.Count == 0 ? "(none)" : string.Join("; ", options.AllowedRoots))}");
        var authPath = ResolveAuthPath(options);
        if (!string.IsNullOrWhiteSpace(authPath)) {
            Console.WriteLine($"Auth store: {authPath}");
        }

        try {
            var startupPackWarnings = new List<string>();
            var startupRuntimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
                BuildRuntimePolicyOptions(options),
                warning => CollectPackWarning(startupPackWarnings, warning));
            var startupRuntimePolicyDiagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(startupRuntimePolicyContext);
            var startupBootstrapResult = BuildPackBootstrapResult(
                options,
                startupRuntimePolicyContext,
                warning => CollectPackWarning(startupPackWarnings, warning));
            var packs = startupBootstrapResult.Packs;
            var startupRegistryContext = BuildHostToolRegistry(
                packs,
                requireExplicitRoutingMetadata: options.RequireExplicitRoutingMetadata);
            WritePolicyBanner(
                options,
                packs,
                startupBootstrapResult.PackAvailability,
                startupBootstrapResult.PluginAvailability,
                startupBootstrapResult.PluginCatalog,
                startupRuntimePolicyContext,
                startupRuntimePolicyDiagnostics,
                startupRegistryContext.RoutingCatalogDiagnostics,
                startupRegistryContext.OrchestrationCatalog,
                startupPackWarnings);
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            return await RunAsync(options, packs, cts.Token).ConfigureAwait(false);
        } catch (ToolPackBootstrapConfigurationException ex) {
            Console.Error.WriteLine(ex.Message);
            return 2;
        } catch (OpenAIUserCanceledLoginException) {
            Console.WriteLine();
            Console.WriteLine("Login canceled.");
            return 1;
        } catch (OperationCanceledException) {
            Console.WriteLine();
            Console.WriteLine("Canceled.");
            return 130;
        }
    }

    private static async Task<int> RunAsync(ReplOptions options, IReadOnlyList<IToolPack> packs, CancellationToken cancellationToken) {
        IntelligenceXClient? client = null;
        ToolRegistry? registry = null;
        ReplSession? session = null;
        string? runtimeInstructions = null;
        IReadOnlyList<ToolPackAvailabilityInfo> runtimePackAvailability = Array.Empty<ToolPackAvailabilityInfo>();
        IReadOnlyList<ToolPluginAvailabilityInfo> runtimePluginAvailability = Array.Empty<ToolPluginAvailabilityInfo>();
        IReadOnlyList<ToolPluginCatalogInfo> runtimePluginCatalog = Array.Empty<ToolPluginCatalogInfo>();
        ToolRoutingCatalogDiagnostics? runtimeRoutingCatalogDiagnostics = null;
        ToolOrchestrationCatalog? runtimeOrchestrationCatalog = null;

        Action<string> statusWriter = status => {
            // Keep progress lines visually distinct from assistant output.
            if (!string.IsNullOrWhiteSpace(status)) {
                Console.WriteLine($"> {status}");
            }
        };

        async Task BuildRuntimeAsync() {
            var runtimePackWarnings = new List<string>();
            var runtimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
                BuildRuntimePolicyOptions(options),
                warning => CollectPackWarning(runtimePackWarnings, warning));
            var nextBootstrapResult = BuildPackBootstrapResult(
                options,
                runtimePolicyContext,
                warning => CollectPackWarning(runtimePackWarnings, warning));
            var nextPacks = nextBootstrapResult.Packs;
            var clientOptions = new IntelligenceXClientOptions {
                TransportKind = options.OpenAITransport,
                DefaultModel = options.Model
            };

            var instructions = LoadInstructions(options);
            var shaped = ApplyRuntimeShaping(instructions, options);
            runtimeInstructions = string.IsNullOrWhiteSpace(shaped) ? null : shaped;
            if (clientOptions.TransportKind == OpenAITransportKind.Native && !string.IsNullOrWhiteSpace(runtimeInstructions)) {
                clientOptions.NativeOptions.Instructions = runtimeInstructions!;
            }

            if (clientOptions.TransportKind == OpenAITransportKind.CompatibleHttp) {
                clientOptions.CompatibleHttpOptions.BaseUrl = options.OpenAIBaseUrl;
                clientOptions.CompatibleHttpOptions.ApiKey = options.OpenAIApiKey;
                clientOptions.CompatibleHttpOptions.Streaming = options.OpenAIStreaming;
                clientOptions.CompatibleHttpOptions.AllowInsecureHttp = options.OpenAIAllowInsecureHttp;
                clientOptions.CompatibleHttpOptions.AllowInsecureHttpNonLoopback = options.OpenAIAllowInsecureHttpNonLoopback;
            }

            var authPath = ResolveAuthPath(options);
            if (!string.IsNullOrWhiteSpace(authPath)) {
                clientOptions.NativeOptions.AuthStore = new FileAuthBundleStore(authPath);
            }

            var nextClient = await IntelligenceXClient.ConnectAsync(clientOptions).ConfigureAwait(false);

            try {
                if (nextClient.TransportKind == OpenAITransportKind.Native) {
                    if (!options.ForceLogin && await TryUseCachedChatGptLoginAsync(nextClient, cancellationToken).ConfigureAwait(false)) {
                        Console.WriteLine("ChatGPT login: using cached token.");
                        Console.WriteLine();
                    } else {
                        await nextClient.LoginChatGptAndWaitAsync(
                            onUrl: url => {
                                Console.WriteLine("ChatGPT login required. Open this URL in a browser:");
                                Console.WriteLine(url);
                                Console.WriteLine();
                                Console.WriteLine("After login, you may be redirected to a localhost URL.");
                                Console.WriteLine("If the app doesn't auto-complete the login, copy the final redirect URL (or the code) and paste it here.");
                                Console.WriteLine();
                            },
                            onPrompt: prompt => PromptForAuthAsync(prompt, cancellationToken),
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                }
            } catch {
                try {
                    await nextClient.DisposeAsync().ConfigureAwait(false);
                } catch {
                    // Ignore.
                }
                throw;
            }

            var nextRegistryContext = BuildHostToolRegistry(
                nextPacks,
                runtimePolicyContext.Options.RequireExplicitRoutingMetadata);
            var nextRegistry = nextRegistryContext.Registry;
            var runtimePolicyDiagnostics = ToolRuntimePolicyBootstrap.ApplyToRegistry(nextRegistry, runtimePolicyContext);
            var nextRoutingCatalogDiagnostics = nextRegistryContext.RoutingCatalogDiagnostics;
            var runtimeCapabilitySnapshot = BuildHostCapabilitySnapshot(
                options.AllowedRoots.Count,
                nextRegistry.GetDefinitions(),
                nextBootstrapResult.PackAvailability,
                nextBootstrapResult.PluginAvailability,
                nextRoutingCatalogDiagnostics,
                nextRegistryContext.OrchestrationCatalog,
                nextBootstrapResult.PluginCatalog);
            var nextSession = new ReplSession(
                nextClient,
                nextRegistry,
                options,
                runtimeInstructions,
                statusWriter,
                nextRegistryContext.OrchestrationCatalog);

            if (client is not null) {
                try {
                    await client.DisposeAsync().ConfigureAwait(false);
                } catch {
                    // Ignore.
                }
            }

            packs = nextPacks;
            client = nextClient;
            registry = nextRegistry;
            session = nextSession;
            runtimePackAvailability = nextBootstrapResult.PackAvailability;
            runtimePluginAvailability = nextBootstrapResult.PluginAvailability;
            runtimePluginCatalog = nextBootstrapResult.PluginCatalog;
            runtimeRoutingCatalogDiagnostics = nextRoutingCatalogDiagnostics;
            runtimeOrchestrationCatalog = nextRegistryContext.OrchestrationCatalog;

            Console.WriteLine($"Registered tools: {registry.GetDefinitions().Count}");
            var formattedRuntimePackWarnings = BuildFormattedPackWarnings(runtimePackWarnings, runtimePackAvailability);
            if (formattedRuntimePackWarnings.Count > 0) {
                foreach (var warning in formattedRuntimePackWarnings) {
                    Console.WriteLine($"[pack warning] {warning}");
                }
            }
            Console.WriteLine($"Routing catalog: {ToolRoutingCatalogDiagnosticsBuilder.FormatSummary(nextRoutingCatalogDiagnostics)}");
            Console.WriteLine($"Capability snapshot: {FormatCapabilitySnapshotSummary(runtimeCapabilitySnapshot)}");
            var runtimeCapabilityHighlights = BuildCapabilitySnapshotHighlights(runtimeCapabilitySnapshot);
            if (runtimeCapabilityHighlights.Count > 0) {
                foreach (var highlight in runtimeCapabilityHighlights) {
                    Console.WriteLine($"[capability] {highlight}");
                }
            }
            var runtimeRoutingWarnings = ToolRoutingCatalogDiagnosticsBuilder.BuildWarnings(nextRoutingCatalogDiagnostics, maxWarnings: 12);
            if (runtimeRoutingWarnings.Count > 0) {
                foreach (var warning in runtimeRoutingWarnings) {
                    Console.WriteLine($"[routing warning] {warning}");
                }
            }
            Console.WriteLine(
                $"Runtime policy: write_mode={ToolRuntimePolicyBootstrap.FormatWriteGovernanceMode(runtimePolicyDiagnostics.WriteGovernanceMode)}, " +
                $"audit_sink={ToolRuntimePolicyBootstrap.FormatWriteAuditSinkMode(runtimePolicyDiagnostics.WriteAuditSinkMode)}, " +
                $"explicit_routing_required={(runtimePolicyDiagnostics.RequireExplicitRoutingMetadata ? "on" : "off")}, " +
                $"auth_preset={ToolRuntimePolicyBootstrap.FormatAuthenticationRuntimePreset(runtimePolicyDiagnostics.AuthenticationPreset)}");

            Console.WriteLine("Commands: /help, /tools, /toolsjson, /toolhealth [filters], /roots, /profiles, /profile <name>, /models, /model <name>, /favorites, /favorite <model>, /unfavorite <model>, /compare <p1,p2,...> -- <prompt>, /exit");
            Console.WriteLine();
        }

        try {
            await BuildRuntimeAsync().ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(options.ScenarioFile)) {
                return await RunScenarioFileAsync(session!, options, cancellationToken).ConfigureAwait(false);
            }

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                Console.Write("ix> ");
                var line = Console.ReadLine();
                if (line is null) {
                    break;
                }

                line = line.Trim();
                if (line.Length == 0) {
                    continue;
                }

                if (line.StartsWith("/", StringComparison.Ordinal)) {
                    var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var cmd = parts.Length == 0 ? string.Empty : parts[0].Trim().ToLowerInvariant();
                    var arg = parts.Length > 1 ? parts[1].Trim() : null;

                    switch (cmd) {
                        case "/exit":
                        case "/quit":
                            return 0;
                        case "/help":
                            WriteHelp();
                            continue;
                        case "/tools":
                            if (runtimeRoutingCatalogDiagnostics is not null && runtimeOrchestrationCatalog is not null) {
                                var toolInspectionLines = BuildToolsInspectionLines(
                                    options.AllowedRoots.Count,
                                    registry!.GetDefinitions(),
                                    runtimePackAvailability,
                                    runtimePluginAvailability,
                                    runtimeRoutingCatalogDiagnostics,
                                    runtimeOrchestrationCatalog,
                                    options.ShowToolIds,
                                    runtimePluginCatalog);
                                foreach (var toolInspectionLine in toolInspectionLines) {
                                    Console.WriteLine(toolInspectionLine);
                                }
                            } else {
                                foreach (var def in registry!.GetDefinitions()) {
                                    var id = options.ShowToolIds ? $" ({def.Name})" : string.Empty;
                                    Console.WriteLine($"- {GetToolDisplayName(def.Name)}{id}: {def.Description}");
                                }
                            }
                            continue;
                        case "/toolsjson":
                            if (runtimeRoutingCatalogDiagnostics is not null && runtimeOrchestrationCatalog is not null) {
                                var exportMessage = BuildHostToolsExportMessage(
                                    options.AllowedRoots.Count,
                                    registry!.GetDefinitions(),
                                    runtimePackAvailability,
                                    runtimePluginAvailability,
                                    runtimeRoutingCatalogDiagnostics,
                                    runtimeOrchestrationCatalog,
                                    runtimePluginCatalog);
                                var exportJson = JsonSerializer.Serialize(exportMessage, ChatServiceJsonContext.Default.ToolListMessage);
                                Console.WriteLine(exportJson);
                            } else {
                                Console.WriteLine("{\"kind\":\"response\",\"requestId\":\"host.toolsjson\",\"tools\":[]}");
                            }
                            continue;
                        case "/toolhealth":
                            await RunToolHealthAsync(registry!, packs, options, arg, cancellationToken).ConfigureAwait(false);
                            continue;
                        case "/roots":
                            if (options.AllowedRoots.Count == 0) {
                                Console.WriteLine("(none)");
                                Console.WriteLine("Tip: pass --allow-root <PATH> to enable file/evtx tools.");
                            } else {
                                foreach (var root in options.AllowedRoots) {
                                    Console.WriteLine(root);
                                }
                            }
                            continue;
                        case "/profiles": {
                                var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                using var store = new SqliteServiceProfileStore(dbPath);
                                var names = await store.ListNamesAsync(cancellationToken).ConfigureAwait(false);
                                if (names.Count == 0) {
                                    Console.WriteLine("(no profiles)");
                                } else {
                                    foreach (var name in names) {
                                        var active = string.Equals(options.ProfileName, name, StringComparison.OrdinalIgnoreCase) ? " *" : string.Empty;
                                        Console.WriteLine($"{name}{active}");
                                    }
                                }
                                continue;
                            }
                        case "/profile": {
                                if (string.IsNullOrWhiteSpace(arg)) {
                                    Console.WriteLine(string.IsNullOrWhiteSpace(options.ProfileName) ? "(none)" : options.ProfileName);
                                    continue;
                                }

                                var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                using var store = new SqliteServiceProfileStore(dbPath);
                                var profile = await store.GetAsync(arg, cancellationToken).ConfigureAwait(false);
                                if (profile is null) {
                                    Console.WriteLine($"Profile not found: {arg}");
                                    continue;
                                }

                                var previousOptions = options.Clone();
                                try {
                                    options.ApplyProfile(profile);
                                    options.ProfileName = arg;

                                    var profilePackWarnings = new List<string>();
                                    var profileRuntimePolicyContext = ToolRuntimePolicyBootstrap.CreateContext(
                                        BuildRuntimePolicyOptions(options),
                                        warning => CollectPackWarning(profilePackWarnings, warning));
                                    var profileRuntimePolicyDiagnostics = ToolRuntimePolicyBootstrap.BuildDiagnostics(profileRuntimePolicyContext);
                                    var profileBootstrapResult = BuildPackBootstrapResult(
                                        options,
                                        profileRuntimePolicyContext,
                                        warning => CollectPackWarning(profilePackWarnings, warning));
                                    var profilePacks = profileBootstrapResult.Packs;
                                    var profileRegistryContext = BuildHostToolRegistry(
                                        profilePacks,
                                        requireExplicitRoutingMetadata: options.RequireExplicitRoutingMetadata);
                                    WritePolicyBanner(
                                        options,
                                        profilePacks,
                                        profileBootstrapResult.PackAvailability,
                                        profileBootstrapResult.PluginAvailability,
                                        profileBootstrapResult.PluginCatalog,
                                        profileRuntimePolicyContext,
                                        profileRuntimePolicyDiagnostics,
                                        profileRegistryContext.RoutingCatalogDiagnostics,
                                        profileRegistryContext.OrchestrationCatalog,
                                        profilePackWarnings);
                                    Console.WriteLine();

                                    await BuildRuntimeAsync().ConfigureAwait(false);
                                    session!.ResetThread();
                                    Console.WriteLine($"Switched profile: {arg}");
                                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                                    options.CopyFrom(previousOptions);
                                    throw;
                                } catch (Exception ex) {
                                    options.CopyFrom(previousOptions);
                                    Console.WriteLine($"Profile switch failed: {ex.Message}");
                                }
                                continue;
                            }
                        case "/models": {
                                var models = await client!.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                                HashSet<string>? favorites = null;
                                if (!string.IsNullOrWhiteSpace(options.ProfileName)) {
                                    try {
                                        var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                        using var prefs = new SqliteModelPreferencesStore(dbPath);
                                        var favs = await prefs.ListFavoritesAsync(options.ProfileName!, cancellationToken).ConfigureAwait(false);
                                        favorites = favs.Count == 0 ? null : new HashSet<string>(favs, StringComparer.OrdinalIgnoreCase);
                                    } catch {
                                        favorites = null;
                                    }
                                }
                                if (models.Models.Count == 0) {
                                    Console.WriteLine("(no models returned)");
                                } else {
                                    foreach (var m in models.Models) {
                                        var title = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Model : m.DisplayName;
                                        var marker = m.IsDefault ? " (default)" : string.Empty;
                                        var fav = favorites is not null && favorites.Contains(m.Model) ? " (fav)" : string.Empty;
                                        Console.WriteLine($"- {title} [{m.Model}]{marker}{fav}");
                                    }
                                }
                                continue;
                            }
                        case "/model": {
                                if (string.IsNullOrWhiteSpace(arg)) {
                                    Console.WriteLine(options.Model);
                                    continue;
                                }
                                options.Model = arg;
                                client!.ConfigureDefaults(model: options.Model);
                                session!.ResetThread();
                                Console.WriteLine($"Model set to: {options.Model} (new thread)");

                                if (!string.IsNullOrWhiteSpace(options.ProfileName)) {
                                    try {
                                        var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                        using var prefs = new SqliteModelPreferencesStore(dbPath);
                                        await prefs.RecordRecentAsync(options.ProfileName!, options.Model, maxRecentsPerProfile: 50, cancellationToken).ConfigureAwait(false);
                                    } catch {
                                        // Best-effort.
                                    }
                                }
                                continue;
                            }
                        case "/favorites": {
                                if (string.IsNullOrWhiteSpace(options.ProfileName)) {
                                    Console.WriteLine("No active profile. Use --profile or /profile <name> first.");
                                    continue;
                                }

                                var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                using var prefs = new SqliteModelPreferencesStore(dbPath);
                                var favs = await prefs.ListFavoritesAsync(options.ProfileName!, cancellationToken).ConfigureAwait(false);
                                if (favs.Count == 0) {
                                    Console.WriteLine("(no favorites)");
                                } else {
                                    foreach (var m in favs) {
                                        Console.WriteLine(m);
                                    }
                                }
                                continue;
                            }
                        case "/favorite": {
                                if (string.IsNullOrWhiteSpace(options.ProfileName)) {
                                    Console.WriteLine("No active profile. Use --profile or /profile <name> first.");
                                    continue;
                                }
                                if (string.IsNullOrWhiteSpace(arg)) {
                                    Console.WriteLine("Usage: /favorite <model>");
                                    continue;
                                }

                                var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                using var prefs = new SqliteModelPreferencesStore(dbPath);
                                await prefs.SetFavoriteAsync(options.ProfileName!, arg, isFavorite: true, cancellationToken).ConfigureAwait(false);
                                Console.WriteLine($"Favorited: {arg}");
                                continue;
                            }
                        case "/unfavorite": {
                                if (string.IsNullOrWhiteSpace(options.ProfileName)) {
                                    Console.WriteLine("No active profile. Use --profile or /profile <name> first.");
                                    continue;
                                }
                                if (string.IsNullOrWhiteSpace(arg)) {
                                    Console.WriteLine("Usage: /unfavorite <model>");
                                    continue;
                                }

                                var dbPath = string.IsNullOrWhiteSpace(options.StateDbPath) ? ReplOptions.GetDefaultStateDbPath() : options.StateDbPath!.Trim();
                                using var prefs = new SqliteModelPreferencesStore(dbPath);
                                await prefs.SetFavoriteAsync(options.ProfileName!, arg, isFavorite: false, cancellationToken).ConfigureAwait(false);
                                Console.WriteLine($"Unfavorited: {arg}");
                                continue;
                            }
                        case "/compare":
                            await HandleCompareCommandAsync(arg, options, cancellationToken).ConfigureAwait(false);
                            continue;
                        default:
                            Console.WriteLine("Unknown command. Try /help.");
                            continue;
                    }
                }

                try {
                    var result = await session!.AskAsync(line, cancellationToken).ConfigureAwait(false);
                    WriteTurnResult(result, options);
                    Console.WriteLine();
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (Exception ex) {
                    WriteTurnFailure(ex);
                    Console.WriteLine();
                }
            }

            return 0;
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

    private static void WriteTurnResult(ReplTurnResult result, ReplOptions options) {
        // Tool calls may be printed live; keep the final output clean by default.
        if (!options.LiveProgress && result.ToolCalls.Count > 0) {
            foreach (var call in result.ToolCalls) {
                var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                var id = options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                Console.WriteLine($"> tool: {GetToolDisplayName(call.Name)}{id} args={args}");
            }
        }

        if (result.ToolOutputs.Count > 0 && options.EchoToolOutputs) {
            foreach (var output in result.ToolOutputs) {
                var text = options.Redact ? RedactText(output.Output) : output.Output;
                var truncated = Truncate(text, options.MaxConsoleToolOutputChars);
                Console.WriteLine($"> tool output: {output.CallId} ({truncated.Length} chars)");
                Console.WriteLine(truncated);
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Text)) {
            var text = options.Redact ? RedactText(result.Text) : result.Text;
            Console.WriteLine(text);
        }
    }

    private static void WriteTurnFailure(Exception ex) {
        var message = (ex.Message ?? "Turn failed.").Trim();
        if (message.Length == 0) {
            message = "Turn failed.";
        }

        Console.WriteLine("Turn failed: " + message);
        if (LooksLikeContextWindowFailure(ex)) {
            Console.WriteLine("Hint: local model context window is too small for current instructions + tool catalog.");
            Console.WriteLine("Try a larger-context model, or reduce enabled tool packs.");
        }
    }

    private static bool LooksLikeContextWindowFailure(Exception ex) {
        var text = (ex.ToString() ?? string.Empty).ToLowerInvariant();
        if (text.Contains("cannot truncate prompt with n_keep")
            || text.Contains("n_ctx")) {
            return true;
        }

        return text.Contains("context length")
               || text.Contains("maximum context length");
    }

    private static string Truncate(string value, int maxChars) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        if (maxChars <= 0 || value.Length <= maxChars) {
            return value;
        }
        return value.Substring(0, maxChars);
    }

    private static void WriteHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  IntelligenceX.Chat.Host [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine($"  --model <NAME>          OpenAI model (default: {OpenAIModelCatalog.DefaultModel})");
        Console.WriteLine("  --reasoning-effort <LEVEL>   Reasoning effort hint: minimal|low|medium|high|xhigh.");
        Console.WriteLine("  --reasoning-summary <LEVEL>  Reasoning summary hint: auto|concise|detailed|off.");
        Console.WriteLine("  --text-verbosity <LEVEL>     Text verbosity hint: low|medium|high.");
        Console.WriteLine("  --temperature <N>       Sampling temperature (0-2).");
        Console.WriteLine("  --openai-transport <KIND>  Provider transport: native|appserver|compatible-http|copilot-cli (default: native).");
        Console.WriteLine("  --openai-base-url <URL> Base URL for compatible-http (example: http://127.0.0.1:11434 or http://127.0.0.1:11434/v1).");
        Console.WriteLine("  --openai-api-key <KEY>  Optional Bearer token for compatible-http.");
        Console.WriteLine("  --openai-stream         Request streaming responses (default: on).");
        Console.WriteLine("  --openai-no-stream      Disable streaming responses.");
        Console.WriteLine("  --openai-allow-insecure-http  Allow http:// base URLs for loopback hosts (default: off).");
        Console.WriteLine("  --openai-allow-insecure-http-non-loopback  Allow http:// base URLs for non-loopback hosts (dangerous).");
        Console.WriteLine("  --profile <NAME>        Load a saved profile or built-in preset (for example: plugin-only).");
        Console.WriteLine("  --state-db <PATH>       Override the SQLite state DB path (defaults to LocalAppData).");
        Console.WriteLine("  --allow-root <PATH>     Allow filesystem/evtx operations under PATH (repeatable).");
        Console.WriteLine("  --auth-path <PATH>      Override auth store path (default: %USERPROFILE%\\.intelligencex\\auth.json).");
        Console.WriteLine("  --instructions-file <PATH>  Load system instructions from a file (default: bundled HostSystemPrompt.md).");
        Console.WriteLine("  --scenario-file <PATH>  Run a non-interactive multi-turn scenario (JSON or line-based text) and exit.");
        Console.WriteLine("  --scenario-output <PATH>  Write scenario run report markdown (default: artifacts/chat-scenarios).");
        Console.WriteLine("  --scenario-continue-on-error  Continue remaining scenario turns after a failed turn/assertion.");
        Console.WriteLine("  --ad-domain-controller  Active Directory domain controller host/FQDN (optional).");
        Console.WriteLine("  --ad-search-base        Active Directory base DN (optional; defaultNamingContext used otherwise).");
        Console.WriteLine("  --ad-max-results <N>    Max results returned by AD tools (default: 1000).");
        Console.WriteLine("  --enable-pack-id <ID>   Enable a tool pack by normalized pack id (repeatable).");
        Console.WriteLine("  --disable-pack-id <ID>  Disable a tool pack by normalized pack id (repeatable).");
        Console.WriteLine("                          Pack ids come from runtime metadata (built-in + plugin packs).");
        Console.WriteLine("  --powershell-allow-write  Allow read_write intent in IX.PowerShell tools (default: off).");
        Console.WriteLine("  --no-built-in-packs    Disable built-in pack loading (plugin-only mode).");
        Console.WriteLine("  --built-in-packs       Enable built-in pack loading (default: on).");
        Console.WriteLine("  --built-in-tool-assembly <NAME> Additional built-in tool assembly name to include (repeatable).");
        Console.WriteLine("  --built-in-tool-probe-path <PATH> Runtime-only built-in tool assembly probe root (repeatable).");
        Console.WriteLine("  --no-default-built-in-tool-assemblies Disable built-in discovery from Chat's default assembly allowlist.");
        Console.WriteLine("  --default-built-in-tool-assemblies Re-enable built-in discovery from Chat's default assembly allowlist.");
        Console.WriteLine("  --plugin-path <PATH>    Additional folder-based plugin path (repeatable).");
        Console.WriteLine("  --no-default-plugin-paths Disable default plugin paths (%LOCALAPPDATA% and app ./plugins).");
        ToolRuntimePolicyBootstrap.WriteRuntimePolicyCliHelp(Console.WriteLine);
        Console.WriteLine("  --max-table-rows <N>    Max rows to show in table-like output (0 = no limit; default: 0).");
        Console.WriteLine("  --max-sample <N>        Max sample items to show from long lists (0 = no limit; default: 0).");
        Console.WriteLine("  --redact                Best-effort redact output for display/logging (default: off).");
        Console.WriteLine(BuildMaxToolRoundsHelpLine());
        Console.WriteLine("  --parallel-tools        Execute tool calls in parallel when possible (default: on).");
        Console.WriteLine("  --no-parallel-tools     Disable parallel tool calls.");
        Console.WriteLine("  --allow-mutating-parallel-tools  Allow parallel execution for write-capable tool calls (default: off).");
        Console.WriteLine("  --disallow-mutating-parallel-tools  Disable mutating parallel override.");
        Console.WriteLine("  --turn-timeout-seconds <N>  Per-turn timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --tool-timeout-seconds <N>  Per-tool timeout in seconds (0 = no timeout; default: 0).");
        Console.WriteLine("  --echo-tool-outputs     Print tool outputs to console (default: off).");
        Console.WriteLine("  --max-tool-output <N>   Max chars printed per tool output (default: 2000).");
        Console.WriteLine("  --show-tool-ids         Show raw tool ids in output (default: off).");
        Console.WriteLine("  --no-progress           Disable live progress lines (default: on).");
        Console.WriteLine("  --login                 Force ChatGPT login even if a cached token exists.");
        Console.WriteLine("  -h, --help              Show help.");
        Console.WriteLine();
        Console.WriteLine("REPL commands:");
        Console.WriteLine("  /help, /tools, /toolsjson, /toolhealth [filters], /roots, /profiles, /profile <name>, /models, /model <name>, /favorites, /favorite <model>, /unfavorite <model>, /compare <p1,p2,...> -- <prompt>, /exit");
        Console.WriteLine("  /toolsjson emits the lightweight tool catalog/autonomy snapshot as JSON.");
        Console.WriteLine("  /toolhealth filters: open|closed|private|builtin|pack:<id> (repeatable)");
    }

    internal static string BuildMaxToolRoundsHelpLine() {
        return
            $"  --max-tool-rounds <N>   Max tool-call rounds per user message ({ChatRequestOptionLimits.MinToolRounds}..{ChatRequestOptionLimits.MaxToolRounds}; default: {ChatRequestOptionLimits.DefaultToolRounds}).";
    }

    private static string? LoadInstructions(ReplOptions options) {
        var path = options.InstructionsFile;
        if (string.IsNullOrWhiteSpace(path)) {
            path = Path.Combine(AppContext.BaseDirectory, "HostSystemPrompt.md");
        }
        try {
            if (!File.Exists(path)) {
                return null;
            }
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load instructions from '{path}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveAuthPath(ReplOptions options) {
        if (!string.IsNullOrWhiteSpace(options.AuthPath)) {
            return options.AuthPath.Trim();
        }
        // Keep in sync with IntelligenceX default (AuthPaths.ResolveAuthPath).
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            return null;
        }
        return Path.Combine(home, ".intelligencex", "auth.json");
    }

    private static async Task<bool> TryUseCachedChatGptLoginAsync(IntelligenceXClient client, CancellationToken cancellationToken) {
        try {
            _ = await client.GetAccountAsync(cancellationToken).ConfigureAwait(false);
            return true;
        } catch (OperationCanceledException) {
            throw;
        } catch (OpenAIAuthenticationRequiredException) {
            return false;
        } catch (Exception ex) {
            // Any other failure means the cache isn't usable (corrupt store, token parse issue, etc.).
            // We still allow a best-effort interactive login, but surface the error for debugging.
            Console.Error.WriteLine($"ChatGPT cached login could not be used: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static async Task<string> PromptForAuthAsync(string prompt, CancellationToken cancellationToken) {
        while (true) {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(prompt)) {
                Console.WriteLine(prompt.TrimEnd());
            }

            Console.Write("> ");
            var line = await Task.Run(Console.ReadLine, cancellationToken).ConfigureAwait(false);
            line = line?.Trim() ?? string.Empty;

            if (line.Length == 0) {
                Console.WriteLine("Paste the redirect URL or authorization code, or type 'cancel' to abort.");
                continue;
            }

            if (string.Equals(line, "cancel", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(line, "exit", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(line, "quit", StringComparison.OrdinalIgnoreCase)) {
                throw new OpenAIUserCanceledLoginException();
            }

            return line;
        }
    }

}

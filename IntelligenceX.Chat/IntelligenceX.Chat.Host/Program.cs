using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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

internal static class Program {
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
        Console.WriteLine($"Model: {options.Model}");
        Console.WriteLine($"Parallel tool calls: {options.ParallelToolCalls}");
        Console.WriteLine($"Max tool rounds: {options.MaxToolRounds}");
        Console.WriteLine($"Turn timeout: {(options.TurnTimeoutSeconds <= 0 ? "(none)" : $"{options.TurnTimeoutSeconds}s")}");
        Console.WriteLine($"Tool timeout: {(options.ToolTimeoutSeconds <= 0 ? "(none)" : $"{options.ToolTimeoutSeconds}s")}");
        Console.WriteLine($"Max table rows: {(options.MaxTableRows <= 0 ? "(none)" : options.MaxTableRows)}");
        Console.WriteLine($"Max sample items: {(options.MaxSample <= 0 ? "(none)" : options.MaxSample)}");
        Console.WriteLine($"Redaction: {(options.Redact ? "on" : "off")}");
        Console.WriteLine($"IX.PowerShell pack: {(options.EnablePowerShellPack ? "enabled (dangerous)" : "disabled")}");
        Console.WriteLine($"IX.TestimoX pack: {(options.EnableTestimoXPack ? "enabled" : "disabled")}");
        Console.WriteLine($"Allowed roots: {(options.AllowedRoots.Count == 0 ? "(none)" : string.Join("; ", options.AllowedRoots))}");
        var authPath = ResolveAuthPath(options);
        if (!string.IsNullOrWhiteSpace(authPath)) {
            Console.WriteLine($"Auth store: {authPath}");
        }

        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions {
            AllowedRoots = options.AllowedRoots.ToArray(),
            AdDomainController = options.AdDomainController,
            AdDefaultSearchBaseDn = options.AdDefaultSearchBaseDn,
            AdMaxResults = options.AdMaxResults,
            EnablePowerShellPack = options.EnablePowerShellPack,
            EnableTestimoXPack = options.EnableTestimoXPack
        });
        WritePolicyBanner(options, packs);
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };

        try {
            await RunAsync(options, packs, cts.Token).ConfigureAwait(false);
            return 0;
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

    private static async Task RunAsync(ReplOptions options, IReadOnlyList<IToolPack> packs, CancellationToken cancellationToken) {
        var clientOptions = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.Native
        };
        var instructions = LoadInstructions(options);
        var shaped = ApplyRuntimeShaping(instructions, options);
        if (!string.IsNullOrWhiteSpace(shaped)) {
            clientOptions.NativeOptions.Instructions = shaped!;
        }
        var authPath = ResolveAuthPath(options);
        if (!string.IsNullOrWhiteSpace(authPath)) {
            clientOptions.NativeOptions.AuthStore = new FileAuthBundleStore(authPath);
        }

        await using var client = await IntelligenceXClient.ConnectAsync(clientOptions).ConfigureAwait(false);

        if (!options.ForceLogin && await TryUseCachedChatGptLoginAsync(client, cancellationToken).ConfigureAwait(false)) {
            Console.WriteLine("ChatGPT login: using cached token.");
            Console.WriteLine();
        } else {
            await client.LoginChatGptAndWaitAsync(
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

        var registry = new ToolRegistry();
        ToolPackBootstrap.RegisterAll(registry, packs);

        Console.WriteLine($"Registered tools: {registry.GetDefinitions().Count}");
        Console.WriteLine("Commands: /help, /tools, /roots, /exit");
        Console.WriteLine();

        var session = new ReplSession(client, registry, options, status => {
            // Keep progress lines visually distinct from assistant output.
            if (!string.IsNullOrWhiteSpace(status)) {
                Console.WriteLine($"> {status}");
            }
        });
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
                if (HandleCommand(line, registry, options)) {
                    break;
                }
                continue;
            }

            var result = await session.AskAsync(line, cancellationToken).ConfigureAwait(false);
            WriteTurnResult(result, options);
            Console.WriteLine();
        }
    }

    private static bool HandleCommand(string input, ToolRegistry registry, ReplOptions options) {
        switch (input.Trim().ToLowerInvariant()) {
            case "/exit":
            case "/quit":
                return true;
            case "/help":
                WriteHelp();
                return false;
            case "/tools":
                foreach (var def in registry.GetDefinitions()) {
                    var id = options.ShowToolIds ? $" ({def.Name})" : string.Empty;
                    Console.WriteLine($"- {GetToolDisplayName(def.Name)}{id}: {def.Description}");
                }
                return false;
            case "/roots":
                if (options.AllowedRoots.Count == 0) {
                    Console.WriteLine("(none)");
                    Console.WriteLine("Tip: pass --allow-root <PATH> to enable file/evtx tools.");
                } else {
                    foreach (var root in options.AllowedRoots) {
                        Console.WriteLine(root);
                    }
                }
                return false;
            default:
                Console.WriteLine("Unknown command. Try /help.");
                return false;
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

    private static string Truncate(string value, int maxChars) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        if (maxChars <= 0 || value.Length <= maxChars) {
            return value;
        }
        return value.Substring(0, maxChars);
    }

    private static string GetToolDisplayName(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return string.Empty;
        }

        // Keep stable tool ids (machine-friendly), but display a friendlier title for humans.
        var (prefix, suffix) = SplitPrefix(toolName.Trim());
        var group = prefix switch {
            "ad" => "Active Directory",
            "eventlog" => "Event Log",
            "system" => "System",
            "fs" => "File System",
            "wsl" => "System",
            "testimox" => "TestimoX",
            _ => string.Empty
        };

        var title = ToTitle(suffix);
        if (toolName.Equals("wsl_status", StringComparison.OrdinalIgnoreCase)) {
            title = "WSL Status";
        }

        return string.IsNullOrWhiteSpace(group) ? title : $"{group} / {title}";
    }

    private static (string Prefix, string Suffix) SplitPrefix(string toolName) {
        var idx = toolName.IndexOf('_');
        if (idx <= 0 || idx == toolName.Length - 1) {
            return (toolName, toolName);
        }
        return (toolName.Substring(0, idx), toolName.Substring(idx + 1));
    }

    private static string ToTitle(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var parts = value.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < parts.Length; i++) {
            if (i > 0) {
                sb.Append(' ');
            }

            var p = parts[i];
            if (IsAcronym(p, out var acronym)) {
                sb.Append(acronym);
                continue;
            }

            if (p.Length == 1) {
                sb.Append(char.ToUpperInvariant(p[0]));
                continue;
            }

            sb.Append(char.ToUpperInvariant(p[0]));
            sb.Append(p.Substring(1));
        }
        return sb.ToString();
    }

    private static bool IsAcronym(string value, out string acronym) {
        acronym = value;
        if (value.Length == 0) {
            return false;
        }

        // Common acronyms used across tools.
        switch (value) {
            case var _ when string.Equals(value, "ldap", StringComparison.OrdinalIgnoreCase):
                acronym = "LDAP";
                return true;
            case var _ when string.Equals(value, "spn", StringComparison.OrdinalIgnoreCase):
                acronym = "SPN";
                return true;
            case var _ when string.Equals(value, "evtx", StringComparison.OrdinalIgnoreCase):
                acronym = "EVTX";
                return true;
            case var _ when string.Equals(value, "wsl", StringComparison.OrdinalIgnoreCase):
                acronym = "WSL";
                return true;
            case var _ when string.Equals(value, "utc", StringComparison.OrdinalIgnoreCase):
                acronym = "UTC";
                return true;
            default:
                return false;
        }
    }

    private static void WriteHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  IntelligenceX.Chat.Host [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --model <NAME>          OpenAI model (default: gpt-5.3-codex)");
        Console.WriteLine("  --allow-root <PATH>     Allow filesystem/evtx operations under PATH (repeatable).");
        Console.WriteLine("  --auth-path <PATH>      Override auth store path (default: %USERPROFILE%\\.intelligencex\\auth.json).");
        Console.WriteLine("  --instructions-file <PATH>  Load system instructions from a file (default: bundled HostSystemPrompt.md).");
        Console.WriteLine("  --ad-domain-controller  Active Directory domain controller host/FQDN (optional).");
        Console.WriteLine("  --ad-search-base        Active Directory base DN (optional; defaultNamingContext used otherwise).");
        Console.WriteLine("  --ad-max-results <N>    Max results returned by AD tools (default: 1000).");
        Console.WriteLine("  --enable-powershell-pack  Enable dangerous IX.PowerShell runtime tools (default: off).");
        Console.WriteLine("  --enable-testimox-pack  Enable IX.TestimoX diagnostics tools (default: on).");
        Console.WriteLine("  --disable-testimox-pack Disable IX.TestimoX diagnostics tools.");
        Console.WriteLine("  --max-table-rows <N>    Max rows to show in table-like output (0 = no limit; default: 20).");
        Console.WriteLine("  --max-sample <N>        Max sample items to show from long lists (0 = no limit; default: 10).");
        Console.WriteLine("  --redact                Best-effort redact output for display/logging (default: off).");
        Console.WriteLine("  --max-tool-rounds <N>   Max tool-call rounds per user message (default: 3).");
        Console.WriteLine("  --parallel-tools        Execute tool calls in parallel when possible.");
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
        Console.WriteLine("  /help, /tools, /roots, /exit");
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

    private sealed class ReplSession {
        private readonly IntelligenceXClient _client;
        private readonly ToolRegistry _registry;
        private readonly ReplOptions _options;
        private readonly Action<string>? _status;
        private string? _previousResponseId;

        public ReplSession(IntelligenceXClient client, ToolRegistry registry, ReplOptions options, Action<string>? status) {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _status = status;
        }

        public async Task<ReplTurnResult> AskAsync(string text, CancellationToken cancellationToken) {
            if (string.IsNullOrWhiteSpace(text)) {
                return new ReplTurnResult(string.Empty, Array.Empty<ToolCall>(), Array.Empty<ToolOutput>());
            }

            using var turnCts = CreateTimeoutCts(cancellationToken, _options.TurnTimeoutSeconds);
            var turnToken = turnCts?.Token ?? cancellationToken;

            var calls = new List<ToolCall>();
            var outputs = new List<ToolOutput>();

            var input = ChatInput.FromText(text);
            var toolDefs = _registry.GetDefinitions();
            var chatOptions = new ChatOptions {
                Model = _options.Model,
                ParallelToolCalls = _options.ParallelToolCalls,
                Tools = toolDefs.Count == 0 ? null : toolDefs,
                ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto,
                PreviousResponseId = _previousResponseId,
                NewThread = string.IsNullOrWhiteSpace(_previousResponseId)
            };

            if (_options.LiveProgress) {
                _status?.Invoke("thinking...");
            }
            TurnInfo turn = await _client.ChatAsync(input, chatOptions, turnToken).ConfigureAwait(false);

            for (var round = 0; round < Math.Max(1, _options.MaxToolRounds); round++) {
                var extracted = ToolCallParser.Extract(turn);
                if (extracted.Count == 0) {
                    var finalText = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                    _previousResponseId = TryGetResponseId(turn) ?? _previousResponseId;
                    return new ReplTurnResult(finalText, calls, outputs);
                }

                calls.AddRange(extracted);
                if (_options.LiveProgress) {
                    foreach (var call in extracted) {
                        var args = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments);
                        var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                        _status?.Invoke($"tool: {GetToolDisplayName(call.Name)}{id} args={args}");
                    }
                }
                var executed = await ExecuteToolsAsync(extracted, turnToken).ConfigureAwait(false);
                outputs.AddRange(executed);

                var next = new ChatInput();
                foreach (var output in executed) {
                    next.AddToolOutput(output.CallId, output.Output);
                }

                chatOptions.NewThread = false;
                chatOptions.PreviousResponseId = TryGetResponseId(turn);
                if (_options.LiveProgress) {
                    _status?.Invoke("thinking...");
                }
                turn = await _client.ChatAsync(next, chatOptions, turnToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException($"Tool runner exceeded max rounds ({_options.MaxToolRounds}).");
        }

        private async Task<IReadOnlyList<ToolOutput>> ExecuteToolsAsync(IReadOnlyList<ToolCall> calls, CancellationToken cancellationToken) {
            if (!_options.ParallelToolCalls || calls.Count <= 1) {
                var outputs = new List<ToolOutput>(calls.Count);
                foreach (var call in calls) {
                    outputs.Add(await ExecuteToolAsync(call, cancellationToken).ConfigureAwait(false));
                }
                return outputs;
            }

            var tasks = new Task<ToolOutput>[calls.Count];
            for (var i = 0; i < calls.Count; i++) {
                tasks[i] = ExecuteToolAsync(calls[i], cancellationToken);
            }
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        private async Task<ToolOutput> ExecuteToolAsync(ToolCall call, CancellationToken cancellationToken) {
            if (!_registry.TryGet(call.Name, out var tool)) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_not_registered",
                    error: $"Tool '{call.Name}' is not registered.",
                    hints: new[] { "Run /tools to list available tools.", "Check that the correct packs are enabled." },
                    isTransient: false));
            }
            if (_options.LiveProgress) {
                var id = _options.ShowToolIds ? $" ({call.Name})" : string.Empty;
                _status?.Invoke($"running: {GetToolDisplayName(call.Name)}{id}");
            }
            using var toolCts = CreateTimeoutCts(cancellationToken, _options.ToolTimeoutSeconds);
            var toolToken = toolCts?.Token ?? cancellationToken;
            try {
                var result = await tool.InvokeAsync(call.Arguments, toolToken).ConfigureAwait(false);
                return new ToolOutput(call.CallId, result ?? string.Empty);
            } catch (OperationCanceledException) when (toolCts is not null && toolCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_timeout",
                    error: $"Tool '{call.Name}' timed out after {_options.ToolTimeoutSeconds}s.",
                    hints: new[] { "Increase --tool-timeout-seconds, or narrow the query (OU scoping, tighter filters)." },
                    isTransient: true));
            } catch (Exception ex) {
                return new ToolOutput(call.CallId, ToolOutputEnvelope.Error(
                    errorCode: "tool_exception",
                    error: $"{ex.GetType().Name}: {ex.Message}",
                    hints: new[] { "Try again. If it keeps failing, re-run with --echo-tool-outputs to capture details." },
                    isTransient: false));
            }
        }

        private static string? TryGetResponseId(TurnInfo turn) {
            if (turn is null) {
                return null;
            }
            if (!string.IsNullOrWhiteSpace(turn.ResponseId)) {
                return turn.ResponseId;
            }
            var responseId = turn.Raw.GetString("responseId");
            if (!string.IsNullOrWhiteSpace(responseId)) {
                return responseId;
            }
            var response = turn.Raw.GetObject("response");
            return response?.GetString("id");
        }
    }

    private sealed class ReplTurnResult {
        public ReplTurnResult(string text, IReadOnlyList<ToolCall> toolCalls, IReadOnlyList<ToolOutput> toolOutputs) {
            Text = text ?? string.Empty;
            ToolCalls = toolCalls ?? Array.Empty<ToolCall>();
            ToolOutputs = toolOutputs ?? Array.Empty<ToolOutput>();
        }

        public string Text { get; }
        public IReadOnlyList<ToolCall> ToolCalls { get; }
        public IReadOnlyList<ToolOutput> ToolOutputs { get; }
    }

    private sealed class ReplOptions {
        public string Model { get; set; } = "gpt-5.3-codex";
        public bool ShowHelp { get; set; }
        public bool ForceLogin { get; set; }
        public bool ParallelToolCalls { get; set; }
        public int MaxToolRounds { get; set; } = 8;
        public int TurnTimeoutSeconds { get; set; }
        public int ToolTimeoutSeconds { get; set; }
        public List<string> AllowedRoots { get; } = new();
        public bool EchoToolOutputs { get; set; }
        public int MaxConsoleToolOutputChars { get; set; } = 2000;
        public bool ShowToolIds { get; set; }
        public bool LiveProgress { get; set; } = true;
        public int MaxTableRows { get; set; } = 20;
        public int MaxSample { get; set; } = 10;
        public bool Redact { get; set; }
        public string? AuthPath { get; set; }
        public string? InstructionsFile { get; set; }
        public string? AdDomainController { get; set; }
        public string? AdDefaultSearchBaseDn { get; set; }
        public int AdMaxResults { get; set; } = 1000;
        public bool EnablePowerShellPack { get; set; }
        public bool EnableTestimoXPack { get; set; } = true;

        public static ReplOptions Parse(string[] args, out string? error) {
            error = null;
            var options = new ReplOptions();
            if (args is null || args.Length == 0) {
                return options;
            }

            for (var i = 0; i < args.Length; i++) {
                var a = args[i] ?? string.Empty;
                switch (a) {
                    case "-h":
                    case "--help":
                        options.ShowHelp = true;
                        return options;
                    case "--model":
                        if (!TryGetValue(args, ref i, out var model, out error)) {
                            return options;
                        }
                        options.Model = model;
                        break;
                    case "--allow-root":
                        if (!TryGetValue(args, ref i, out var root, out error)) {
                            return options;
                        }
                        options.AllowedRoots.Add(root);
                        break;
                    case "--auth-path":
                        if (!TryGetValue(args, ref i, out var authPath, out error)) {
                            return options;
                        }
                        options.AuthPath = authPath;
                        break;
                    case "--instructions-file":
                        if (!TryGetValue(args, ref i, out var instructionsFile, out error)) {
                            return options;
                        }
                        options.InstructionsFile = instructionsFile;
                        break;
                    case "--ad-domain-controller":
                        if (!TryGetValue(args, ref i, out var dc, out error)) {
                            return options;
                        }
                        options.AdDomainController = dc;
                        break;
                    case "--ad-search-base":
                        if (!TryGetValue(args, ref i, out var baseDn, out error)) {
                            return options;
                        }
                        options.AdDefaultSearchBaseDn = baseDn;
                        break;
                    case "--ad-max-results":
                        if (!TryGetValue(args, ref i, out var adMax, out error)) {
                            return options;
                        }
                        if (!int.TryParse(adMax, out var adMaxResults) || adMaxResults <= 0) {
                            error = "Invalid --ad-max-results value.";
                            return options;
                        }
                        options.AdMaxResults = adMaxResults;
                        break;
                    case "--enable-powershell-pack":
                        options.EnablePowerShellPack = true;
                        break;
                    case "--enable-testimox-pack":
                        options.EnableTestimoXPack = true;
                        break;
                    case "--disable-testimox-pack":
                        options.EnableTestimoXPack = false;
                        break;
                    case "--max-table-rows":
                        if (!TryGetValue(args, ref i, out var maxRows, out error)) {
                            return options;
                        }
                        if (!int.TryParse(maxRows, out var mr) || mr < 0) {
                            error = "Invalid --max-table-rows value.";
                            return options;
                        }
                        options.MaxTableRows = mr;
                        break;
                    case "--max-sample":
                        if (!TryGetValue(args, ref i, out var maxSample, out error)) {
                            return options;
                        }
                        if (!int.TryParse(maxSample, out var ms) || ms < 0) {
                            error = "Invalid --max-sample value.";
                            return options;
                        }
                        options.MaxSample = ms;
                        break;
                    case "--redact":
                        options.Redact = true;
                        break;
                    case "--max-tool-rounds":
                        if (!TryGetValue(args, ref i, out var rounds, out error)) {
                            return options;
                        }
                        if (!int.TryParse(rounds, out var n) || n <= 0) {
                            error = "Invalid --max-tool-rounds value.";
                            return options;
                        }
                        options.MaxToolRounds = n;
                        break;
                    case "--parallel-tools":
                        options.ParallelToolCalls = true;
                        break;
                    case "--turn-timeout-seconds":
                        if (!TryGetValue(args, ref i, out var turnTimeout, out error)) {
                            return options;
                        }
                        if (!int.TryParse(turnTimeout, out var tts) || tts < 0) {
                            error = "Invalid --turn-timeout-seconds value.";
                            return options;
                        }
                        options.TurnTimeoutSeconds = tts;
                        break;
                    case "--tool-timeout-seconds":
                        if (!TryGetValue(args, ref i, out var toolTimeout, out error)) {
                            return options;
                        }
                        if (!int.TryParse(toolTimeout, out var ots) || ots < 0) {
                            error = "Invalid --tool-timeout-seconds value.";
                            return options;
                        }
                        options.ToolTimeoutSeconds = ots;
                        break;
                    case "--login":
                        options.ForceLogin = true;
                        break;
                    case "--echo-tool-outputs":
                        options.EchoToolOutputs = true;
                        break;
                    case "--show-tool-ids":
                        options.ShowToolIds = true;
                        break;
                    case "--no-progress":
                        options.LiveProgress = false;
                        break;
                    case "--max-tool-output":
                        if (!TryGetValue(args, ref i, out var maxOut, out error)) {
                            return options;
                        }
                        if (!int.TryParse(maxOut, out var maxChars) || maxChars <= 0) {
                            error = "Invalid --max-tool-output value.";
                            return options;
                        }
                        options.MaxConsoleToolOutputChars = maxChars;
                        break;
                    default:
                        // Allow users to run: `dotnet run -- --model x`
                        if (a.StartsWith("-", StringComparison.Ordinal)) {
                            error = $"Unknown option: {a}";
                            return options;
                        }
                        break;
                }
            }

            return options;
        }

        private static bool TryGetValue(string[] args, ref int i, out string value, out string? error) {
            error = null;
            value = string.Empty;
            if (i + 1 >= args.Length) {
                error = $"Missing value for {args[i]}.";
                return false;
            }
            i++;
            value = args[i] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) {
                error = $"Empty value for {args[i - 1]}.";
                return false;
            }
            return true;
        }
    }

    private static void WritePolicyBanner(ReplOptions options, IReadOnlyList<IToolPack> packs) {
        var descriptors = ToolPackBootstrap.GetDescriptors(packs);
        var dangerousEnabled = descriptors.Any(static p => p.IsDangerous || p.Tier == ToolCapabilityTier.DangerousWrite);

        Console.WriteLine("Policy:");
        Console.WriteLine($"  Mode: {(dangerousEnabled ? "mixed (dangerous pack enabled)" : "read-only (no writes implied)")}");
        var maxTable = options.MaxTableRows <= 0 ? "(none)" : options.MaxTableRows.ToString();
        var maxSample = options.MaxSample <= 0 ? "(none)" : options.MaxSample.ToString();
        Console.WriteLine($"  Response shaping: max_table_rows={maxTable}, max_sample={maxSample}, redact={(options.Redact ? "on" : "off")}");
        Console.WriteLine($"  Allowed roots: {(options.AllowedRoots.Count == 0 ? "(none)" : string.Join("; ", options.AllowedRoots))}");
        Console.WriteLine("  Packs:");

        foreach (var p in descriptors) {
            Console.WriteLine($"    - {p.Id} ({p.Tier})");
        }

        Console.WriteLine($"  Dangerous tools: {(dangerousEnabled ? "enabled (explicit opt-in)" : "disabled")}");
    }

    private static string? ApplyRuntimeShaping(string? instructions, ReplOptions options) {
        var shaping = BuildShapingInstructions(options);
        if (string.IsNullOrWhiteSpace(shaping)) {
            return instructions;
        }
        if (string.IsNullOrWhiteSpace(instructions)) {
            return shaping;
        }
        return instructions + Environment.NewLine + Environment.NewLine + shaping;
    }

    private static string? BuildShapingInstructions(ReplOptions options) {
        if (options.MaxTableRows <= 0 && options.MaxSample <= 0 && !options.Redact) {
            return null;
        }

        var lines = new List<string> {
            "## Session Response Shaping",
            "Follow these display constraints for all assistant responses:"
        };
        if (options.MaxTableRows > 0) {
            lines.Add($"- Max table rows: {options.MaxTableRows} (show a preview, then offer to paginate/refine).");
        }
        if (options.MaxSample > 0) {
            lines.Add($"- Max sample items: {options.MaxSample} (for long lists, show a sample and counts).");
        }
        if (options.Redact) {
            lines.Add("- Redaction: redact emails/UPNs in assistant output. Prefer summaries over raw identifiers.");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static readonly Regex EmailRegex = new(@"\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string RedactText(string text) {
        if (string.IsNullOrEmpty(text)) {
            return string.Empty;
        }
        return EmailRegex.Replace(text, "[redacted_email]");
    }

    private static CancellationTokenSource? CreateTimeoutCts(CancellationToken ct, int seconds) {
        if (seconds <= 0) {
            return null;
        }
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(seconds));
        return cts;
    }

    // Tool errors are returned as JSON strings to the model. Use the shared contract helper so
    // tool packs and hosts converge on the same envelope over time.
}

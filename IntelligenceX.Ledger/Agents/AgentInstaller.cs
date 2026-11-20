using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>One change agent-install makes. Apply is null for a note that needs no change here.</summary>
internal sealed record AgentInstallStep(string Action, string Detail, Func<Task>? Apply = null);

/// <summary>Configures the agent service on this machine for one installed version: its agent.json, the reporter's
/// configuration without the agent section older builds kept there, and the ask tools (MCP server and inbox hook) from
/// that version registered through the Codex and Claude CLIs' own commands. Install-LedgerAgent.ps1 owns the files and the
/// sign-in task or LaunchAgent and calls this. Preview is the default; a repeated run changes nothing.</summary>
internal static class AgentInstaller {
    private const string McpName = "ix_ask";
    private const string Usage = "Usage: IntelligenceX.Ledger agent-install --reporter <reporter.json> --version <installed-version-folder> " +
        "[--agent-config <agent.json>] [--workspace-root <dir>] [--capability <name>]... [--codex <path>] [--claude <path>] [--apply]";
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal sealed record Options(string ReporterConfig, string Version, string AgentConfig, string? WorkspaceRoot, string[] Capabilities,
        string? Codex, string? Claude, bool Apply);

    public static async Task<int> RunAsync(string[] args) {
        Options options;
        try { options = Parse(args); }
        catch (ArgumentException error) { Console.Error.WriteLine(error.Message + "\n" + Usage); return 2; }
        try {
            var steps = Plan(options, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Find);
            if (options.Apply)
                foreach (var step in steps.Where(s => s.Apply is not null)) await step.Apply!().ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new { applied = options.Apply,
                steps = steps.Select(s => new { s.Action, s.Detail, change = s.Apply is not null }) }, Indented));
            return 0;
        } catch (InvalidOperationException error) {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    internal static Options Parse(string[] args) {
        string? reporter = null, version = null, agentConfig = null, workspace = null, codex = null, claude = null;
        var capabilities = new List<string>();
        var apply = false;
        for (var i = 1; i < args.Length; i++) {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i] + ".");
            switch (args[i]) {
                case "--reporter": reporter = Next(); break;
                case "--version": version = Next(); break;
                case "--agent-config": agentConfig = Next(); break;
                case "--workspace-root": workspace = Next(); break;
                case "--capability": capabilities.Add(Next()); break;
                case "--codex": codex = Next(); break;
                case "--claude": claude = Next(); break;
                case "--apply": apply = true; break;
                default: throw new ArgumentException("Unknown argument " + args[i] + ".");
            }
        }
        if (reporter is null || version is null) throw new ArgumentException("--reporter and --version are required.");
        reporter = Path.GetFullPath(reporter);
        return new(reporter, Path.GetFullPath(version), Path.GetFullPath(agentConfig ?? Path.Combine(Path.GetDirectoryName(reporter)!, "agent.json")),
            workspace, capabilities.ToArray(), codex, claude, apply);
    }

    /// <summary>Works out every change without making any. Find locates the Codex and Claude CLIs.</summary>
    internal static List<AgentInstallStep> Plan(Options options, string home, Func<string, string?> find) {
        var steps = new List<AgentInstallStep>();
        var root = Path.GetDirectoryName(options.ReporterConfig)!;
        var hostClient = Path.Combine(root, "coordination", "host-client.json");
        var sessionsClient = Path.Combine(root, "coordination", "sessions-client.json");
        foreach (var file in new[] { hostClient, sessionsClient })
            if (!File.Exists(file)) throw new InvalidOperationException("Missing " + file + ". Copy this machine's files from agent-enroll first.");
        if (!Directory.Exists(Path.Combine(options.Version, "tools", "mcp")))
            throw new InvalidOperationException("No installed version with tools/mcp at " + options.Version + ".");

        var reporter = JsonNode.Parse(File.ReadAllText(options.ReporterConfig))!.AsObject();
        var existing = File.Exists(options.AgentConfig) ? JsonNode.Parse(File.ReadAllText(options.AgentConfig))!.AsObject() : null;
        // Older builds kept these settings in the reporter's configuration; they move to agent.json once.
        var legacy = reporter["agent"] as JsonObject;
        var agent = AgentSettings(options, existing ?? legacy, reporter, root, hostClient, find);
        if (!JsonNode.DeepEquals(existing, agent))
            steps.Add(new("write-agent-config", options.AgentConfig, () => { LocalFiles.WriteAtomic(options.AgentConfig, agent.ToJsonString(Indented)); return Task.CompletedTask; }));
        var mcp = McpCommand(options.Version, sessionsClient);
        var codex = agent["codexExecutable"]?.GetValue<string>();
        var claude = agent["claudeExecutable"]?.GetValue<string>();
        if (codex is not null && !Registered(codex, mcp))
            steps.Add(new("register-mcp-codex", string.Join(' ', mcp), async () => {
                await RunAsync(codex, ["mcp", "remove", McpName], allowFailure: true).ConfigureAwait(false);
                await RunAsync(codex, ["mcp", "add", McpName, "--", .. mcp]).ConfigureAwait(false);
            }));
        if (claude is not null) {
            if (!Registered(claude, mcp))
                steps.Add(new("register-mcp-claude", string.Join(' ', mcp), async () => {
                    await RunAsync(claude, ["mcp", "remove", McpName, "--scope", "user"], allowFailure: true).ConfigureAwait(false);
                    await RunAsync(claude, ["mcp", "add", McpName, "--scope", "user", "--", .. mcp]).ConfigureAwait(false);
                }));
            var hookFile = Path.Combine(options.Version, "tools", "hook", OperatingSystem.IsWindows() ? "IntelligenceX.Ledger.Hook.exe" : "IntelligenceX.Ledger.Hook");
            if (File.Exists(hookFile)) {
                var hook = "\"" + hookFile + "\" inbox --config \"" + options.AgentConfig + "\"";
                var settings = Path.Combine(home, ".claude", "settings.json");
                if (!HookRegistered(settings, hook))
                    steps.Add(new("register-claude-inbox-hook", hook, () => { RegisterHook(settings, hook); return Task.CompletedTask; }));
            } else steps.Add(new("claude-inbox-hook-missing", "This version has no native hook for this platform; build its kit on this OS."));
        }
        // Last, so a step that fails leaves the section (and with it the pending reporter restart) for the retry.
        if (legacy is not null) {
            steps.Add(new("remove-reporter-agent-section", options.ReporterConfig, () => {
                File.Copy(options.ReporterConfig, options.ReporterConfig + ".before-agent-install", overwrite: true);
                reporter.Remove("agent");
                LocalFiles.WriteAtomic(options.ReporterConfig, reporter.ToJsonString(Indented));
                return Task.CompletedTask;
            }));
            steps.Add(new("restart-reporter", "The reporter must restart so it stops running the agent service itself."));
        }
        return steps;
    }

    /// <summary>agent.json: explicit options win, then what is already configured, then defaults. Settings this command
    /// doesn't manage (for example sweepSeconds) are kept.</summary>
    internal static JsonObject AgentSettings(Options options, JsonObject? existing, JsonObject reporter, string root, string hostClient,
        Func<string, string?> find) {
        string? Existing(string name) => existing?[name]?.GetValue<string>();
        var capabilities = options.Capabilities.Length > 0 ? options.Capabilities
            : existing?["capabilities"]?.AsArray().Select(c => c!.GetValue<string>()).ToArray() ?? [];
        if (capabilities.Length == 0)
            throw new InvalidOperationException("Pass --capability with this machine's policy capability (for example windows-powershell or macos-shell).");
        var workspace = options.WorkspaceRoot ?? Existing("workspaceRoot") ??
            (Environment.GetEnvironmentVariable("EVOTEC_DEV_ROOT") is { Length: > 0 } dev ? Path.Combine(dev, "Worktrees", "ledger-agent")
                : throw new InvalidOperationException("Pass --workspace-root; EVOTEC_DEV_ROOT is not set."));
        var agent = existing?.DeepClone().AsObject() ?? new JsonObject();
        agent["machineId"] = reporter["machineId"]!.GetValue<string>();
        agent["reporterConfig"] = options.ReporterConfig;
        agent["coordinationConfig"] = hostClient;
        agent["stateDirectory"] = Existing("stateDirectory") ?? Path.Combine(root, "agent");
        agent["workspaceRoot"] = workspace;
        agent["capabilities"] = new JsonArray(capabilities.Select(c => (JsonNode)c).ToArray());
        // A stored path that no longer exists (or a bare name) is looked up again; a CLI that isn't installed is left out.
        string? Cli(string? chosen, string? stored, string name) =>
            chosen ?? (stored is not null && Path.IsPathFullyQualified(stored) && File.Exists(stored) ? stored : find(name));
        foreach (var (key, chosen, name) in new[] { ("codexExecutable", options.Codex, "codex"), ("claudeExecutable", options.Claude, "claude") }) {
            if (Cli(chosen, Existing(key), name) is { } path) agent[key] = path;
            else agent.Remove(key);
        }
        return agent;
    }

    /// <summary>Finds a CLI: Codex in the desktop app first (see <see cref="CodexCli"/>), otherwise on PATH or in the
    /// usual per-user, Homebrew and ChatGPT app folders. The stored path is
    /// absolute because a LaunchAgent starts with a minimal PATH.</summary>
    internal static string? Find(string name) {
        if (name == "codex" && CodexCli.DesktopApp() is { } app) return app;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var names = OperatingSystem.IsWindows() ? new[] { name + ".exe" } : [name];
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat([Path.Combine(home, ".local", "bin"), "/opt/homebrew/bin", "/usr/local/bin",
                "/Applications/ChatGPT.app/Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex",
                    "node_modules", "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin")]);
        return folders.SelectMany(folder => names.Select(file => Path.Combine(folder, file))).FirstOrDefault(File.Exists);
    }

    /// <summary>The MCP server launch: the native app host on Windows, the user's dotnet elsewhere (an app host there
    /// needs DOTNET_ROOT, which Codex and Claude don't pass to MCP servers).</summary>
    internal static string[] McpCommand(string version, string sessionsClient) {
        var folder = Path.Combine(version, "tools", "mcp");
        return OperatingSystem.IsWindows()
            ? [Path.Combine(folder, "IntelligenceX.Ledger.Mcp.exe"), "--coordination-config", sessionsClient]
            : [DotnetHost(), Path.Combine(folder, "IntelligenceX.Ledger.Mcp.dll"), "--coordination-config", sessionsClient];
    }

    /// <summary>The dotnet host: this process when it is dotnet, otherwise DOTNET_ROOT's, the user install's, or PATH's.</summary>
    internal static string DotnetHost() {
        if (Environment.ProcessPath is { } self && Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return self;
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var folder in new[] { Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet") })
            if (!string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, name))) return Path.Combine(folder, name);
        return "dotnet";
    }

    /// <summary>How long Claude lets the inbox hook run; the hook's own budgets stay inside it (LedgerInbox).</summary>
    internal const int HookTimeoutSeconds = 15;

    /// <summary>Whether the CLI already reports this exact MCP server command (read with its own `mcp get`).</summary>
    private static bool Registered(string cli, string[] command) {
        try {
            var start = new ProcessStartInfo(cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in new[] { "mcp", "get", McpName }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
            if (process is null) return false;
            _ = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            return process.WaitForExit(30000) && process.ExitCode == 0 && command.All(part => output.Contains(part, StringComparison.OrdinalIgnoreCase));
        } catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Whether SessionStart and UserPromptSubmit each run exactly this inbox hook and no other.</summary>
    internal static bool HookRegistered(string settingsPath, string command) {
        if (!File.Exists(settingsPath)) return false;
        var hooks = JsonNode.Parse(File.ReadAllText(settingsPath))?["hooks"] as JsonObject;
        return new[] { "SessionStart", "UserPromptSubmit" }.All(name => (hooks?[name] as JsonArray)?.OfType<JsonObject>()
            .SelectMany(group => group["hooks"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(entry => entry["command"]?.GetValue<string>()).Where(c => c?.Contains(" inbox --config ", StringComparison.Ordinal) == true)
            .SequenceEqual([command]) == true);
    }

    /// <summary>Replaces any earlier inbox hook in Claude's settings with this one, on SessionStart and UserPromptSubmit,
    /// keeping a copy of the previous settings.</summary>
    internal static void RegisterHook(string settingsPath, string command) {
        var settings = File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject() : new JsonObject();
        if (File.Exists(settingsPath)) File.Copy(settingsPath, settingsPath + ".before-agent-install", overwrite: true);
        if (settings["hooks"] is not JsonObject hooks) settings["hooks"] = hooks = new JsonObject();
        foreach (var name in new[] { "SessionStart", "UserPromptSubmit" }) {
            if (hooks[name] is not JsonArray groups) hooks[name] = groups = new JsonArray();
            foreach (var group in groups.OfType<JsonObject>().ToArray()) {
                if (group["hooks"] is not JsonArray entries) continue;
                foreach (var entry in entries.OfType<JsonObject>()
                    .Where(e => e["command"]?.GetValue<string>().Contains(" inbox --config ", StringComparison.Ordinal) == true).ToArray())
                    entries.Remove(entry);
                if (entries.Count == 0) groups.Remove(group);
            }
            groups.Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = HookTimeoutSeconds }) });
        }
        LocalFiles.WriteAtomic(settingsPath, settings.ToJsonString(Indented) + "\n");
    }

    private static async Task RunAsync(string executable, string[] arguments, bool allowFailure = false) {
        var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException(executable + " did not start.");
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException(Path.GetFileName(executable) + " " + string.Join(' ', arguments.Take(3)) + " failed: " + (await error.ConfigureAwait(false)).Trim());
    }
}

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>One change agent-install makes. Apply is null for a note that needs no change here.</summary>
internal sealed record AgentInstallStep(string Action, string Detail, Func<Task>? Apply = null);

/// <summary>Sets up the agent service on a machine whose reporter is already installed: the reporter's agent section,
/// the ask tools (MCP server and inbox hook) in a folder named for their source commit, their registration through the
/// Codex and Claude CLIs' own commands, and removal of older tool folders. The platform installers own the reporter
/// itself. Preview is the default and changes nothing; every step can be repeated.</summary>
internal static class AgentInstaller {
    private const string McpName = "ix_ask";
    private const string Usage = "Usage: IntelligenceX.Ledger agent-install --config <reporter.json> [--tools <kit>] " +
        "[--workspace-root <dir>] [--capability <name>]... [--codex <path>] [--claude <path>] [--apply]";
    private static readonly JsonSerializerOptions Indented = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal sealed record Options(string ReporterConfig, string? Kit, string? WorkspaceRoot, string[] Capabilities,
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
        string? config = null, kit = null, workspace = null, codex = null, claude = null;
        var capabilities = new List<string>();
        var apply = false;
        for (var i = 1; i < args.Length; i++) {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value for " + args[i] + ".");
            switch (args[i]) {
                case "--config": config = Next(); break;
                case "--tools": kit = Next(); break;
                case "--workspace-root": workspace = Next(); break;
                case "--capability": capabilities.Add(Next()); break;
                case "--codex": codex = Next(); break;
                case "--claude": claude = Next(); break;
                case "--apply": apply = true; break;
                default: throw new ArgumentException("Unknown argument " + args[i] + ".");
            }
        }
        if (config is null) throw new ArgumentException("--config is required.");
        return new(Path.GetFullPath(config), kit is null ? null : Path.GetFullPath(kit), workspace, capabilities.ToArray(), codex, claude, apply);
    }

    /// <summary>Works out every change for this machine without making any. Find locates the Codex and Claude CLIs.</summary>
    internal static List<AgentInstallStep> Plan(Options options, string home, Func<string, string?> find) {
        var steps = new List<AgentInstallStep>();
        var root = Path.GetDirectoryName(options.ReporterConfig)!;
        var reporter = JsonNode.Parse(File.ReadAllText(options.ReporterConfig))!.AsObject();
        var hostClient = Path.Combine(root, "coordination", "host-client.json");
        var sessionsClient = Path.Combine(root, "coordination", "sessions-client.json");
        foreach (var file in new[] { hostClient, sessionsClient }) {
            if (!File.Exists(file)) throw new InvalidOperationException("Missing " + file + ". Copy this machine's files from agent-enroll first.");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(file) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite)) != 0)
                steps.Add(new("protect-client-file", file, () => {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    return Task.CompletedTask;
                }));
        }

        var agent = AgentSection(options, reporter["agent"] as JsonObject, root, hostClient, find);
        if (!JsonNode.DeepEquals(reporter["agent"], agent)) {
            steps.Add(new("update-reporter-agent-section", options.ReporterConfig, () => {
                File.Copy(options.ReporterConfig, options.ReporterConfig + ".before-agent-install", overwrite: true);
                reporter["agent"] = agent;
                LocalFiles.WriteAtomic(options.ReporterConfig, reporter.ToJsonString(Indented));
                return Task.CompletedTask;
            }));
            steps.Add(new("restart-reporter", OperatingSystem.IsMacOS()
                ? "Run Install-MacLedgerReporter.ps1 -Action Restart -Apply so the reporter reads its new agent section."
                : "Stop the reporter process; its startup check starts it again within a minute with the new agent section."));
        }

        if (options.Kit is null) {
            steps.Add(new("tools-unchanged", "No --tools kit was given."));
            return steps;
        }
        var kitTools = Path.Combine(options.Kit, "tools");
        if (!Directory.Exists(Path.Combine(kitTools, "mcp"))) throw new InvalidOperationException("The kit has no tools/mcp folder: " + options.Kit);
        var toolsRoot = Path.Combine(root, "tools");
        var tools = Path.Combine(toolsRoot, ReadSourceCommit(options.Kit)[..12]);
        if (!Directory.Exists(tools)) steps.Add(new("install-tools", tools, () => { CopyDirectory(kitTools, tools); return Task.CompletedTask; }));

        var mcp = McpCommand(tools, sessionsClient);
        var codex = agent["codexExecutable"]?.GetValue<string>();
        var claude = agent["claudeExecutable"]?.GetValue<string>();
        if (codex is not null && !Registered(codex, ["mcp", "get", McpName], mcp))
            steps.Add(new("register-mcp-codex", string.Join(' ', mcp), async () => {
                await RunAsync(codex, ["mcp", "remove", McpName], allowFailure: true).ConfigureAwait(false);
                await RunAsync(codex, ["mcp", "add", McpName, "--", .. mcp]).ConfigureAwait(false);
            }));
        if (claude is not null) {
            if (!Registered(claude, ["mcp", "get", McpName], mcp))
                steps.Add(new("register-mcp-claude", string.Join(' ', mcp), async () => {
                    await RunAsync(claude, ["mcp", "remove", McpName, "--scope", "user"], allowFailure: true).ConfigureAwait(false);
                    await RunAsync(claude, ["mcp", "add", McpName, "--scope", "user", "--", .. mcp]).ConfigureAwait(false);
                }));
            var hookName = OperatingSystem.IsWindows() ? "IntelligenceX.Ledger.Hook.exe" : "IntelligenceX.Ledger.Hook";
            if (File.Exists(Path.Combine(kitTools, "hook", hookName))) {
                var hook = "\"" + Path.Combine(tools, "hook", hookName) + "\" inbox --config \"" + options.ReporterConfig + "\"";
                var settings = Path.Combine(home, ".claude", "settings.json");
                if (!HookRegistered(settings, hook))
                    steps.Add(new("register-claude-inbox-hook", hook, () => { RegisterHook(settings, hook); return Task.CompletedTask; }));
            } else steps.Add(new("claude-inbox-hook-missing", "The kit has no native hook for this platform; build it on this OS."));
        }
        // A folder still in use by a running MCP server stays until the next run.
        foreach (var old in Directory.Exists(toolsRoot) ? Directory.GetDirectories(toolsRoot) : [])
            if (!string.Equals(Path.GetFullPath(old), Path.GetFullPath(tools), StringComparison.OrdinalIgnoreCase))
                steps.Add(new("remove-old-tools", old, () => {
                    try { Directory.Delete(old, recursive: true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                    return Task.CompletedTask;
                }));
        return steps;
    }

    /// <summary>The reporter's agent section: explicit options win, then what is already there, then defaults.</summary>
    internal static JsonObject AgentSection(Options options, JsonObject? existing, string root, string hostClient,
        Func<string, string?> find) {
        string? Existing(string name) => existing?[name]?.GetValue<string>();
        var capabilities = options.Capabilities.Length > 0 ? options.Capabilities
            : existing?["capabilities"]?.AsArray().Select(c => c!.GetValue<string>()).ToArray() ?? [];
        if (capabilities.Length == 0)
            throw new InvalidOperationException("Pass --capability with this machine's policy capability (for example windows-powershell or macos-shell).");
        var workspace = options.WorkspaceRoot ?? Existing("workspaceRoot") ??
            (Environment.GetEnvironmentVariable("EVOTEC_DEV_ROOT") is { Length: > 0 } dev ? Path.Combine(dev, "Worktrees", "ledger-agent")
                : throw new InvalidOperationException("Pass --workspace-root; EVOTEC_DEV_ROOT is not set."));
        var agent = new JsonObject {
            ["coordinationConfig"] = hostClient,
            ["stateDirectory"] = Existing("stateDirectory") ?? Path.Combine(root, "agent"),
            ["workspaceRoot"] = workspace,
            ["capabilities"] = new JsonArray(capabilities.Select(c => (JsonNode)c).ToArray())
        };
        // A stored path that no longer exists (or a bare name) is looked up again; a CLI that isn't installed is left out.
        string? Cli(string? chosen, string? stored, string name) => chosen ?? (stored is not null && Path.IsPathFullyQualified(stored) && File.Exists(stored) ? stored : find(name));
        if (Cli(options.Codex, Existing("codexExecutable"), "codex") is { } codex) agent["codexExecutable"] = codex;
        if (Cli(options.Claude, Existing("claudeExecutable"), "claude") is { } claude) agent["claudeExecutable"] = claude;
        return agent;
    }

    /// <summary>Finds a CLI on PATH or in the usual per-user and Homebrew folders. The stored path is absolute because
    /// a LaunchAgent starts with a minimal PATH.</summary>
    internal static string? Find(string name) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var names = OperatingSystem.IsWindows() ? new[] { name + ".exe", name + ".cmd" } : [name];
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat([Path.Combine(home, ".local", "bin"), "/opt/homebrew/bin", "/usr/local/bin"])
            .SelectMany(folder => names.Select(file => Path.Combine(folder, file))).FirstOrDefault(File.Exists);
    }

    /// <summary>The MCP server launch: the native app host on Windows, the user's dotnet elsewhere (an app host there
    /// needs DOTNET_ROOT, which Codex and Claude don't pass to MCP servers).</summary>
    internal static string[] McpCommand(string tools, string sessionsClient) {
        var folder = Path.Combine(tools, "mcp");
        return OperatingSystem.IsWindows()
            ? [Path.Combine(folder, "IntelligenceX.Ledger.Mcp.exe"), "--coordination-config", sessionsClient]
            : [Environment.ProcessPath ?? "dotnet", Path.Combine(folder, "IntelligenceX.Ledger.Mcp.dll"), "--coordination-config", sessionsClient];
    }

    /// <summary>Whether the CLI already reports this exact MCP server command (read with its own `mcp get`).</summary>
    private static bool Registered(string cli, string[] get, string[] command) {
        try {
            var start = new ProcessStartInfo(cli) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in get) start.ArgumentList.Add(argument);
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
            groups.Add(new JsonObject { ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command, ["timeout"] = 15 }) });
        }
        LocalFiles.WriteAtomic(settingsPath, settings.ToJsonString(Indented) + "\n");
    }

    private static string ReadSourceCommit(string kit) {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(kit, "manifest.json")));
        return manifest.RootElement.TryGetProperty("sourceCommit", out var commit) && commit.GetString() is { Length: 40 } value
            ? value : throw new InvalidOperationException("The kit manifest has no source commit.");
    }

    private static void CopyDirectory(string from, string to) {
        var staging = to + ".partial";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) {
            var target = Path.Combine(staging, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, File.GetUnixFileMode(file));
        }
        Directory.Move(staging, to);
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

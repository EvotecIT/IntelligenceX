using System.Text.Json.Nodes;
using IntelligenceX.Ledger.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class AgentSetupTests : IDisposable {
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ix-agent-setup-" + Guid.NewGuid().ToString("N"));

    // Tests never find the real Codex or Claude CLIs, so they can't change this machine's registrations.
    private static string? NoClis(string name) => null;

    private string Reporter(JsonObject? agent = null) {
        Directory.CreateDirectory(Path.Combine(_root, "coordination"));
        File.WriteAllText(Path.Combine(_root, "coordination", "host-client.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "coordination", "sessions-client.json"), "{}");
        var reporter = new JsonObject { ["machineId"] = "mac", ["endpoint"] = "https://ledger/" };
        if (agent is not null) reporter["agent"] = agent;
        var path = Path.Combine(_root, "reporter.json");
        File.WriteAllText(path, reporter.ToJsonString());
        return path;
    }

    /// <summary>An installed version folder as Install-LedgerAgent.ps1 leaves it.</summary>
    private string Version(bool hook) {
        var version = Path.Combine(_root, "agent-service", "versions", new string('a', 40));
        Directory.CreateDirectory(Path.Combine(version, "tools", "mcp"));
        File.WriteAllText(Path.Combine(version, "tools", "mcp", "IntelligenceX.Ledger.Mcp.dll"), "mcp");
        if (hook) {
            Directory.CreateDirectory(Path.Combine(version, "tools", "hook"));
            File.WriteAllText(Path.Combine(version, "tools", "hook", OperatingSystem.IsWindows() ? "IntelligenceX.Ledger.Hook.exe" : "IntelligenceX.Ledger.Hook"), "hook");
        }
        return version;
    }

    private AgentInstaller.Options Options(string reporter, string version, string[] capabilities) =>
        new(reporter, version, Path.Combine(_root, "agent.json"), Path.Combine(_root, "work"), capabilities, null, null, Apply: true);

    [Fact]
    public async Task InstallMovesTheAgentSettingsOutOfTheReporterOnceAndAgainIsANoOp() {
        var config = Reporter(new JsonObject { ["stateDirectory"] = "/state", ["capabilities"] = new JsonArray("macos-shell"), ["sweepSeconds"] = 5 });
        var options = Options(config, Version(hook: false), []);
        var steps = AgentInstaller.Plan(options, _root, NoClis);
        Assert.DoesNotContain(steps, s => s.Action.StartsWith("register-", StringComparison.Ordinal));
        Assert.Equal(["write-agent-config", "remove-reporter-agent-section", "restart-reporter"], steps.Select(s => s.Action).ToArray());
        foreach (var step in steps.Where(s => s.Apply is not null)) await step.Apply!();

        Assert.Null(JsonNode.Parse(File.ReadAllText(config))!["agent"]);
        Assert.True(File.Exists(config + ".before-agent-install"));
        var agent = AgentHost.Load(Path.Combine(_root, "agent.json"));
        Assert.Equal(("mac", config, "/state", 5d), (agent.MachineId, agent.ReporterConfig, agent.StateDirectory, agent.SweepSeconds));
        Assert.Equal(Path.Combine(_root, "coordination", "host-client.json"), agent.CoordinationConfig);
        Assert.Empty(AgentInstaller.Plan(options, _root, NoClis));
    }

    [Fact]
    public void RewrittenFilesKeepTheirOwnerOnlyPermissions() {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "private.json");
        LocalFiles.WriteAtomic(path, "{}");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        LocalFiles.WriteAtomic(path, "{\"a\":1}");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
    }

    [Fact]
    public void AStoredCliThatIsNotInstalledIsLeftOut() {
        var config = Reporter();
        var reporter = JsonNode.Parse(File.ReadAllText(config))!.AsObject();
        var existing = new JsonObject { ["capabilities"] = new JsonArray("macos-shell"), ["claudeExecutable"] = "claude" };
        var agent = AgentInstaller.AgentSettings(Options(config, Version(hook: false), []), existing, reporter, _root, "host.json", NoClis);
        Assert.Null(agent["claudeExecutable"]);
    }

    // The repository roots decide what the local store's bootstrap policy allows. They live in the reporter's settings,
    // which older agent versions also read, so a reinstall keeps them and only an explicit --repository-root replaces them.
    [Fact]
    public void RepositoryRootsStayInTheReporterUnlessReplaced() {
        var config = Reporter();
        var reporter = JsonNode.Parse(File.ReadAllText(config))!.AsObject();
        reporter["repositoryRoots"] = new JsonArray("/stored");
        var options = Options(config, Version(hook: false), ["macos-shell"]);
        Assert.Null(AgentInstaller.RepositoryRoots(options, reporter));
        Assert.Equal(["/chosen"], AgentInstaller.RepositoryRoots(options with { RepositoryRoots = ["/chosen"] }, reporter)!);
        Assert.Null(AgentInstaller.AgentSettings(options with { RepositoryRoots = ["/chosen"] }, null, reporter, _root, "host.json",
            NoClis)["repositoryRoots"]);
    }

    [Fact]
    public void InstallNeedsTheMachineClientFilesAnInstalledVersionAndACapability() {
        var config = Reporter();
        var options = Options(config, Path.Combine(_root, "missing"), ["macos-shell"]);
        Assert.Contains("tools/mcp", Assert.Throws<InvalidOperationException>(() => AgentInstaller.Plan(options, _root, NoClis)).Message);
        options = options with { Version = Version(hook: false) };
        File.Delete(Path.Combine(_root, "coordination", "sessions-client.json"));
        Assert.Contains("agent-enroll", Assert.Throws<InvalidOperationException>(() => AgentInstaller.Plan(options, _root, NoClis)).Message);
        File.WriteAllText(Path.Combine(_root, "coordination", "sessions-client.json"), "{}");
        Assert.Contains("--capability", Assert.Throws<InvalidOperationException>(() =>
            AgentInstaller.Plan(options with { Capabilities = [] }, _root, NoClis)).Message);
    }

    [Fact]
    public void TheInboxHookReplacesEarlierOnesAndKeepsOtherHooks() {
        Directory.CreateDirectory(Path.Combine(_root, ".claude"));
        var settings = Path.Combine(_root, ".claude", "settings.json");
        File.WriteAllText(settings, """
            {"model":"opus","hooks":{"SessionStart":[
              {"hooks":[{"type":"command","command":"dotnet ledger.dll hook --config r.json","async":true}]},
              {"hooks":[{"type":"command","command":"dotnet old/ledger.dll inbox --config r.json","timeout":30}]}]}}
            """);
        AgentInstaller.RegisterHook(settings, "\"new/hook\" inbox --config \"r.json\"");
        AgentInstaller.RegisterHook(settings, "\"new/hook\" inbox --config \"r.json\"");
        var hooks = JsonNode.Parse(File.ReadAllText(settings))!["hooks"]!;
        var start = hooks["SessionStart"]!.AsArray().SelectMany(g => g!["hooks"]!.AsArray()).Select(h => h!["command"]!.GetValue<string>()).ToArray();
        Assert.Equal(["dotnet ledger.dll hook --config r.json", "\"new/hook\" inbox --config \"r.json\""], start);
        Assert.Single(hooks["UserPromptSubmit"]!.AsArray());
        Assert.True(AgentInstaller.HookRegistered(settings, "\"new/hook\" inbox --config \"r.json\""));
        Assert.False(AgentInstaller.HookRegistered(settings, "\"other/hook\" inbox --config \"r.json\""));
        Assert.Equal("opus", JsonNode.Parse(File.ReadAllText(settings))!["model"]!.GetValue<string>());
    }

    [Fact]
    public void EnrollAddsScopedPrincipalsOnceAndWritesTheMachineFiles() {
        Directory.CreateDirectory(_root);
        var configuration = Path.Combine(_root, "configuration.json");
        File.WriteAllText(configuration, """
            {"principals":[{"id":"pc-host","role":"host","token":"t1","projects":["IntelligenceX"],"machineId":"win-pc"}],
             "initialPolicy":{"machines":[{"id":"win-pc"},{"id":"mac-studio"}]}}
            """);
        var service = Path.Combine(_root, "service.json");
        File.WriteAllText(service, new JsonObject { ["coordinationConfigPath"] = configuration, ["port"] = 7348 }.ToJsonString());
        var output = Path.Combine(_root, "out");

        var preview = AgentEnrollment.Enroll(service, "mac-studio", output, local: true, apply: false);
        Assert.True(preview.ConfigurationChanged);
        Assert.False(Directory.Exists(output));
        var applied = AgentEnrollment.Enroll(service, "mac-studio", output, local: true, apply: true);
        Assert.Equal(("mac-studio-host", "mac-studio-sessions"), (applied.HostPrincipal, applied.SessionsPrincipal));
        var principals = JsonNode.Parse(File.ReadAllText(configuration))!["principals"]!.AsArray();
        Assert.Equal(3, principals.Count);
        Assert.Equal("http://127.0.0.1:7348/", JsonNode.Parse(File.ReadAllText(Path.Combine(output, "sessions-client.json")))!["endpoint"]!.GetValue<string>());

        Assert.False(AgentEnrollment.Enroll(service, "mac-studio", output, local: true, apply: true).ConfigurationChanged);
        Assert.Equal("pc-host", AgentEnrollment.Enroll(service, "win-pc", output, local: true, apply: false).HostPrincipal);
        Assert.Throws<InvalidOperationException>(() => AgentEnrollment.Enroll(service, "not a machine", output, local: true, apply: false));
    }

    [Fact]
    public async Task StopEndsOnlyTheAgentItsReceiptNamesAndLeavesOtherLedgerProcessesRunning() {
        // Runs the real installer script, so it needs PowerShell 7.5 (the script's minimum) and the source tree.
        var script = FindScript();
        var pwsh = await FindPowerShellAsync();
        if (script is null || pwsh is null) return;
        var started = new List<System.Diagnostics.Process>();
        try {
            var agent = Sleeper(pwsh, started);
            // Stands in for an MCP server whose path sits under the same install, as session MCP servers do.
            var mcp = Sleeper(pwsh, started);
            var state = Path.Combine(_root, "state");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(_root, "agent.json"), new JsonObject { ["stateDirectory"] = state }.ToJsonString());
            File.WriteAllText(Path.Combine(state, "agent-process.json"), new JsonObject {
                ["pid"] = agent.Id, ["startedAtUtc"] = agent.StartTime.ToUniversalTime().ToString("o")
            }.ToJsonString());
            var (exit, output, errors) = await RunAsync(pwsh, started, "-NoProfile", "-NonInteractive", "-File", script, "-Action", "Stop", "-Root", _root);
            Assert.True(exit == 0, errors);
            Assert.Contains(agent.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), output);
            Assert.True(agent.HasExited);
            Assert.False(mcp.HasExited);
        } finally {
            foreach (var process in started) {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.Dispose();
            }
        }
    }

    [Fact]
    public async Task OldVersionsGoExceptOnesARunningProcessWasStartedFrom() {
        var script = FindScript();
        var pwsh = await FindPowerShellAsync();
        if (script is null || pwsh is null) return;
        var versions = Path.Combine(_root, "agent-service", "versions");
        string Folder(string name) { var path = Path.Combine(versions, name, "tools", "mcp"); Directory.CreateDirectory(path); return path; }
        foreach (var name in new[] { "current", "previous", "unused" }) File.WriteAllText(Path.Combine(Folder(name), "a.dll"), "x");
        // Stands in for the MCP server of a session that is still open. Like dotnet hosting the MCP dll on macOS,
        // the process runs from a host outside the folder and names a file inside it.
        var hosted = Path.Combine(Folder("in-use"), "server.ps1");
        File.WriteAllText(hosted, "Start-Sleep 120");
        var started = new List<System.Diagnostics.Process>();
        try {
            started.Add(System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pwsh) {
                ArgumentList = { "-NoProfile", "-NonInteractive", "-File", hosted } })!);
            // Loads only the function from the script, so nothing else in it runs.
            var command = $$"""
                $ast = [System.Management.Automation.Language.Parser]::ParseFile('{{script.Replace("'", "''")}}', [ref]$null, [ref]$null)
                $ast.FindAll({ $args[0] -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $args[0].Name -eq 'Remove-OldVersions' }, $true) | ForEach-Object { Invoke-Expression $_.Extent.Text }
                $Versions = '{{versions.Replace("'", "''")}}'
                Remove-OldVersions @('current', 'previous')
                """;
            var (exit, output, errors) = await RunAsync(pwsh, started, "-NoProfile", "-NonInteractive", "-Command", command);
            Assert.True(exit == 0, errors);
            Assert.Equal("", errors.Trim());
            Assert.Contains("Kept in-use: 1 running process(es)", output);
            Assert.Equal(["current", "in-use", "previous"], Directory.GetDirectories(versions).Select(path => Path.GetFileName(path)!).Order().ToArray());
            Assert.True(File.Exists(hosted));
        } finally {
            foreach (var process in started) {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                process.WaitForExit();
                process.Dispose();
            }
        }
    }

    private static System.Diagnostics.Process Sleeper(string pwsh, List<System.Diagnostics.Process> started) {
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pwsh) {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep 120" } })!;
        started.Add(process);
        return process;
    }

    /// <summary>Runs a process with a one-minute limit; on timeout it is killed and the test fails.</summary>
    private static async Task<(int Exit, string Output, string Errors)> RunAsync(string file, List<System.Diagnostics.Process> started,
        params string[] arguments) {
        var info = new System.Diagnostics.ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        var process = System.Diagnostics.Process.Start(info)!;
        started.Add(process);
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var output = process.StandardOutput.ReadToEndAsync(limit.Token);
        var errors = process.StandardError.ReadToEndAsync(limit.Token);
        await process.WaitForExitAsync(limit.Token);
        return (process.ExitCode, await output, await errors);
    }

    /// <summary>The first pwsh on PATH that is at least 7.5, or null when there is none.</summary>
    private static async Task<string?> FindPowerShellAsync() {
        var name = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var candidate = Path.Combine(folder, name);
            if (!File.Exists(candidate)) continue;
            var started = new List<System.Diagnostics.Process>();
            try {
                var (exit, output, _) = await RunAsync(candidate, started, "-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.ToString()");
                if (exit == 0 && System.Version.TryParse(output.Trim().Split('-')[0], out var version) && version >= new System.Version(7, 5)) return candidate;
            } catch (Exception error) when (error is System.ComponentModel.Win32Exception or OperationCanceledException) {
            } finally { foreach (var process in started) process.Dispose(); }
        }
        return null;
    }

    private static string? FindScript() {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent) {
            var candidate = Path.Combine(folder.FullName, "IntelligenceX.Ledger", "Install", "Install-LedgerAgent.ps1");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

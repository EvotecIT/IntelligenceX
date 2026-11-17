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

    public void Dispose() {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}

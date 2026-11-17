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

    private string Kit(bool hook) {
        var kit = Path.Combine(_root, "kit");
        Directory.CreateDirectory(Path.Combine(kit, "tools", "mcp"));
        File.WriteAllText(Path.Combine(kit, "tools", "mcp", "IntelligenceX.Ledger.Mcp.dll"), "mcp");
        if (hook) {
            Directory.CreateDirectory(Path.Combine(kit, "tools", "hook"));
            File.WriteAllText(Path.Combine(kit, "tools", "hook", OperatingSystem.IsWindows() ? "IntelligenceX.Ledger.Hook.exe" : "IntelligenceX.Ledger.Hook"), "hook");
        }
        File.WriteAllText(Path.Combine(kit, "manifest.json"), "{\"sourceCommit\":\"" + new string('a', 40) + "\"}");
        return kit;
    }

    [Fact]
    public async Task InstallSetsTheAgentSectionAndToolsOnceAndAgainIsANoOp() {
        var config = Reporter();
        var options = new AgentInstaller.Options(config, Kit(hook: false), Path.Combine(_root, "work"), ["macos-shell"],
            Codex: null, Claude: null, Apply: true);
        var steps = AgentInstaller.Plan(options, _root, NoClis);
        Assert.DoesNotContain(steps, s => s.Action.StartsWith("register-", StringComparison.Ordinal));
        Assert.Contains(steps, s => s.Action == "update-reporter-agent-section");
        Assert.Contains(steps, s => s.Action == "restart-reporter");
        Assert.Contains(steps, s => s.Action == "install-tools");
        foreach (var step in steps.Where(s => s.Apply is not null)) await step.Apply!();

        var agent = JsonNode.Parse(File.ReadAllText(config))!["agent"]!;
        Assert.Equal(Path.Combine(_root, "coordination", "host-client.json"), agent["coordinationConfig"]!.GetValue<string>());
        Assert.Equal("macos-shell", agent["capabilities"]![0]!.GetValue<string>());
        Assert.True(File.Exists(Path.Combine(_root, "tools", "aaaaaaaaaaaa", "mcp", "IntelligenceX.Ledger.Mcp.dll")));
        Assert.True(File.Exists(config + ".before-agent-install"));

        var again = AgentInstaller.Plan(options with { Capabilities = [] }, _root, NoClis);
        Assert.DoesNotContain(again, s => s.Apply is not null && s.Action is "update-reporter-agent-section" or "install-tools");
    }

    [Fact]
    public void InstallNeedsTheMachineClientFilesAndACapability() {
        var config = Reporter();
        File.Delete(Path.Combine(_root, "coordination", "sessions-client.json"));
        var options = new AgentInstaller.Options(config, null, Path.Combine(_root, "work"), ["macos-shell"], null, null, false);
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

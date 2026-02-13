using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Setup.Onboarding;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ReviewerSetupContractTests {
    [Fact]
    public async Task ListToolsSourceIncludesReviewerSetupPackInfoAndContractVerificationIsStable() {
        var packs = ToolPackBootstrap.CreateDefaultReadOnlyPacks(new ToolPackBootstrapOptions());
        var registry = new ToolRegistry();
        ToolPackBootstrap.RegisterAll(registry, packs);

        var definitions = registry.GetDefinitions();
        var toolDefinition = Assert.Single(definitions.Where(static d =>
            string.Equals(d.Name, "reviewer_setup_pack_info", StringComparison.Ordinal)));
        Assert.Equal("reviewer_setup_pack_info", toolDefinition.Name);
        var verifyDefinition = Assert.Single(definitions.Where(static d =>
            string.Equals(d.Name, "reviewer_setup_contract_verify", StringComparison.Ordinal)));
        Assert.Equal("reviewer_setup_contract_verify", verifyDefinition.Name);

        Assert.True(registry.TryGet("reviewer_setup_pack_info", out var tool));
        var response = await tool.InvokeAsync(arguments: null, CancellationToken.None).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("ok", out var okNode) && okNode.ValueKind == JsonValueKind.True);
        Assert.Contains("reviewer_setup_pack_info", ReadStringArray(root.GetProperty("tools")));
        Assert.Contains("reviewer_setup_contract_verify", ReadStringArray(root.GetProperty("tools")));

        var verifyCatalogEntry = root.GetProperty("tool_catalog")
            .EnumerateArray()
            .Single(static node =>
                string.Equals(node.GetProperty("name").GetString(), "reviewer_setup_contract_verify", StringComparison.Ordinal));
        var requiredArguments = ReadStringArray(verifyCatalogEntry.GetProperty("required_arguments"));
        Assert.Contains("autodetect_contract_version", requiredArguments);
        Assert.Contains("autodetect_contract_fingerprint", requiredArguments);

        var setupHints = root.GetProperty("setup_hints");
        Assert.Equal(SetupOnboardingContract.ContractVersion, setupHints.GetProperty("contract_version").GetString());
        Assert.Equal(
            SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            setupHints.GetProperty("contract_fingerprint").GetString());

        var pathMap = BuildPathMap(setupHints.GetProperty("paths"));
        Assert.Equal(4, pathMap.Count);

        Assert.True(pathMap.ContainsKey("new-setup"));
        Assert.True(pathMap.ContainsKey("refresh-auth"));
        Assert.True(pathMap.ContainsKey("cleanup"));
        Assert.True(pathMap.ContainsKey("maintenance"));

        Assert.Equal("setup", pathMap["new-setup"].Operation);
        Assert.True(pathMap["new-setup"].RequiresGitHubAuth);
        Assert.True(pathMap["new-setup"].RequiresRepoSelection);
        Assert.True(pathMap["new-setup"].RequiresAiAuth);

        Assert.Equal("update-secret", pathMap["refresh-auth"].Operation);
        Assert.True(pathMap["refresh-auth"].RequiresGitHubAuth);
        Assert.True(pathMap["refresh-auth"].RequiresRepoSelection);
        Assert.True(pathMap["refresh-auth"].RequiresAiAuth);

        Assert.Equal("cleanup", pathMap["cleanup"].Operation);
        Assert.True(pathMap["cleanup"].RequiresGitHubAuth);
        Assert.True(pathMap["cleanup"].RequiresRepoSelection);
        Assert.False(pathMap["cleanup"].RequiresAiAuth);

        Assert.Equal("setup", pathMap["maintenance"].Operation);
        Assert.True(pathMap["maintenance"].RequiresGitHubAuth);
        Assert.True(pathMap["maintenance"].RequiresRepoSelection);
        Assert.False(pathMap["maintenance"].RequiresAiAuth);

        var commandTemplates = setupHints.GetProperty("command_templates");
        Assert.Equal("intelligencex setup autodetect --json", commandTemplates.GetProperty("auto_detect").GetString());
        Assert.Equal("intelligencex setup --repo owner/name --with-config --dry-run", commandTemplates.GetProperty("new_setup_dry_run").GetString());
        Assert.Equal("intelligencex setup --repo owner/name --with-config", commandTemplates.GetProperty("new_setup_apply").GetString());
        Assert.Equal("intelligencex setup --repo owner/name --update-secret --auth-b64 <base64> --dry-run",
            commandTemplates.GetProperty("refresh_auth_dry_run").GetString());
        Assert.Equal("intelligencex setup --repo owner/name --update-secret --auth-b64 <base64>",
            commandTemplates.GetProperty("refresh_auth_apply").GetString());
        Assert.Equal("intelligencex setup --repo owner/name --cleanup --dry-run", commandTemplates.GetProperty("cleanup_dry_run").GetString());
        Assert.Equal("intelligencex setup --repo owner/name --cleanup", commandTemplates.GetProperty("cleanup_apply").GetString());
        Assert.Equal("intelligencex setup web", commandTemplates.GetProperty("maintenance_wizard").GetString());

        Assert.True(registry.TryGet("reviewer_setup_contract_verify", out var verifyTool));
        var verifyResponse = await verifyTool.InvokeAsync(
            arguments: new JsonObject()
                .Add("autodetect_contract_version", SetupOnboardingContract.ContractVersion)
                .Add("autodetect_contract_fingerprint", SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true))
                .Add("pack_contract_version", setupHints.GetProperty("contract_version").GetString())
                .Add("pack_contract_fingerprint", setupHints.GetProperty("contract_fingerprint").GetString()),
            CancellationToken.None).ConfigureAwait(false);

        using var verifyDoc = JsonDocument.Parse(verifyResponse);
        Assert.True(verifyDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("match", verifyDoc.RootElement.GetProperty("status").GetString());
        Assert.False(verifyDoc.RootElement.GetProperty("requires_sync").GetBoolean());
    }

    private static Dictionary<string, SetupPathContract> BuildPathMap(JsonElement pathsNode) {
        var result = new Dictionary<string, SetupPathContract>(StringComparer.Ordinal);
        foreach (var item in pathsNode.EnumerateArray()) {
            var id = item.GetProperty("id").GetString();
            Assert.False(string.IsNullOrWhiteSpace(id));

            result[id!] = new SetupPathContract {
                Operation = item.GetProperty("operation").GetString() ?? string.Empty,
                RequiresGitHubAuth = item.GetProperty("requires_git_hub_auth").GetBoolean(),
                RequiresRepoSelection = item.GetProperty("requires_repo_selection").GetBoolean(),
                RequiresAiAuth = item.GetProperty("requires_ai_auth").GetBoolean()
            };
        }
        return result;
    }

    private sealed record SetupPathContract {
        public required string Operation { get; init; }
        public required bool RequiresGitHubAuth { get; init; }
        public required bool RequiresRepoSelection { get; init; }
        public required bool RequiresAiAuth { get; init; }
    }

    private static string[] ReadStringArray(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static node => node.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .ToArray();
    }
}

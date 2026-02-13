using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Setup.Onboarding;
using IntelligenceX.Tools.ReviewerSetup;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ReviewerSetupContractTests {
    [Fact]
    public async Task ReviewerSetupPackInfo_ShouldMatchCanonicalOnboardingContract() {
        var tool = new ReviewerSetupPackInfoTool(new ReviewerSetupToolOptions {
            IncludeMaintenancePath = true
        });

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Contains("reviewer_setup_pack_info", ReadStringArray(root.GetProperty("tools")));
        Assert.Contains("reviewer_setup_contract_verify", ReadStringArray(root.GetProperty("tools")));

        var verifyCatalogEntry = root.GetProperty("tool_catalog")
            .EnumerateArray()
            .Single(static node =>
                string.Equals(node.GetProperty("name").GetString(), "reviewer_setup_contract_verify", StringComparison.Ordinal));
        var requiredArguments = ReadStringArray(verifyCatalogEntry.GetProperty("required_arguments"));
        Assert.Contains("autodetect_contract_version", requiredArguments);
        Assert.Contains("autodetect_contract_fingerprint", requiredArguments);
        Assert.Contains("include_maintenance_path", ReadStringArray(verifyCatalogEntry.GetProperty("arguments")));

        var setupHints = root.GetProperty("setup_hints");
        Assert.Equal(
            SetupOnboardingContract.ContractVersion,
            setupHints.GetProperty("contract_version").GetString());
        Assert.Equal(
            SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true),
            setupHints.GetProperty("contract_fingerprint").GetString());

        var pathNodes = setupHints.GetProperty("paths");
        var contractPaths = SetupOnboardingContract.GetPaths(includeMaintenancePath: true);
        Assert.Equal(contractPaths.Count, pathNodes.GetArrayLength());

        for (var i = 0; i < contractPaths.Count; i++) {
            var expected = contractPaths[i];
            var actual = pathNodes[i];

            Assert.Equal(expected.Id, actual.GetProperty("id").GetString());
            Assert.Equal(expected.Operation, actual.GetProperty("operation").GetString());
            Assert.Equal(expected.RequiresGitHubAuth, actual.GetProperty("requires_git_hub_auth").GetBoolean());
            Assert.Equal(expected.RequiresRepoSelection, actual.GetProperty("requires_repo_selection").GetBoolean());
            Assert.Equal(expected.RequiresAiAuth, actual.GetProperty("requires_ai_auth").GetBoolean());
        }

        var expectedTemplates = SetupOnboardingContract.GetCommandTemplates();
        var templateNode = setupHints.GetProperty("command_templates");
        Assert.Equal(expectedTemplates.AutoDetect, templateNode.GetProperty("auto_detect").GetString());
        Assert.Equal(expectedTemplates.NewSetupDryRun, templateNode.GetProperty("new_setup_dry_run").GetString());
        Assert.Equal(expectedTemplates.NewSetupApply, templateNode.GetProperty("new_setup_apply").GetString());
        Assert.Equal(expectedTemplates.RefreshAuthDryRun, templateNode.GetProperty("refresh_auth_dry_run").GetString());
        Assert.Equal(expectedTemplates.RefreshAuthApply, templateNode.GetProperty("refresh_auth_apply").GetString());
        Assert.Equal(expectedTemplates.CleanupDryRun, templateNode.GetProperty("cleanup_dry_run").GetString());
        Assert.Equal(expectedTemplates.CleanupApply, templateNode.GetProperty("cleanup_apply").GetString());
        Assert.Equal(expectedTemplates.MaintenanceWizard, templateNode.GetProperty("maintenance_wizard").GetString());
    }

    [Fact]
    public async Task ReviewerSetupPackInfo_WithoutMaintenance_ShouldMatchCanonicalSubset() {
        var tool = new ReviewerSetupPackInfoTool(new ReviewerSetupToolOptions {
            IncludeMaintenancePath = false
        });

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        using var document = JsonDocument.Parse(json);
        var setupHints = document.RootElement.GetProperty("setup_hints");
        Assert.Equal(
            SetupOnboardingContract.ContractVersion,
            setupHints.GetProperty("contract_version").GetString());
        Assert.Equal(
            SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: false),
            setupHints.GetProperty("contract_fingerprint").GetString());

        var pathNodes = setupHints.GetProperty("paths");

        var contractPaths = SetupOnboardingContract.GetPaths(includeMaintenancePath: false);
        Assert.Equal(contractPaths.Count, pathNodes.GetArrayLength());
        for (var i = 0; i < contractPaths.Count; i++) {
            Assert.Equal(contractPaths[i].Id, pathNodes[i].GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task ReviewerSetupContractVerify_ShouldReportMatchWhenMetadataMatches() {
        var tool = new ReviewerSetupContractVerifyTool(new ReviewerSetupToolOptions {
            IncludeMaintenancePath = true
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("autodetect_contract_version", SetupOnboardingContract.ContractVersion)
                .Add("autodetect_contract_fingerprint", SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true))
                .Add("pack_contract_version", SetupOnboardingContract.ContractVersion)
                .Add("pack_contract_fingerprint", SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true)),
            cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("match", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("requires_sync").GetBoolean());
        Assert.Equal(0, root.GetProperty("mismatch_count").GetInt32());
    }

    [Fact]
    public async Task ReviewerSetupContractVerify_ShouldReportMismatchWhenMetadataDiffers() {
        var tool = new ReviewerSetupContractVerifyTool(new ReviewerSetupToolOptions {
            IncludeMaintenancePath = true
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("autodetect_contract_version", "1900-01-01.0")
                .Add("autodetect_contract_fingerprint", SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true))
                .Add("pack_contract_version", SetupOnboardingContract.ContractVersion)
                .Add("pack_contract_fingerprint", "not-the-right-fingerprint"),
            cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("mismatch", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("requires_sync").GetBoolean());
        Assert.True(root.GetProperty("mismatch_count").GetInt32() >= 2);

        var mismatchFields = root.GetProperty("mismatches")
            .EnumerateArray()
            .Select(static node => node.GetProperty("field").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Contains("contract_version", mismatchFields);
        Assert.Contains("contract_fingerprint", mismatchFields);
    }

    [Fact]
    public async Task ReviewerSetupContractVerify_ShouldRejectMissingAutodetectVersion() {
        var tool = new ReviewerSetupContractVerifyTool(new ReviewerSetupToolOptions {
            IncludeMaintenancePath = true
        });

        var json = await tool.InvokeAsync(
            arguments: new JsonObject()
                .Add("autodetect_contract_fingerprint", SetupOnboardingContract.GetContractFingerprint(includeMaintenancePath: true)),
            cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
    }

    private static string[] ReadStringArray(JsonElement element) {
        if (element.ValueKind != global::System.Text.Json.JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var node in element.EnumerateArray()) {
            string? value = node.ValueKind switch {
                global::System.Text.Json.JsonValueKind.String => node.GetString(),
                global::System.Text.Json.JsonValueKind.Object when node.TryGetProperty("name", out var nameNode)
                                                                    && nameNode.ValueKind == global::System.Text.Json.JsonValueKind.String =>
                    nameNode.GetString(),
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(value)) {
                values.Add(value.Trim());
            }
        }

        return values.ToArray();
    }
}

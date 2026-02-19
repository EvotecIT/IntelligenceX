using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Email;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.OfficeIMO;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolPackInfoContractTests {
    [Fact]
    public async Task PackInfoTools_ShouldExposeRegisteredToolCatalogs() {
        var adOptions = new ActiveDirectoryToolOptions();
        var eventLogOptions = new EventLogToolOptions();
        var fileSystemOptions = new FileSystemToolOptions();
        var systemOptions = new SystemToolOptions();
        var emailOptions = new EmailToolOptions();
        var powerShellOptions = new PowerShellToolOptions { Enabled = true };
        var testimoXOptions = new TestimoXToolOptions { Enabled = true };
        var officeImoOptions = new OfficeImoToolOptions();

        var cases = new[] {
            new PackCase(
                Pack: "active_directory",
                Engine: "ADPlayground",
                Tool: new AdPackInfoTool(adOptions),
                ExpectedTools: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolNames(adOptions),
                ExpectedCatalog: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolCatalog(adOptions)),
            new PackCase(
                Pack: "eventlog",
                Engine: "EventViewerX",
                Tool: new EventLogPackInfoTool(eventLogOptions),
                ExpectedTools: ToolRegistryEventLogExtensions.GetRegisteredToolNames(eventLogOptions),
                ExpectedCatalog: ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(eventLogOptions)),
            new PackCase(
                Pack: "filesystem",
                Engine: "IntelligenceX.Engines.FileSystem",
                Tool: new FileSystemPackInfoTool(fileSystemOptions),
                ExpectedTools: ToolRegistryFileSystemExtensions.GetRegisteredToolNames(fileSystemOptions),
                ExpectedCatalog: ToolRegistryFileSystemExtensions.GetRegisteredToolCatalog(fileSystemOptions)),
            new PackCase(
                Pack: "system",
                Engine: "ComputerX",
                Tool: new SystemPackInfoTool(systemOptions),
                ExpectedTools: ToolRegistrySystemExtensions.GetRegisteredToolNames(systemOptions),
                ExpectedCatalog: ToolRegistrySystemExtensions.GetRegisteredToolCatalog(systemOptions)),
            new PackCase(
                Pack: "email",
                Engine: "Mailozaurr",
                Tool: new EmailPackInfoTool(emailOptions),
                ExpectedTools: ToolRegistryEmailExtensions.GetRegisteredToolNames(emailOptions),
                ExpectedCatalog: ToolRegistryEmailExtensions.GetRegisteredToolCatalog(emailOptions)),
            new PackCase(
                Pack: "powershell",
                Engine: "IntelligenceX.Engines.PowerShell",
                Tool: new PowerShellPackInfoTool(powerShellOptions),
                ExpectedTools: ToolRegistryPowerShellExtensions.GetRegisteredToolNames(powerShellOptions),
                ExpectedCatalog: ToolRegistryPowerShellExtensions.GetRegisteredToolCatalog(powerShellOptions)),
            new PackCase(
                Pack: "testimox",
                Engine: "TestimoX",
                Tool: new TestimoXPackInfoTool(testimoXOptions),
                ExpectedTools: ToolRegistryTestimoXExtensions.GetRegisteredToolNames(testimoXOptions),
                ExpectedCatalog: ToolRegistryTestimoXExtensions.GetRegisteredToolCatalog(testimoXOptions)),
            new PackCase(
                Pack: "officeimo",
                Engine: "OfficeIMO.Reader",
                Tool: new OfficeImoPackInfoTool(officeImoOptions),
                ExpectedTools: ToolRegistryOfficeImoExtensions.GetRegisteredToolNames(officeImoOptions),
                ExpectedCatalog: ToolRegistryOfficeImoExtensions.GetRegisteredToolCatalog(officeImoOptions))
        };

        foreach (var @case in cases) {
            var json = await @case.Tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.True(root.GetProperty("ok").GetBoolean());
            Assert.Equal(@case.Pack, root.GetProperty("pack").GetString());
            Assert.Equal(@case.Engine, root.GetProperty("engine").GetString());
            Assert.Equal(1, root.GetProperty("guidance_version").GetInt32());

            var outputContract = root.GetProperty("output_contract");
            Assert.Equal("_view", outputContract.GetProperty("view_field_suffix").GetString());
            var viewProjectionPolicy = outputContract.GetProperty("view_projection_policy").GetString() ?? string.Empty;
            Assert.Contains("view-only", viewProjectionPolicy, StringComparison.OrdinalIgnoreCase);
            var rawPayloadPolicy = outputContract.GetProperty("raw_payload_policy").GetString() ?? string.Empty;
            Assert.False(string.IsNullOrWhiteSpace(rawPayloadPolicy));

            var actualTools = ReadStringArray(root.GetProperty("tools"));
            var expectedTools = @case.ExpectedTools
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(expectedTools, actualTools);

            var toolCatalog = root.GetProperty("tool_catalog");
            Assert.True(toolCatalog.ValueKind == JsonValueKind.Array);
            Assert.Equal(actualTools.Length, toolCatalog.GetArrayLength());
            var catalogNames = ReadCatalogNames(toolCatalog);
            Assert.Equal(actualTools, catalogNames);

            var expectedCatalogByName = @case.ExpectedCatalog.ToDictionary(
                static x => x.Name,
                StringComparer.OrdinalIgnoreCase);
            foreach (var entry in toolCatalog.EnumerateArray()) {
                var name = entry.GetProperty("name").GetString() ?? string.Empty;
                var description = entry.GetProperty("description").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.False(string.IsNullOrWhiteSpace(description));
                Assert.True(entry.TryGetProperty("required_arguments", out var requiredArguments));
                Assert.Equal(JsonValueKind.Array, requiredArguments.ValueKind);
                Assert.True(entry.TryGetProperty("arguments", out var arguments));
                Assert.Equal(JsonValueKind.Array, arguments.ValueKind);
                Assert.True(entry.TryGetProperty("supports_table_view_projection", out var supportsProjection));
                Assert.True(supportsProjection.ValueKind == JsonValueKind.True || supportsProjection.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("is_pack_info_tool", out var isPackInfo));
                Assert.True(isPackInfo.ValueKind == JsonValueKind.True || isPackInfo.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("traits", out var traits));
                Assert.Equal(JsonValueKind.Object, traits.ValueKind);
                Assert.True(entry.TryGetProperty("is_write_capable", out var isWriteCapable));
                Assert.True(isWriteCapable.ValueKind == JsonValueKind.True || isWriteCapable.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("requires_write_governance", out var requiresWriteGovernance));
                Assert.True(requiresWriteGovernance.ValueKind == JsonValueKind.True || requiresWriteGovernance.ValueKind == JsonValueKind.False);
                Assert.True(entry.TryGetProperty("write_governance_contract_id", out var writeGovernanceContractId));
                Assert.True(writeGovernanceContractId.ValueKind == JsonValueKind.String || writeGovernanceContractId.ValueKind == JsonValueKind.Null);

                Assert.True(expectedCatalogByName.TryGetValue(name, out var expectedCatalogEntry), $"Unexpected catalog entry: {name}");
                Assert.Equal(expectedCatalogEntry.Description, description);
                Assert.Equal(
                    expectedCatalogEntry.RequiredArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadStringArray(requiredArguments));
                Assert.Equal(
                    expectedCatalogEntry.Arguments.Select(static x => x.Name).OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                    ReadArgumentNames(arguments));
                AssertArgumentDetails(arguments, expectedCatalogEntry.Arguments);
                Assert.Equal(expectedCatalogEntry.SupportsTableViewProjection, supportsProjection.GetBoolean());
                Assert.Equal(expectedCatalogEntry.IsPackInfoTool, isPackInfo.GetBoolean());
                AssertTraitDetails(traits, expectedCatalogEntry.Traits);
                Assert.Equal(expectedCatalogEntry.IsWriteCapable, isWriteCapable.GetBoolean());
                Assert.Equal(expectedCatalogEntry.RequiresWriteGovernance, requiresWriteGovernance.GetBoolean());
                Assert.Equal(expectedCatalogEntry.WriteGovernanceContractId, writeGovernanceContractId.GetString());
            }

            var recommendedFlow = root.GetProperty("recommended_flow");
            Assert.True(recommendedFlow.ValueKind == JsonValueKind.Array);
            Assert.True(recommendedFlow.GetArrayLength() > 0);

            var flowSteps = root.GetProperty("recommended_flow_steps");
            Assert.True(flowSteps.ValueKind == JsonValueKind.Array);
            Assert.True(flowSteps.GetArrayLength() > 0);
            foreach (var step in flowSteps.EnumerateArray()) {
                var goal = step.GetProperty("goal").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(goal));
                var suggestedTools = ReadStringArray(step.GetProperty("suggested_tools"));
                Assert.True(suggestedTools.Length > 0);
            }

            var capabilities = root.GetProperty("capabilities");
            Assert.True(capabilities.ValueKind == JsonValueKind.Array);
            Assert.True(capabilities.GetArrayLength() > 0);
            foreach (var capability in capabilities.EnumerateArray()) {
                var capabilityId = capability.GetProperty("id").GetString() ?? string.Empty;
                var summary = capability.GetProperty("summary").GetString() ?? string.Empty;
                Assert.False(string.IsNullOrWhiteSpace(capabilityId));
                Assert.False(string.IsNullOrWhiteSpace(summary));

                var primaryTools = ReadStringArray(capability.GetProperty("primary_tools"));
                Assert.True(primaryTools.Length > 0);
            }
        }
    }

    private static string[] ReadStringArray(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadCatalogNames(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetProperty("name").GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ReadArgumentNames(JsonElement element) {
        return element
            .EnumerateArray()
            .Select(static x => x.GetProperty("name").GetString())
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AssertArgumentDetails(JsonElement actualArguments, IReadOnlyList<ToolPackToolArgumentModel> expectedArguments) {
        var expectedByName = expectedArguments.ToDictionary(
            static x => x.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var arg in actualArguments.EnumerateArray()) {
            var name = arg.GetProperty("name").GetString() ?? string.Empty;
            Assert.True(expectedByName.TryGetValue(name, out var expected), $"Unexpected argument: {name}");

            Assert.Equal(expected.Type, arg.GetProperty("type").GetString());
            Assert.Equal(expected.Required, arg.GetProperty("required").GetBoolean());
            var actualDescription = arg.TryGetProperty("description", out var descriptionNode)
                ? descriptionNode.GetString() ?? string.Empty
                : string.Empty;
            Assert.Equal(expected.Description ?? string.Empty, actualDescription);
            Assert.Equal(
                expected.EnumValues.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
                ReadStringArray(arg.GetProperty("enum_values")));
        }
    }

    private static void AssertTraitDetails(JsonElement actualTraits, ToolPackToolTraitsModel expectedTraits) {
        Assert.Equal(expectedTraits.SupportsTableViewProjection, actualTraits.GetProperty("supports_table_view_projection").GetBoolean());
        Assert.Equal(expectedTraits.SupportsPaging, actualTraits.GetProperty("supports_paging").GetBoolean());
        Assert.Equal(expectedTraits.SupportsTimeRange, actualTraits.GetProperty("supports_time_range").GetBoolean());
        Assert.Equal(expectedTraits.SupportsDynamicAttributes, actualTraits.GetProperty("supports_dynamic_attributes").GetBoolean());
        Assert.Equal(expectedTraits.SupportsTargetScoping, actualTraits.GetProperty("supports_target_scoping").GetBoolean());
        Assert.Equal(expectedTraits.SupportsMutatingActions, actualTraits.GetProperty("supports_mutating_actions").GetBoolean());
        Assert.Equal(expectedTraits.SupportsWriteGovernanceMetadata, actualTraits.GetProperty("supports_write_governance_metadata").GetBoolean());

        Assert.Equal(
            expectedTraits.TableViewArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("table_view_arguments")));
        Assert.Equal(
            expectedTraits.PagingArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("paging_arguments")));
        Assert.Equal(
            expectedTraits.TimeRangeArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("time_range_arguments")));
        Assert.Equal(
            expectedTraits.DynamicAttributeArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("dynamic_attribute_arguments")));
        Assert.Equal(
            expectedTraits.TargetScopeArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("target_scope_arguments")));
        Assert.Equal(
            expectedTraits.MutatingActionArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("mutating_action_arguments")));
        Assert.Equal(
            expectedTraits.WriteGovernanceMetadataArguments.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            ReadStringArray(actualTraits.GetProperty("write_governance_metadata_arguments")));
    }

    private sealed record PackCase(
        string Pack,
        string Engine,
        ITool Tool,
        IReadOnlyList<string> ExpectedTools,
        IReadOnlyList<ToolPackToolCatalogEntryModel> ExpectedCatalog);
}

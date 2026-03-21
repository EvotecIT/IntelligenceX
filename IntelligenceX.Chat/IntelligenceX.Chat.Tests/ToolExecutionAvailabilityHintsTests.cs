using System;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Validates shared execution-locality helper text used across host, service, and planner prompts.
/// </summary>
public sealed class ToolExecutionAvailabilityHintsTests {
    [Fact]
    public void DeferredWorkAffordanceCatalog_BuildPromptHintLines_SummarizesCapabilitiesExamplesAndBackgroundSupport() {
        var lines = DeferredWorkAffordanceCatalog.BuildPromptHintLines(new[] {
            new SessionCapabilityDeferredWorkAffordanceDto {
                CapabilityId = "background_followup",
                DisplayName = "Background Follow-up",
                Summary = "Runtime scheduler can continue deferred work.",
                AvailabilityMode = "runtime_scheduler",
                SupportsBackgroundExecution = true,
                PackIds = Array.Empty<string>(),
                RoutingFamilies = Array.Empty<string>(),
                RepresentativeExamples = Array.Empty<string>()
            },
            new SessionCapabilityDeferredWorkAffordanceDto {
                CapabilityId = "reporting",
                DisplayName = "Reporting",
                Summary = "Generate reporting artifacts.",
                AvailabilityMode = "pack_declared",
                SupportsBackgroundExecution = false,
                PackIds = new[] { "testimox_analytics" },
                RoutingFamilies = new[] { "monitoring_artifacts" },
                RepresentativeExamples = new[] { "publish a monitoring report snapshot" }
            }
        });

        Assert.Equal(4, lines.Count);
        Assert.Contains("Background Follow-up [background_followup]", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reporting [reporting]", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("publish a monitoring report snapshot", lines[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("background_followup[runtime_scheduler]", lines[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reporting[pack_declared:testimox_analytics]", lines[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Background-capable deferred follow-up is available", lines[3], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRegistrationHintLines_ExplainsLocalOnlyCatalogForRemoteWork() {
        var definitions = new[] {
            new ToolDefinition(
                name: "system_local_trace_query",
                description: "Inspect local traces only.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Host label."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                })
        };

        var lines = ToolExecutionAvailabilityHints.BuildRegistrationHintLines(definitions, hasKnownHostTargets: true);

        Assert.Single(lines);
        Assert.Contains("currently local-only", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered remote-capable tool path", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Known prior hosts/DCs", lines[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRegistrationHintLines_PrefersRegisteredRemoteReadyToolsWhenCatalogIsMixed() {
        var definitions = new[] {
            new ToolDefinition(
                name: "system_local_trace_query",
                description: "Inspect local traces only.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Host label."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                }),
            new ToolDefinition(
                name: "eventlog_live_query",
                description: "Query remote event logs.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOrRemote
                })
        };

        var lines = ToolExecutionAvailabilityHints.BuildRegistrationHintLines(definitions);

        Assert.Single(lines);
        Assert.Contains("both local-only and remote-ready tool contracts", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered remote-ready tools", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inventing a new tool name", lines[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRegistrationHintLines_PointsToRegisteredRemoteReadyContractsWhenAvailable() {
        var definitions = new[] {
            new ToolDefinition(
                name: "eventlog_live_query",
                description: "Query remote event logs.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.RemoteOnly
                })
        };

        var lines = ToolExecutionAvailabilityHints.BuildRegistrationHintLines(definitions);

        Assert.Single(lines);
        Assert.Contains("Remote-ready tool contracts are already registered", lines[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("registered host/DC-capable tools", lines[0], StringComparison.OrdinalIgnoreCase);
    }
}

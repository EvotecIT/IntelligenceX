using System.Collections.Generic;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolRepresentativeExamplesTests {
    private sealed class ExampleItem {
        public IReadOnlyList<string> DeclaredExamples { get; init; } = System.Array.Empty<string>();
        public bool MatchesDirectoryFallback { get; init; }
        public bool MatchesSetupFallback { get; init; }
        public bool MatchesPackInfoFallback { get; init; }
    }

    [Fact]
    public void CollectDeclaredExamples_ShouldTrimDeduplicateAndRespectMax() {
        var items = new[] {
            new ExampleItem {
                DeclaredExamples = new[] {
                    " inspect host posture ",
                    "inspect host posture",
                    "query event evidence"
                }
            },
            new ExampleItem {
                DeclaredExamples = new[] {
                    "review directory scope",
                    "open fallback helper"
                }
            }
        };

        var examples = ToolRepresentativeExamples.CollectDeclaredExamples(
            items,
            static item => item.DeclaredExamples,
            maxExamples: 3);

        Assert.Equal(
            new[] {
                "inspect host posture",
                "query event evidence",
                "review directory scope"
            },
            examples);
    }

    [Fact]
    public void AppendFallbackExamples_ShouldApplyOrderedRules_AndHonorCap() {
        var items = new[] {
            new ExampleItem {
                MatchesDirectoryFallback = true,
                MatchesSetupFallback = true,
                MatchesPackInfoFallback = true
            }
        };
        var examples = new List<string>();

        ToolRepresentativeExamples.AppendFallbackExamples(
            examples,
            items,
            maxExamples: 2,
            (static item => item.MatchesDirectoryFallback, ToolRepresentativeExamples.DirectoryScopeFallbackExample),
            (static item => item.MatchesSetupFallback, ToolRepresentativeExamples.SetupAwareFallbackExample),
            (static item => item.MatchesPackInfoFallback, ToolRepresentativeExamples.PackInfoFallbackExample));

        Assert.Equal(
            new[] {
                ToolRepresentativeExamples.DirectoryScopeFallbackExample,
                ToolRepresentativeExamples.SetupAwareFallbackExample
            },
            examples);
    }

    [Fact]
    public void TryAddExample_ShouldPreventDuplicates_IgnoringCase() {
        var examples = new List<string> {
            "inspect event logs"
        };

        var addedDuplicate = ToolRepresentativeExamples.TryAddExample(examples, "Inspect Event Logs");
        var addedNew = ToolRepresentativeExamples.TryAddExample(examples, "collect host posture");

        Assert.False(addedDuplicate);
        Assert.True(addedNew);
        Assert.Equal(
            new[] {
                "inspect event logs",
                "collect host posture"
            },
            examples);
    }

    [Fact]
    public void CollectTargetDisplayNames_ShouldNormalizeResolveDeduplicateAndSort() {
        var items = new[] {
            new ExampleItemWithTargets {
                TargetIds = new[] { " eventlog ", "SYSTEM" }
            },
            new ExampleItemWithTargets {
                TargetIds = new[] { "system", "eventlog" }
            }
        };

        var displayNames = ToolRepresentativeExamples.CollectTargetDisplayNames(
            items,
            static item => item.TargetIds,
            static targetId => (targetId ?? string.Empty).Trim().ToLowerInvariant(),
            static normalizedTargetId => normalizedTargetId switch {
                "eventlog" => "Event Log",
                "system" => "System",
                _ => string.Empty
            });

        Assert.Equal(
            new[] {
                "Event Log",
                "System"
            },
            displayNames);
    }

    [Fact]
    public void BuildCrossPackFormatters_ShouldReturnSharedPhrases() {
        var displayNames = new[] { "Event Log", "System" };

        Assert.Equal(
            "pivot findings into Event Log, System for follow-up checks when the workflow calls for it",
            ToolRepresentativeExamples.BuildCrossPackPivotExample(displayNames));
        Assert.Equal(
            "Cross-pack follow-up pivots are live into Event Log, System when the workflow calls for it.",
            ToolRepresentativeExamples.BuildCrossPackAvailabilityLine(displayNames, "live"));
        Assert.Equal(
            "Cross-pack follow-up pivots: Event Log, System",
            ToolRepresentativeExamples.BuildCrossPackSummary(displayNames));
    }

    [Fact]
    public void FallbackTraitHelpers_ShouldRecognizeDirectoryEventAndHostShapes() {
        Assert.True(ToolRepresentativeExamples.IsDirectoryScopeFallbackCandidate(
            isEnvironmentDiscoverTool: false,
            scope: "domain",
            supportsTargetScoping: false,
            targetScopeArguments: new[] { "search_base_dn" }));
        Assert.True(ToolRepresentativeExamples.IsEventEvidenceFallbackCandidate(
            entity: "event",
            supportsRemoteHostTargeting: true,
            supportsRemoteExecution: false,
            executionScope: "local_only"));
        Assert.True(ToolRepresentativeExamples.IsHostDiagnosticsFallbackCandidate(
            scope: "host",
            entity: "host",
            supportsRemoteHostTargeting: false,
            supportsRemoteExecution: true,
            executionScope: "local_only"));
    }

    private sealed class ExampleItemWithTargets {
        public IReadOnlyList<string> TargetIds { get; init; } = System.Array.Empty<string>();
    }
}

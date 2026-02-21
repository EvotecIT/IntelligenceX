using System;
using System.Linq;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolSelectionMetadataIdempotencyTests {

    [Fact]
    public void Enrich_ShouldBeIdempotent_ForAlreadyEnrichedDefinition() {
        var definition = new ToolDefinition(
            name: "custom_probe",
            description: "Probe",
            parameters: null,
            category: "General",
            tags: new[] {
                "TagB",
                "tagA",
                "risk:",
                "scope:   "
            });

        var enriched = ToolSelectionMetadata.Enrich(definition, toolType: null);
        var enrichedAgain = ToolSelectionMetadata.Enrich(enriched, toolType: null);

        Assert.Equal(enriched.Name, enrichedAgain.Name);
        Assert.Equal(enriched.Description, enrichedAgain.Description);
        Assert.Equal(enriched.Category, enrichedAgain.Category);
        Assert.True(enriched.Tags.SequenceEqual(enrichedAgain.Tags, StringComparer.OrdinalIgnoreCase));
        var actualTags = enriched.Tags.ToArray();
        var expectedSortedTags = actualTags.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(expectedSortedTags, actualTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:general", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("risk:", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope:", enriched.Tags, StringComparer.OrdinalIgnoreCase);
    }
}

using System;
using IntelligenceX.Tools;
using IntelligenceX.UnitTests.TestDoubles;
using Xunit;

namespace IntelligenceX.UnitTests {
    public sealed class ToolSelectionMetadataCategoryInferenceTests {

        [Fact]
        public void Enrich_ShouldPreferNamePrefixOverRuntimeTypeNamespace() {
            var definition = new ToolDefinition(
                name: "ad_custom_probe",
                description: "Probe",
                parameters: null);

            var enriched = ToolSelectionMetadata.Enrich(definition, ToolSelectionMetadataNamespaceTypes.SystemDecoratorType);

            Assert.Equal("active_directory", enriched.Category);
            Assert.Contains("active_directory", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void Enrich_ShouldFallbackToRuntimeTypeNamespace_WhenNamePrefixIsMissing() {
            var definition = new ToolDefinition(
                name: "customprobe",
                description: "Probe",
                parameters: null);

            var enriched = ToolSelectionMetadata.Enrich(definition, ToolSelectionMetadataNamespaceTypes.EventLogDecoratorType);

            Assert.Equal("eventlog", enriched.Category);
            Assert.Contains("eventlog", enriched.Tags, StringComparer.OrdinalIgnoreCase);
        }
    }
}

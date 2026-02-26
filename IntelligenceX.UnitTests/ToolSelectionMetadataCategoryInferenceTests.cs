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

        [Fact]
        public void Enrich_ShouldInferDnsCategory_FromDnsAndDomainDetectivePrefixes() {
            var dnsDefinition = new ToolDefinition(
                name: "dnsclientx_query",
                description: "Query DNS",
                parameters: null);
            var domainDetectiveDefinition = new ToolDefinition(
                name: "domaindetective_domain_summary",
                description: "Domain posture",
                parameters: null);

            var dnsEnriched = ToolSelectionMetadata.Enrich(dnsDefinition, toolType: null);
            var domainDetectiveEnriched = ToolSelectionMetadata.Enrich(domainDetectiveDefinition, toolType: null);

            Assert.Equal("dns", dnsEnriched.Category);
            Assert.Equal("dns", domainDetectiveEnriched.Category);
            Assert.Contains("dns", dnsEnriched.Tags, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("dns", domainDetectiveEnriched.Tags, StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void ResolveRouting_ShouldUseExplicitOverrides_ForDomainDetectiveTools() {
            var summary = new ToolDefinition(
                name: "domaindetective_domain_summary",
                description: "Domain summary",
                parameters: null);
            var probe = new ToolDefinition(
                name: "domaindetective_network_probe",
                description: "Network probe",
                parameters: null);

            var summaryRouting = ToolSelectionMetadata.ResolveRouting(summary);
            var probeRouting = ToolSelectionMetadata.ResolveRouting(probe);

            Assert.Equal("domain", summaryRouting.Scope);
            Assert.Equal("dns", summaryRouting.Entity);
            Assert.True(summaryRouting.IsExplicit);

            Assert.Equal("host", probeRouting.Scope);
            Assert.Equal("host", probeRouting.Entity);
            Assert.True(probeRouting.IsExplicit);
        }
    }
}

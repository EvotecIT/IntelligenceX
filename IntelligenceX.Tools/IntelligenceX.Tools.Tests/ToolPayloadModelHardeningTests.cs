using System.Reflection;
using System.Text.Json;
using ADPlayground.Gpo;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.OfficeIMO;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolPayloadModelHardeningTests {
    [Fact]
    public void AdDefenderAsrPolicy_ResponseMapping_ShouldSerializeTypedDtos() {
        var asrView = new DefenderAsrEvaluator.View(
            DomainName: "contoso.com",
            TargetDn: "OU=Domain Controllers,DC=contoso,DC=com",
            Entries: new[] { new DefenderAsrEvaluator.AsrEntry("56a863a9-875e-4185-98a7-b882c64b5ce5", 1u) },
            AnyEnabled: true);

        var cloudView = new DefenderCloudEvaluator.View(
            DomainName: "contoso.com",
            TargetDn: "OU=Domain Controllers,DC=contoso,DC=com",
            DisableBlockAtFirstSeen: 0u,
            SpynetReporting: 2u,
            SubmitSamplesConsent: 1u);

        var asr = AdDefenderAsrPolicyTool.MapAsrForResponse(asrView);
        var cloud = AdDefenderAsrPolicyTool.MapCloudForResponse(cloudView);
        var cloudFriendly = AdDefenderAsrPolicyTool.MapCloudFriendlyForResponse(cloudView);

        var json = ToolResponse.OkModel(new {
            asr,
            cloud,
            asr_entries_friendly = asr.EntriesFriendly,
            cloud_friendly = cloudFriendly
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("contoso.com", root.GetProperty("asr").GetProperty("domain_name").GetString());
        Assert.Equal("contoso.com", root.GetProperty("cloud").GetProperty("domain_name").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("asr_entries_friendly").ValueKind);
        Assert.Equal("On", root.GetProperty("cloud_friendly").GetProperty("block_at_first_sight").GetString());
        Assert.Equal("Advanced", root.GetProperty("cloud_friendly").GetProperty("maps").GetString());
    }

    [Fact]
    public void AdDefenderAsrPolicy_ResultContract_ShouldNotUseObjectTypedFields() {
        var resultType = typeof(AdDefenderAsrPolicyTool).GetNestedType(
            "AdDefenderAsrPolicyResult",
            BindingFlags.NonPublic);

        Assert.NotNull(resultType);
        Assert.DoesNotContain(resultType!.GetProperties(), static property => property.PropertyType == typeof(object));
    }

    [Fact]
    public void OfficeImoChunk_ShouldSerializeTypedLocationAndTables() {
        var chunk = new OfficeImoChunk {
            Id = "chunk-1",
            Kind = "markdown",
            Text = "Hello",
            Location = new OfficeImoChunkLocation {
                Path = @"C:\\docs\\notes.md",
                BlockIndex = 2,
                StartLine = 5
            },
            Tables = new[] {
                new OfficeImoChunkTable {
                    Title = "Sample",
                    Columns = new[] { "name", "value" },
                    Rows = new[] { new[] { "alpha", "1" } },
                    TotalRowCount = 1,
                    Truncated = false
                }
            }
        };

        var json = ToolResponse.OkModel(new { chunks = new[] { chunk } });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var first = root.GetProperty("chunks")[0];

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(@"C:\\docs\\notes.md", first.GetProperty("location").GetProperty("path").GetString());
        Assert.Equal(2, first.GetProperty("location").GetProperty("block_index").GetInt32());
        Assert.Equal(JsonValueKind.Array, first.GetProperty("tables").ValueKind);
        Assert.Equal("Sample", first.GetProperty("tables")[0].GetProperty("title").GetString());
    }
}

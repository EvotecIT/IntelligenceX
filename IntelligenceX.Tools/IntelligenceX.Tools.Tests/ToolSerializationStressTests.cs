using System.DirectoryServices.ActiveDirectory;
using System.Text.Json;
using ADPlayground.Replication;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.OfficeIMO;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolSerializationStressTests {
    [Fact]
    public void AdScopeDiscoveryStylePayload_WithLargeReceiptAndCycle_ShouldSerialize() {
        var cyclicOutput = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["count"] = 2,
            ["sample"] = new[] { "dc01.contoso.com", "dc02.contoso.com" }
        };
        cyclicOutput["self"] = cyclicOutput;

        var steps = Enumerable.Range(0, 180)
            .Select(index => new {
                name = $"domain_controllers:dns_srv:{index}",
                ok = index % 7 != 0,
                duration_ms = 5 + index,
                timeout_ms = 2_000,
                endpoints_checked = new[] { $"dc{index:000}.contoso.com" },
                retries = 0,
                output = index == 0
                    ? (object?)cyclicOutput
                    : new Dictionary<string, object?>(StringComparer.Ordinal) {
                        ["count"] = index,
                        ["sample"] = new[] { $"dc{index:000}.contoso.com" },
                        ["flags"] = new[] { true, false }
                    }
            })
            .ToArray();

        var json = ToolResponse.OkModel(new {
            receipt = new {
                steps,
                summary = new { domains = 3, domain_controllers = 180, failed_steps = 26, total_steps = steps.Length }
            }
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(180, root.GetProperty("receipt").GetProperty("steps").GetArrayLength());
        Assert.Equal("[cycle]", root.GetProperty("receipt").GetProperty("steps")[0].GetProperty("output").GetProperty("self").GetString());
    }

    [Fact]
    public void AdForestDiscoverStylePayload_WithDeepNestingAndLargeCollections_ShouldSerialize() {
        var deep = CreateDeepNode(14);
        var domains = Enumerable.Range(0, 220).Select(static index => $"child{index:000}.contoso.com").ToArray();
        var dcs = Enumerable.Range(0, 300).Select(static index => $"dc{index:000}.contoso.com").ToArray();

        var json = ToolResponse.OkModel(new {
            domains,
            domain_controllers = dcs,
            receipt = new {
                deep,
                summary = new { domains = domains.Length, domain_controllers = dcs.Length, trusts = 4 }
            }
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(220, root.GetProperty("domains").GetArrayLength());
        Assert.Equal(300, root.GetProperty("domain_controllers").GetArrayLength());
    }

    [Fact]
    public void AdReplicationConnectionsPayload_WithManyMappedRows_ShouldSerialize() {
        var rows = new List<object>(320);
        for (var i = 0; i < 320; i++) {
            var schedule = new ActiveDirectorySchedule();
            var raw = new bool[7, 24, 4];
            raw[i % 7, i % 24, i % 4] = true;
            schedule.RawSchedule = raw;

            var connection = new SiteConnectionInfo(
                Name: $"CN=Conn-{i:000}",
                Site: "Default-First-Site-Name",
                SourceServer: $"DC{i:000}",
                DestinationServer: $"DC{i + 1:000}",
                Transport: ActiveDirectoryTransportType.Rpc,
                Enabled: true,
                GeneratedByKcc: true,
                ReciprocalReplicationEnabled: i % 2 == 0,
                ChangeNotificationStatus: NotificationStatus.IntraSiteOnly,
                DataCompressionEnabled: true,
                ReplicationScheduleOwnedByUser: false,
                ReplicationSpan: ReplicationSpan.InterSite,
                ReplicationSchedule: schedule);

            rows.Add(AdReplicationConnectionsTool.MapConnectionForResponse(connection));
        }

        var json = ToolResponse.OkModel(new {
            scanned = rows.Count,
            truncated = false,
            connections = rows
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(320, root.GetProperty("connections").GetArrayLength());
    }

    [Fact]
    public void OfficeImoReadPayload_WithHighVolumeChunks_ShouldSerialize() {
        var chunks = Enumerable.Range(0, 420).Select(index => new OfficeImoChunk {
            Id = $"chunk-{index:0000}",
            Kind = "markdown",
            Text = $"Paragraph {index}",
            Location = new OfficeImoChunkLocation {
                Path = $@"C:\\docs\\{index:0000}.md",
                BlockIndex = index,
                StartLine = index + 1
            },
            Tables = new[] {
                new OfficeImoChunkTable {
                    Title = "Sample",
                    Columns = new[] { "name", "value" },
                    Rows = new[] { new[] { "alpha", index.ToString() } },
                    TotalRowCount = 1,
                    Truncated = false
                }
            },
            TokenEstimate = 16
        }).ToArray();

        var json = ToolResponse.OkModel(new {
            output_mode = "chunks",
            chunks_returned = chunks.Length,
            chunks
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var chunkArray = root.GetProperty("chunks");

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(420, chunkArray.GetArrayLength());
        Assert.Equal("Sample", chunkArray[0].GetProperty("tables")[0].GetProperty("title").GetString());
    }

    [Fact]
    public void DictionaryPayload_WithCustomKeysAndNestedCycles_ShouldSerializeSafely() {
        var cyclicBag = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["status"] = "ok",
            ["attempt"] = 1
        };
        cyclicBag["self"] = cyclicBag;

        var payload = new Dictionary<object, object?> {
            [new DictionaryKey("alpha", 1)] = cyclicBag,
            [new DictionaryKey("beta", 2)] = new Dictionary<string, object?> {
                ["count"] = 2,
                ["values"] = new[] { "x", "y" }
            }
        };

        var json = ToolResponse.OkModel(new {
            dictionary_payload = payload
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var dictionary = root.GetProperty("dictionary_payload");

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.Object, dictionary.ValueKind);
        Assert.Equal("[cycle]", dictionary.GetProperty("alpha-1").GetProperty("self").GetString());
        Assert.Equal(2, dictionary.GetProperty("beta-2").GetProperty("count").GetInt32());
    }

    private static StressNode CreateDeepNode(int depth) {
        var root = new StressNode { Name = "root" };
        var current = root;
        for (var i = 0; i < depth; i++) {
            var next = new StressNode { Name = $"level-{i:00}" };
            current.Child = next;
            current = next;
        }

        return root;
    }

    private sealed class StressNode {
        public string Name { get; set; } = string.Empty;

        public StressNode? Child { get; set; }
    }

    private sealed class DictionaryKey {
        private readonly string _name;
        private readonly int _index;

        public DictionaryKey(string name, int index) {
            _name = name;
            _index = index;
        }

        public override string ToString() {
            return $"{_name}-{_index}";
        }
    }
}

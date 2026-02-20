using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class AdHandoffPrepareToolTests {
    private sealed record NamedEventRow(string? Who, string? ObjectAffected, string? Computer);
    private sealed record TimelineRow(string? Who, string? ObjectAffected, string? Computer);

    [Fact]
    public async Task InvokeAsync_WhenEntityHandoffMissing_ReturnsInvalidArgument() {
        var tool = new AdHandoffPrepareTool(new ActiveDirectoryToolOptions());

        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("entity_handoff", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenEntityHandoffContractUnsupported_ReturnsInvalidArgument() {
        var tool = new AdHandoffPrepareTool(new ActiveDirectoryToolOptions());
        var args = new JsonObject()
            .Add("entity_handoff", new JsonObject()
                .Add("contract", "other_contract")
                .Add("identity_candidates", new JsonArray()
                    .Add(new JsonObject().Add("value", "alice"))));

        var json = await tool.InvokeAsync(args, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
        Assert.Contains("contract", root.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WithNamedEventsStyleHandoff_ShouldProduceAdReadyIdentityTargets() {
        var rows = new[] {
            new NamedEventRow("alice", "CN=Alice,CN=Users,DC=lab,DC=local", "DC01.lab.local"),
            new NamedEventRow("alice", null, "DC01.lab.local"),
            new NamedEventRow("svc_sql", null, "SQL01.lab.local")
        };
        var handoff = EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);

        var tool = new AdHandoffPrepareTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("entity_handoff", handoff)
                .Add("max_identities", 8),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("alice", root.GetProperty("primary_identity").GetString());

        var identities = root.GetProperty("identities")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Contains("alice", identities, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DC01.lab.local", identities, StringComparer.OrdinalIgnoreCase);

        var resolveTargets = root
            .GetProperty("target_arguments")
            .GetProperty("ad_object_resolve")
            .GetProperty("identities")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Equal(identities, resolveTargets, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WithTimelineStyleHandoffAndComputerExclusion_ShouldDropComputerOnlyCandidates() {
        var rows = new[] {
            new TimelineRow("bob", "CN=Bob,CN=Users,DC=lab,DC=local", "DC02.lab.local"),
            new TimelineRow("bob", "CN=Bob,CN=Users,DC=lab,DC=local", "DC03.lab.local"),
            new TimelineRow("-", null, "DC04.lab.local")
        };
        var handoff = EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);

        var tool = new AdHandoffPrepareTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("entity_handoff", handoff)
                .Add("include_computers", false)
                .Add("max_identities", 8),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());

        var identities = root.GetProperty("identities")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Contains("bob", identities, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("CN=Bob,CN=Users,DC=lab,DC=local", identities, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DC04.lab.local", identities, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_WhenMaxIdentitiesReached_ShouldMarkTruncatedAndApplyMinimumCount() {
        var tool = new AdHandoffPrepareTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(
            new JsonObject()
                .Add("entity_handoff", new JsonObject()
                    .Add("contract", "eventlog_entity_handoff")
                    .Add("version", 1)
                    .Add("identity_candidates", new JsonArray()
                        .Add(new JsonObject().Add("value", "a").Add("count", 1))
                        .Add(new JsonObject().Add("value", "b").Add("count", 3))
                        .Add(new JsonObject().Add("value", "c").Add("count", 2)))
                    .Add("computer_candidates", new JsonArray()
                        .Add(new JsonObject().Add("value", "dc01.lab.local").Add("count", 10))))
                .Add("min_candidate_count", 2)
                .Add("max_identities", 2),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("meta").GetProperty("truncated").GetBoolean());

        var identities = root.GetProperty("identities")
            .EnumerateArray()
            .Select(static value => value.GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.Equal(2, identities.Length);
        Assert.Contains("b", identities, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("a", identities, StringComparer.OrdinalIgnoreCase);
    }
}

using System.Linq;
using IntelligenceX.Tools.EventLog;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class EventLogEntityHandoffTests {
    private sealed record Row(string? Who, string? ObjectAffected, string? Computer);

    [Fact]
    public void BuildFromRows_ShouldAggregateDistinctCandidatesAndSourceFields() {
        var rows = new[] {
            new Row("alice", "CN=Alice,CN=Users,DC=lab,DC=local", "DC01.lab.local"),
            new Row("alice", " ", "dc01.lab.local"),
            new Row("bob", "CN=Alice,CN=Users,DC=lab,DC=local", "DC02.lab.local"),
            new Row("-", null, null)
        };

        var handoff = EventLogEntityHandoff.BuildFromRows(
            rows: rows,
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);

        Assert.Equal("eventlog_entity_handoff", handoff.GetString("contract"));
        Assert.Equal(1, handoff.GetInt64("version"));
        Assert.Equal(4, handoff.GetInt64("scanned_rows"));
        Assert.Equal(5, handoff.GetInt64("identity_candidates_total"));
        Assert.Equal(2, handoff.GetInt64("computer_candidates_total"));

        var identityCandidates = handoff.GetArray("identity_candidates");
        Assert.NotNull(identityCandidates);
        Assert.True(identityCandidates!.Count >= 1);

        var topIdentity = identityCandidates[0].AsObject();
        Assert.NotNull(topIdentity);
        Assert.Equal("alice", topIdentity.GetString("value"));
        Assert.Equal(2, topIdentity.GetInt64("count"));
        var sourceFields = topIdentity.GetArray("source_fields");
        Assert.NotNull(sourceFields);
        Assert.Equal(
            new[] { "who" },
            sourceFields!.Select(static x => x.AsString()).Where(static x => !string.IsNullOrWhiteSpace(x)));

        var hints = handoff.GetArray("target_hints");
        Assert.NotNull(hints);
        Assert.Equal(2, hints!.Count);
        var firstHint = hints[0].AsObject();
        Assert.NotNull(firstHint);
        Assert.Equal("ad_object_resolve", firstHint.GetString("tool"));
        Assert.Equal("identities", firstHint.GetString("argument"));
        var hintValues = firstHint.GetArray("values");
        Assert.NotNull(hintValues);
        Assert.True(hintValues!.Count >= 1);
    }

    [Fact]
    public void BuildFromRows_WhenInputEmpty_ShouldReturnEmptyCandidateCollections() {
        var handoff = EventLogEntityHandoff.BuildFromRows(
            rows: Enumerable.Empty<Row>(),
            whoSelector: static row => row.Who,
            objectAffectedSelector: static row => row.ObjectAffected,
            computerSelector: static row => row.Computer);

        Assert.Equal(0, handoff.GetInt64("scanned_rows"));
        Assert.Equal(0, handoff.GetInt64("identity_candidates_total"));
        Assert.Equal(0, handoff.GetInt64("computer_candidates_total"));
        var identityCandidates = handoff.GetArray("identity_candidates");
        Assert.NotNull(identityCandidates);
        Assert.Equal(0, identityCandidates!.Count);
        var computerCandidates = handoff.GetArray("computer_candidates");
        Assert.NotNull(computerCandidates);
        Assert.Equal(0, computerCandidates!.Count);

        var hints = handoff.GetArray("target_hints");
        Assert.NotNull(hints);
        Assert.Equal(2, hints!.Count);
        foreach (var hint in hints) {
            var hintObject = hint.AsObject();
            Assert.NotNull(hintObject);
            var values = hintObject.GetArray("values");
            Assert.NotNull(values);
            Assert.Equal(0, values!.Count);
        }
    }
}

using System.Net;
using IntelligenceX.Ledger.Reporting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ProjectClaimReaderTests {
    [Theory]
    [InlineData("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE")]
    [InlineData("{aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee}")]
    public async Task SessionGuidAliasesAttachClaimsAndLinksToTheCanonicalSession(string session) {
        var json = """
            [{"node_id":"synthetic-item","fields":[
             {"id":1,"data_type":"text","value":{"raw":"codex:SESSION"}},
             {"id":2,"data_type":"single_select","value":{"name":{"raw":"synthetic-host"}}},
             {"id":3,"data_type":"single_select","value":{"name":{"raw":"Active"}}},
             {"id":4,"data_type":"text","value":{"raw":"https://github.com/Sample/Project/pull/1"}}]}]
            """.Replace("SESSION", session);
        using var client = new HttpClient(new FixtureHandler(json));
        var reader = new ProjectClaimReader(client, new() { Organization = "synthetic", ProjectNumber = 1,
            AgentFieldId = 1, MachineFieldId = 2, StatusFieldId = 3, PullRequestFieldId = 4,
            MachineAliases = new() { ["synthetic-host"] = "machine-a" } });
        var snapshot = await reader.ReadAsync(default);
        var id = LocalReporter.ItemId("machine-a", "codex", "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        Assert.Equal(id, Assert.Single(snapshot.ActiveItems));
        Assert.Equal("https://github.com/Sample/Project/pull/1", Assert.Single(snapshot.PullRequestUrlsByItem[id]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedSessionAcrossPagesUnionsLinksAndRetainsAnyIncompleteAssociation(bool invalidEarlierLink) {
        using var client = new HttpClient(new CursorHandler(false, true, invalidEarlierLink));
        var reader = new ProjectClaimReader(client, new() { Organization = "synthetic", ProjectNumber = 1,
            AgentFieldId = 1, MachineFieldId = 2, StatusFieldId = 3, PullRequestFieldId = 4,
            MachineAliases = new() { ["synthetic-host"] = "machine-a" } });
        var snapshot = await reader.ReadAsync(default);
        var id = LocalReporter.ItemId("machine-a", "codex", "11111111-1111-1111-1111-111111111111");
        Assert.Equal(invalidEarlierLink ? 1 : 2, snapshot.PullRequestUrlsByItem[id].Length);
        Assert.Contains("https://github.com/Sample/Project/pull/2", snapshot.PullRequestUrlsByItem[id]);
        Assert.Equal(invalidEarlierLink, snapshot.IncompletePullRequestItems.Contains(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContinuationUsesReturnedCursorAndNeverForwardsCredentialsToAnotherHost(bool foreignHost) {
        using var client = new HttpClient(new CursorHandler(foreignHost));
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-credential");
        var reader = new ProjectClaimReader(client, new() { Organization = "synthetic", ProjectNumber = 1,
            AgentFieldId = 1, MachineFieldId = 2, StatusFieldId = 3, PullRequestFieldId = 4,
            MachineAliases = new() { ["synthetic-host"] = "machine-a" } });
        if (foreignHost) await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(default));
        else {
            var snapshot = await reader.ReadAsync(default);
            Assert.Equal(2, snapshot.ActiveItems.Count);
            Assert.Contains(LocalReporter.ItemId("machine-a", "codex", "11111111-1111-1111-1111-111111111111"), snapshot.ActiveItems);
            Assert.Contains(LocalReporter.ItemId("machine-a", "codex", "22222222-2222-2222-2222-222222222222"), snapshot.ActiveItems);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOptionalFieldIsKnownEmptyButMissingRequestedFieldRejectsSnapshot(bool missingField) {
        var json = """
            [{"node_id":"synthetic-item","archived_at":null,"fields":[
             {"id":1,"data_type":"text","value":{"raw":"codex:11111111-1111-1111-1111-111111111111"}},
             {"id":2,"data_type":"single_select","value":{"name":{"raw":"synthetic-host"}}},
             {"id":3,"data_type":"single_select","value":{"name":{"raw":"Active"}}},
             {"id":4,"data_type":"text"}]}]
            """;
        // Remove the entire requested field for the incomplete-source control.
        if (missingField) {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            node[0]!["fields"]!.AsArray().RemoveAt(3);
            json = node.ToJsonString();
        }
        using var client = new HttpClient(new FixtureHandler(json));
        var reader = new ProjectClaimReader(client, new() { Organization = "synthetic", ProjectNumber = 1,
            AgentFieldId = 1, MachineFieldId = 2, StatusFieldId = 3, PullRequestFieldId = 4,
            MachineAliases = new() { ["synthetic-host"] = "machine-a" } });
        if (missingField) await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(default));
        else {
            var snapshot = await reader.ReadAsync(default);
            Assert.Single(snapshot.ActiveItems);
            Assert.Empty(snapshot.ItemsWithPullRequests);
        }
    }

    [Fact]
    public async Task UnchangedPagesAreReadWithTheirETagAndReuseThePreviousRows() {
        var handler = new ConditionalHandler();
        using var client = new HttpClient(handler);
        var reader = new ProjectClaimReader(client, new() { Organization = "synthetic", ProjectNumber = 1,
            AgentFieldId = 1, MachineFieldId = 2, StatusFieldId = 3, PullRequestFieldId = 4,
            MachineAliases = new() { ["synthetic-host"] = "machine-a" } });
        var first = await reader.ReadAsync(default);
        Assert.Equal([null, null], handler.Conditions);
        Assert.Equal(2, first.ActiveItems.Count);

        // Both pages unchanged: GitHub answers 304 without a body or Link header, and the snapshot is rebuilt from the cache.
        handler.Conditions.Clear();
        var second = await reader.ReadAsync(default);
        Assert.Equal(["W/\"page-1-v1\"", "W/\"page-2-v1\""], handler.Conditions);
        Assert.Equal(2, reader.NotModified);
        Assert.Equal(first.ActiveItems, second.ActiveItems);
        Assert.Equal(first.PullRequestUrlsByItem.Keys, second.PullRequestUrlsByItem.Keys);

        // A changed page is read again and replaces its cached rows; the unchanged page is still reused.
        handler.Conditions.Clear();
        handler.SecondPageStatus = "Done";
        var third = await reader.ReadAsync(default);
        Assert.Equal(["W/\"page-1-v1\"", "W/\"page-2-v1\""], handler.Conditions);
        Assert.Equal(3, reader.NotModified);
        Assert.Equal(LocalReporter.ItemId("machine-a", "codex", "11111111-1111-1111-1111-111111111111"), Assert.Single(third.ActiveItems));
        Assert.Equal(6, reader.Requests);
    }

    private sealed class FixtureHandler(string json) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }

    private sealed class CursorHandler(bool foreignHost, bool sameSession = false, bool invalidEarlierLink = false) : HttpMessageHandler {
        private int _page;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            var page = ++_page;
            Assert.InRange(page, 1, 2);
            if (page == 2) Assert.Equal("?after=synthetic-cursor", request.RequestUri.Query);
            var id = page == 1 || sameSession ? "11111111-1111-1111-1111-111111111111" : "22222222-2222-2222-2222-222222222222";
            var json = """
                [{"node_id":"synthetic-PAGE","fields":[
                {"id":1,"data_type":"text","value":{"raw":"codex:SESSION"}},
                {"id":2,"data_type":"single_select","value":{"name":{"raw":"synthetic-host"}}},
                {"id":3,"data_type":"single_select","value":{"name":{"raw":"Active"}}},
                {"id":4,"data_type":"text","value":{"raw":"LINK"}}]}]
                """.Replace("PAGE", page.ToString()).Replace("SESSION", id).Replace("LINK", !sameSession ? "" :
                    page == 1 && invalidEarlierLink ? "unresolved-link" : "https://github.com/Sample/Project/pull/" + page);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            if (page == 1) response.Headers.TryAddWithoutValidation("Link",
                $"<https://{(foreignHost ? "example.invalid" : "api.github.com")}/organizations/1/projectsV2/1/items?after=synthetic-cursor>; rel=\"next\"");
            return Task.FromResult(response);
        }
    }
    private sealed class ConditionalHandler : HttpMessageHandler {
        internal List<string?> Conditions { get; } = [];
        internal string SecondPageStatus { get; set; } = "Active";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var page = request.RequestUri!.Query.Contains("after=synthetic-cursor", StringComparison.Ordinal) ? 2 : 1;
            var condition = request.Headers.TryGetValues("If-None-Match", out var values) ? values.Single() : null;
            Conditions.Add(condition);
            var status = page == 1 ? "Active" : SecondPageStatus;
            var etag = $"W/\"page-{page}-{(status == "Active" ? "v1" : "v2")}\"";
            if (condition == etag) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            var json = """
                [{"node_id":"synthetic-PAGE","fields":[
                {"id":1,"data_type":"text","value":{"raw":"codex:SESSION"}},
                {"id":2,"data_type":"single_select","value":{"name":{"raw":"synthetic-host"}}},
                {"id":3,"data_type":"single_select","value":{"name":{"raw":"STATUS"}}},
                {"id":4,"data_type":"text","value":{"raw":"https://github.com/Sample/Project/pull/PAGE"}}]}]
                """.Replace("PAGE", page.ToString()).Replace("STATUS", status)
                .Replace("SESSION", page == 1 ? "11111111-1111-1111-1111-111111111111" : "22222222-2222-2222-2222-222222222222");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            response.Headers.TryAddWithoutValidation("ETag", etag);
            if (page == 1) response.Headers.TryAddWithoutValidation("Link",
                "<https://api.github.com/organizations/1/projectsV2/1/items?after=synthetic-cursor>; rel=\"next\"");
            return Task.FromResult(response);
        }
    }
}

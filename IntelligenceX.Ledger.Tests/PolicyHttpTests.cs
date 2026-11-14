using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class PolicyHttpTests {
    [Fact]
    public async Task OrdinaryReadsExposePolicyWhileOnlyTheOperatorCanEditAnAuditedValue() {
        var operatorToken = new string('o', 32);
        var requesterToken = new string('c', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 2, 2, 8, 8192, ["build"])]),
            Principals = [new("editor", "operator", operatorToken, []), new("requester", "client", requesterToken, ["sample"])]
        }, async (endpoint, settings, token) => {
            using var reader = new LedgerReadClient(new() { Endpoint = endpoint, ReadToken = settings.ReadToken });
            Assert.Equal(2, Assert.Single((await reader.ReadPolicyAsync(token)).Machines).BuildSlots);
            using var document = JsonDocument.Parse("3");
            var command = new PolicyValueCommand("machines.synthetic-machine.buildSlots", document.RootElement.Clone(), 1);
            using var http = new HttpClient { BaseAddress = new(endpoint) };
            foreach (var credential in new[] { settings.ReadToken, settings.WriteToken }) {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                using var denied = await http.PostAsJsonAsync("api/coordination/v1/policy/value", command, token);
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            }
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requesterToken);
            using (var denied = await http.PostAsJsonAsync("api/coordination/v1/policy/value", command, token))
                Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            using var editor = LedgerReadClient.ForCoordination(new(endpoint, operatorToken));
            Assert.Equal(2, (await editor.SetPolicyValueAsync(command, token)).Revision);
            Assert.Equal(3, Assert.Single((await reader.ReadPolicyAsync(token)).Machines).BuildSlots);
            var changes = await editor.ChangesAsync(0, token);
            var audit = Assert.Single(changes.Changes);
            Assert.Equal("editor", audit.Principal);
            Assert.Equal(3, Assert.Single(audit.PolicyChanges!, change => change.Path == command.Path).NewValue.GetInt32());
        });
    }
}

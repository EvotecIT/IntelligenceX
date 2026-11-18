using System.Net;
using System.Net.Http.Headers;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LeaseApprovalTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = LedgerTestPaths.NewDirectory("lease-approval");
    private string Database => Path.Combine(_root, "ledger.sqlite");
    private static CoordinationPolicy Policy => new(1, [new("synthetic", 4, 2, 8, 8192, ["build"])]);

    [Fact]
    public async Task LongerThanPolicyWaitsForApprovalAndTheApprovedLengthBoundsRenewals() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        var asked = await store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "long suite", 10800), Now);
        Assert.False(asked.Ok);
        Assert.Equal("needs-approval", asked.State);
        Assert.Equal("ttl-exceeds-policy", asked.Reason);
        Assert.Equal(3600, asked.MaximumTtlSeconds);
        Assert.Equal(0, (await store.WhoAsync("test:synthetic", false, Now)).Total);
        var again = await store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "long suite", 10800), Now.AddMinutes(1));
        Assert.Equal(asked.Lease!.LeaseId, again.Lease!.LeaseId);
        var pending = Assert.Single((await store.PendingLeaseApprovalsAsync(Now.AddMinutes(1))).Pending);
        Assert.Equal((10800, 3600), (pending.RequestedTtlSeconds, pending.MaximumTtlSeconds));

        var approved = await store.ApproveLeaseAsync(new(asked.Lease.LeaseId, true, 7200), Now.AddMinutes(2));
        Assert.True(approved.Ok);
        Assert.Equal("acquired", approved.State);
        Assert.Equal(7200, approved.Lease!.ApprovedTtlSeconds);
        Assert.Equal(Now.AddMinutes(2).AddSeconds(7200), approved.Lease.ExpiresAtUtc);
        Assert.Empty((await store.PendingLeaseApprovalsAsync(Now.AddMinutes(2))).Pending);

        await Session(store, "worker", Now.AddMinutes(3));
        Assert.Equal("renewed", (await store.RenewLeaseAsync(new("test:synthetic", approved.Lease.LeaseId, 7200), Now.AddMinutes(3))).State);
        var tooLong = await store.RenewLeaseAsync(new("test:synthetic", approved.Lease.LeaseId, 7201), Now.AddMinutes(3));
        Assert.Equal(("ttl-exceeds-policy", 7200), (tooLong.State, tooLong.MaximumTtlSeconds));
        Assert.Equal("not-pending", (await store.ApproveLeaseAsync(new(asked.Lease.LeaseId, true), Now.AddMinutes(3))).State);
    }

    [Fact]
    public async Task DenialAndShorterAsksClearTheWaitingRequestAndUnapprovedRenewalsStayWithinPolicy() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        var first = await store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "long suite", 10800), Now);
        Assert.Equal("denied", (await store.ApproveLeaseAsync(new(first.Lease!.LeaseId, false), Now)).State);
        Assert.Empty((await store.PendingLeaseApprovalsAsync(Now)).Pending);

        await store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "long suite", 10800), Now);
        var shorter = await store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "short suite", 1800), Now);
        Assert.Equal("acquired", shorter.State);
        Assert.Empty((await store.PendingLeaseApprovalsAsync(Now)).Pending);

        await Session(store, "worker", Now);
        var renewal = await store.RenewLeaseAsync(new("test:synthetic", shorter.Lease!.LeaseId, 3601), Now);
        Assert.Equal(("ttl-exceeds-policy", 3600), (renewal.State, renewal.MaximumTtlSeconds));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "suite", 8 * 24 * 3600), Now));
    }

    [Fact]
    public async Task AnApprovedBenchmarkMayExceedTheDailyBudget() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        var asked = await store.AcquireLeaseAsync(new("bench:synthetic", "worker", "synthetic", "overnight", 8 * 3600), Now);
        Assert.Equal(("needs-approval", 7200), (asked.State, asked.MaximumTtlSeconds));
        var approved = await store.ApproveLeaseAsync(new(asked.Lease!.LeaseId, true), Now);
        Assert.Equal("acquired", approved.State);
        Assert.Equal(Now.AddHours(8), approved.Lease!.ExpiresAtUtc);
    }

    [Fact]
    public async Task AShorterApprovalBoundsRenewalsBelowThePolicyMaximum() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        var asked = await store.AcquireLeaseAsync(new("test:synthetic", "worker", "synthetic", "long suite", 10800), Now);
        var approved = await store.ApproveLeaseAsync(new(asked.Lease!.LeaseId, true, 1800), Now);
        await Session(store, "worker", Now);
        var renewal = await store.RenewLeaseAsync(new("test:synthetic", approved.Lease!.LeaseId, 3600), Now);
        Assert.Equal(("ttl-exceeds-policy", 1800), (renewal.State, renewal.MaximumTtlSeconds));
    }

    [Fact]
    public async Task BusyRepliesNameTheActiveHolderAndBranchesNeverWaitForApproval() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        Assert.Equal("needs-approval", (await store.AcquireLeaseAsync(new("resource:x", "waiting", "synthetic", "long", 2 * 86400), Now)).State);
        var holder = await store.AcquireLeaseAsync(new("resource:x", "holder", "synthetic", "short", 600), Now);
        var busy = await store.AcquireLeaseAsync(new("resource:x", "third", "synthetic", "short", 600), Now);
        Assert.Equal(("busy", holder.Lease!.LeaseId), (busy.State, busy.Lease!.LeaseId));

        var branch = await store.AcquireLeaseAsync(new("branch:example/repo:feature", "worker", "synthetic", "edit", 481 * 60), Now);
        Assert.Equal(("ttl-exceeds-policy", 480 * 60), (branch.State, branch.MaximumTtlSeconds));
        Assert.DoesNotContain((await store.PendingLeaseApprovalsAsync(Now)).Pending, p => p.Lease.Key.StartsWith("branch:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnApprovalKeepsItsPlaceAheadOfAYoungerWaiterWhenTheSlotFreesFirst() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        Assert.True((await store.AcquireLeaseAsync(new("bench:synthetic", "first", "synthetic", "measure", 600), Now)).Ok);
        var asked = await store.AcquireLeaseAsync(new("bench:synthetic", "overnight", "synthetic", "measure", 8 * 3600), Now.AddSeconds(1));
        Assert.Equal("queued", (await store.AcquireLeaseAsync(new("bench:synthetic", "younger", "synthetic", "measure", 600), Now.AddSeconds(2))).State);
        var approved = await store.ApproveLeaseAsync(new(asked.Lease!.LeaseId, true), Now.AddMinutes(11));
        Assert.Equal("acquired", approved.State);
        Assert.Equal("overnight", Assert.Single((await store.WhoAsync("bench:synthetic", false, Now.AddMinutes(11))).Leases).Holder);
    }

    [Theory]
    [InlineData(new[] { "lease-approvals", "--config", "x" }, null, false, 0)]
    [InlineData(new[] { "lease-approvals", "--config", "x", "--deny", "id" }, "id", false, 0)]
    [InlineData(new[] { "lease-approvals", "--config", "x", "--approve", "id" }, "id", true, 0)]
    [InlineData(new[] { "lease-approvals", "--config", "x", "--approve", "id", "--minutes", "480" }, "id", true, 28800)]
    public void CommandLineReadsListApproveAndDeny(string[] args, string? id, bool approve, int ttl) {
        Assert.True(LeaseApprovalCommand.TryParse(args, out var approval));
        Assert.Equal(id, approval?.LeaseId);
        if (approval is not null) Assert.Equal((approve, ttl), (approval.Approve, approval.TtlSeconds));
    }

    [Theory]
    [InlineData("--approve", "id", "--minutes", "0")]
    [InlineData("--approve", "id", "--minutes", "-5")]
    [InlineData("--approve", "id", "--minutes", "10081")]
    [InlineData("--deny")]
    public void CommandLineRejectsIncompleteOrOutOfRangeApprovals(params string[] rest) =>
        Assert.False(LeaseApprovalCommand.TryParse(["lease-approvals", "--config", "x", .. rest], out _));

    [Fact]
    public async Task OnlyTheOperatorCanListOrApproveOverHttp() {
        var operatorToken = new string('o', 32);
        var requesterToken = new string('c', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 2, 2, 8, 8192, ["build"])]),
            Principals = [new("editor", "operator", operatorToken, []), new("requester", "client", requesterToken, ["sample"])]
        }, async (endpoint, settings, token) => {
            using var leases = LedgerReadClient.ForLeases(new() { Endpoint = endpoint, ReadToken = settings.ReadToken, WriteToken = settings.WriteToken });
            var asked = await leases.AcquireLeaseAsync(new("test:synthetic-machine", "worker", "synthetic-machine", "long suite", 10800), token);
            Assert.Equal(("needs-approval", 3600), (asked.State, asked.MaximumTtlSeconds));

            using var http = new HttpClient { BaseAddress = new(endpoint) };
            foreach (var credential in new[] { settings.WriteToken, requesterToken }) {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
                using var denied = await http.GetAsync("api/coordination/v1/lease-approvals", token);
                Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
            }
            using var editor = LedgerReadClient.ForCoordination(new(endpoint, operatorToken));
            Assert.Equal(asked.Lease!.LeaseId, Assert.Single((await editor.PendingLeaseApprovalsAsync(token)).Pending).Lease.LeaseId);
            var approved = await editor.ApproveLeaseAsync(new(asked.Lease.LeaseId, true, 5400), token);
            Assert.Equal(("acquired", 5400), (approved.State, approved.Lease!.ApprovedTtlSeconds));
        });
    }

    private static Task<Guid[]> Session(LedgerStore store, string holder, DateTimeOffset at) => store.IngestAsync([new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = "synthetic", ReporterId = "reporter",
        ItemId = holder, Provider = "codex", Source = "watcher", ObservedAtUtc = at,
        Session = new() { Origin = "agent", LastActivityAtUtc = at, TurnOpen = true }
    }], at);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}

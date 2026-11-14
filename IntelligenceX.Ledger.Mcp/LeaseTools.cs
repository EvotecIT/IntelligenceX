using System.ComponentModel;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol.Server;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Explicitly write-enrolled reservations. Expiry releases ownership without a follow-up turn.</summary>
[McpServerToolType]
public sealed class LeaseTools(LedgerReadClient client) {
    [McpServerTool(Name = "lease_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Reserve a resource for a session. Policy controls capacity and TTL. Omit ttlSeconds for the policy default. Heavy work queues. For a branch handoff, supply transferLeaseId and handoff {commit,nextStep,pullRequest?}; the reporter must have observed that clean, pushed commit. Save leaseId for renewal/release.")]
    public Task<LeaseReply> Acquire(string key, string holder, string machine, string purpose, int ttlSeconds = 0,
        string? transferLeaseId = null, LeaseHandoff? handoff = null, CancellationToken cancellationToken = default) =>
        client.AcquireLeaseAsync(new(key, holder, machine, purpose, ttlSeconds, transferLeaseId, handoff), cancellationToken);

    [McpServerTool(Name = "lease_renew", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Renew a reservation by exact leaseId when the holder has fresh session evidence. Policy controls TTL and benchmark budgets. Omit ttlSeconds to use the policy default.")]
    public Task<LeaseReply> Renew(string key, string leaseId, int ttlSeconds = 0, CancellationToken cancellationToken = default) =>
        client.RenewLeaseAsync(new(key, leaseId, ttlSeconds), cancellationToken);

    [McpServerTool(Name = "lease_release", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Release the reservation matching this key and leaseId. A successor's reservation is protected.")]
    public Task<LeaseReply> Release(string key, string leaseId, CancellationToken cancellationToken = default) =>
        client.ReleaseLeaseAsync(new(key, leaseId), cancellationToken);
}

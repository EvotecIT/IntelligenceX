using System.ComponentModel;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol.Server;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Explicitly write-enrolled reservations. Expiry releases ownership without a follow-up turn.</summary>
[McpServerToolType]
public sealed class LeaseTools(LedgerReadClient client) {
    [McpServerTool(Name = "lease_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Reserve one exact key for an agent. A busy answer reports its holder. TTL is 1..86400 seconds. Save the returned leaseId for renew/release.")]
    public Task<LeaseReply> Acquire(string key, string holder, string machine, string purpose, int ttlSeconds = 600, CancellationToken cancellationToken = default) =>
        client.AcquireLeaseAsync(new(key, holder, machine, purpose, ttlSeconds), cancellationToken);

    [McpServerTool(Name = "lease_renew", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Renew a still-live reservation using its exact leaseId. An expired or replaced lease cannot be renewed.")]
    public Task<LeaseReply> Renew(string key, string leaseId, int ttlSeconds = 600, CancellationToken cancellationToken = default) =>
        client.RenewLeaseAsync(new(key, leaseId, ttlSeconds), cancellationToken);

    [McpServerTool(Name = "lease_release", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Release the reservation matching this key and leaseId. A successor's reservation is protected.")]
    public Task<LeaseReply> Release(string key, string leaseId, CancellationToken cancellationToken = default) =>
        client.ReleaseLeaseAsync(new(key, leaseId), cancellationToken);
}

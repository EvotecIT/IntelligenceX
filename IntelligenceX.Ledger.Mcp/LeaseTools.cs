using System.ComponentModel;
using IntelligenceX.Ledger.Contracts;
using ModelContextProtocol.Server;

namespace IntelligenceX.Ledger.Mcp;

/// <summary>Explicitly write-enrolled reservations. Expiry releases ownership without a follow-up turn.</summary>
[McpServerToolType]
public sealed class LeaseTools(LedgerReadClient client) {
    [McpServerTool(Name = "lease_acquire", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Reserve a resource for a session. Policy controls capacity and TTL. Omit ttlSeconds for the policy default. A ttlSeconds above the key's maximum waits as needs-approval (with maximumTtlSeconds) until the operator approves or denies it; ask them, then call again with the same key, holder and machine to see acquired, queued or denied. A shorter ttlSeconds within the maximum replaces the request. Branch keys and handoffs can't wait for approval and return ttl-exceeds-policy; retry them within the maximum. Heavy work queues. For a branch handoff, supply transferLeaseId and handoff {commit,nextStep,pullRequest?}; the reporter must have observed that clean, pushed commit. Save leaseId for renewal/release.")]
    public Task<LeaseReply> Acquire(string key, string holder, string machine, string purpose, int ttlSeconds = 0,
        string? transferLeaseId = null, LeaseHandoff? handoff = null, CancellationToken cancellationToken = default) =>
        client.AcquireLeaseAsync(new(key, holder, machine, purpose, ttlSeconds, transferLeaseId, handoff), cancellationToken);

    [McpServerTool(Name = "lease_renew", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Renew a reservation by exact leaseId when the holder has fresh session evidence. Policy controls TTL and benchmark budgets. Omit ttlSeconds to use the policy default. A ttlSeconds above the policy or approved maximum returns ttl-exceeds-policy with maximumTtlSeconds.")]
    public Task<LeaseReply> Renew(string key, string leaseId, int ttlSeconds = 0, CancellationToken cancellationToken = default) =>
        client.RenewLeaseAsync(new(key, leaseId, ttlSeconds), cancellationToken);

    [McpServerTool(Name = "lease_release", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Release the reservation matching this key and leaseId. A successor's reservation is protected.")]
    public Task<LeaseReply> Release(string key, string leaseId, CancellationToken cancellationToken = default) =>
        client.ReleaseLeaseAsync(new(key, leaseId), cancellationToken);

    [McpServerTool(Name = "session_takeover", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Take over a session that stopped: stuck, at a usage limit, archived, or idle past the reservation takeover window. Its active and queued reservations move to the session named by 'by' (normally this session's own ID), and both sessions record the takeover. The reply carries what is needed to continue: its task, repository, branch, worktree, commit, PRs, children and open asks. not-eligible returns when it will be. Repeating the same takeover is safe.")]
    public Task<SessionTakeoverReply> TakeOver(string from, string by, string? reason = null, CancellationToken cancellationToken = default) =>
        client.TakeOverSessionAsync(new(from, by, reason), cancellationToken);
}

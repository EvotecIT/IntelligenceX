using System.Text.RegularExpressions;
using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Wakes: a session asks to be woken once, when a pull request's checks finish or after a delay, and ends its
/// turn. The service watches the checks without a model; the host on the session's machine delivers the wake once.</summary>
public sealed partial class CoordinationEngine {
    private static readonly Regex RepoName = new("^[A-Za-z0-9][A-Za-z0-9-]{0,38}/[A-Za-z0-9._-]{1,100}$", RegexOptions.CultureInvariant);
    private static bool WakeClosed(CoordinationWake wake) => wake.State is "delivered" or "uncertain" or "cancelled";

    /// <summary>Records a wake for a session on the caller's machine. An identical retry returns the existing wake; any
    /// other reuse of the id is rejected. A newer wake for the same session and pull request replaces a waiting one.</summary>
    public CoordinationWake Wake(CoordinationWakeRequest request, CoordinationPrincipal caller) {
        Require(caller.Role == "client" && Id(caller.MachineId) && caller.RequesterId is null, "wake-role-denied");
        var ci = request?.Repo is not null || request?.Pr is not null;
        Require(request is not null && Id(request.Id) && request.Id.Length <= 120 && Text(request.Prompt) &&
            request.SessionProvider is "codex" or "claude" && Id(request.SessionId) &&
            (request.SessionAddress is null || request.SessionAddress.Length is > 0 and <= 300 && !request.SessionAddress.Any(char.IsControl)) &&
            (ci ? request.Repo is { } repo && RepoName.IsMatch(repo) && request.Pr is > 0 && request.AfterMinutes is null
                : request.AfterMinutes is { } after && double.IsFinite(after) && after > 0 && after <= 7 * 24 * 60), "invalid-wake");
        var session = request!.SessionProvider == "codex" ? request.SessionId.ToLowerInvariant() : request.SessionId;
        if (state.Wakes.TryGetValue(request.Id, out var existing)) {
            Require(existing.Owner == caller.Id && existing.SessionId == session && existing.Prompt == request.Prompt &&
                existing.Repo == request.Repo && existing.Pr == request.Pr, "wake-id-conflict");
            return existing;
        }
        Require(state.Wakes.Values.Count(w => w.Owner == caller.Id && w.State == "waiting") < state.Policy.Ask.MaximumWakes, "wake-limit-reached");
        if (ci)
            foreach (var older in state.Wakes.Values.Where(w => w.State == "waiting" && w.Owner == caller.Id && w.SessionId == session &&
                string.Equals(w.Repo, request.Repo, StringComparison.OrdinalIgnoreCase) && w.Pr == request.Pr).ToArray())
                Close(older with { Outcome = "Replaced by wake " + request.Id + "." }, "cancelled", caller.Id);
        var wake = new CoordinationWake(request.Id, caller.Id, caller.MachineId!, request.SessionProvider, session, request.SessionAddress,
            request.Prompt, request.Repo, request.Pr, now, ci ? null : now.AddMinutes(request.AfterMinutes!.Value),
            now.AddHours(state.Policy.Ask.WakeExpiryHours), Order: state.Sequence + 1);
        state.Wakes[wake.Id] = wake;
        Note(wake.Id, "wake-requested", caller.Id, 0);
        ApplyWakes();
        return state.Wakes[wake.Id];
    }

    /// <summary>Reads one wake for its owner, the host that delivers it, or an operator.</summary>
    public CoordinationWake WakeStatus(string id, CoordinationPrincipal caller) {
        Require(state.Wakes.TryGetValue(id, out var wake) && WakeVisible(wake, caller), "wake-not-found");
        return wake;
    }

    private static bool WakeVisible(CoordinationWake wake, CoordinationPrincipal caller) =>
        caller.Role == "operator" || caller.Id == wake.Owner || caller.Role == "host" && caller.MachineId == wake.MachineId;

    /// <summary>Cancels a wake that has not started delivery.</summary>
    public CoordinationWake CancelWake(string id, CoordinationPrincipal caller) {
        Require(state.Wakes.TryGetValue(id, out var wake) && (caller.Role == "operator" || caller.Id == wake.Owner), "wake-not-found");
        if (wake.State == "cancelled") return wake;
        Require(wake.State is "waiting" or "due" && wake.Delivery is null or { Stage: "held" }, "wake-delivery-started");
        return Close(wake, "cancelled", caller.Id);
    }

    /// <summary>Pull requests that waiting wakes watch, for the service's checks reader.</summary>
    public (string Repo, int Pr)[] WatchedPullRequests() => state.Wakes.Values
        .Where(w => w.State == "waiting" && w.Repo is not null && w.Pr is not null)
        .Select(w => (w.Repo!.ToLowerInvariant(), w.Pr!.Value)).Distinct().Take(32).ToArray();

    /// <summary>Makes every waiting wake on the pull request due with the checks' outcome. Called by the service once
    /// the checks on the current head have finished, or the pull request is no longer open.</summary>
    public int ChecksFinished(string repo, int pr, string outcome) {
        Require(Text(outcome), "invalid-wake-outcome");
        var due = state.Wakes.Values.Where(w => w.State == "waiting" && w.Pr == pr &&
            string.Equals(w.Repo, repo, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var wake in due) MakeDue(wake, outcome);
        return due.Length;
    }

    private void MakeDue(CoordinationWake wake, string outcome) {
        state.Wakes[wake.Id] = wake with { State = "due", Outcome = outcome, DueAtUtc = now, Order = state.Sequence + 1 };
        Note(wake.Id, "wake-due", "service", 0);
    }

    private CoordinationWake Close(CoordinationWake wake, string closed, string principal) {
        wake = wake with { State = closed, ClosedAtUtc = now };
        state.Wakes[wake.Id] = wake;
        Note(wake.Id, "wake-" + closed, principal, 0);
        return wake;
    }

    /// <summary>Timed wakes fall due, wakes past their expiry are delivered as expired, a delivery left sending past the
    /// policy's wait becomes uncertain (never resent), and closed wakes are dropped after the retention period.</summary>
    internal void ApplyWakes() {
        var unconfirmed = TimeSpan.FromMinutes(state.Policy.Ask.DeliveryUnconfirmedMinutes);
        var retain = TimeSpan.FromDays(state.Policy.RetainFinishedDays);
        foreach (var wake in state.Wakes.Values.ToArray()) {
            if (wake.State == "waiting" && wake.Repo is null && wake.DueAtUtc is { } at && at <= now)
                MakeDue(wake, "The " + Minutes(at - wake.CreatedAtUtc) + " you asked to wait have passed.");
            else if (wake.State == "waiting" && wake.ExpiresAtUtc <= now)
                MakeDue(wake, "The checks on " + wake.Repo + "#" + wake.Pr + " did not finish within " +
                    Minutes(wake.ExpiresAtUtc - wake.CreatedAtUtc) + "; this wake expired.");
            else if (wake.State == "due" && wake.Delivery is { Stage: "sending", AtUtc: { } since } && now - since > unconfirmed)
                Close(wake with { Delivery = wake.Delivery with { Stage = "uncertain", EvidenceRef = "delivery-unconfirmed" } }, "uncertain", "service");
            else if (WakeClosed(wake) && wake.ClosedAtUtc is { } closed && now - closed > retain)
                state.Wakes.Remove(wake.Id);
        }
    }

    private static string Minutes(TimeSpan span) => span.TotalMinutes < 120
        ? Math.Round(span.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture) + " minutes"
        : Math.Round(span.TotalHours, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " hours";

    /// <summary>Due wakes the calling host has to deliver: those for sessions on its machine, not yet sending.</summary>
    public CoordinationWakePage WakesToDeliver(CoordinationPrincipal caller, long afterOrder) {
        Require(caller.Role == "host", "wakes-role-denied");
        var items = state.Wakes.Values.Where(w => w.Order > afterOrder && w.State == "due" && w.MachineId == caller.MachineId &&
            w.Delivery is null or { Stage: "held" }).OrderBy(w => w.Order).Take(17).ToArray();
        return new(items.Take(16).ToArray(), items.Length > 16 ? items[15].Order : null);
    }

    /// <summary>Starts or records one wake delivery by the host on the session's machine. A sending or uncertain attempt is
    /// never restarted; only a negative observation (not-submitted) or a pre-send hold permits a new attempt id.</summary>
    public CoordinationWake RecordWakeDelivery(string id, CoordinationWakeDeliveryCommand command, CoordinationPrincipal caller) {
        Require(state.Wakes.TryGetValue(id, out var wake) && caller.Role == "host" && wake.MachineId == caller.MachineId,
            "wake-delivery-owner-conflict");
        Require(Id(command.AttemptId) && (command.EvidenceRef is null || Id(command.EvidenceRef)), "invalid-wake-delivery");
        var current = wake.Delivery;
        if (command.Stage == "sending") {
            if (current is { Stage: "sending" } && current.AttemptId == command.AttemptId && current.Host == caller.Id) return wake;
            Require(wake.State == "due" && current is null or { Stage: "held" } && current?.AttemptId != command.AttemptId,
                "delivery-needs-reconciliation");
            wake = wake with { Delivery = new(command.AttemptId, "sending", caller.Id, AtUtc: now) };
        } else if (command.Stage == "held") {
            if (current is { Stage: "held" } && current.EvidenceRef == command.EvidenceRef) return wake;
            Require(wake.State == "due" && current is null or { Stage: "held" } && command.EvidenceRef is not null, "delivery-needs-reconciliation");
            wake = wake with { Delivery = new(command.AttemptId, "held", caller.Id, command.EvidenceRef) };
        } else {
            Require(command.Stage is "submitted" or "uncertain" or "not-submitted" && command.EvidenceRef is not null, "invalid-delivery-stage");
            if (current is not null && current.AttemptId == command.AttemptId && current.Stage == command.Stage) return wake;
            Require(current is { Stage: "sending" } && current.AttemptId == command.AttemptId && current.Host == caller.Id,
                "delivery-attempt-conflict");
            if (command.Stage == "not-submitted") wake = wake with { Delivery = null };
            else return Close(wake with { Delivery = new(command.AttemptId, command.Stage, caller.Id, command.EvidenceRef) },
                command.Stage == "submitted" ? "delivered" : "uncertain", caller.Id);
        }
        state.Wakes[id] = wake;
        Note(id, "wake-" + command.Stage, caller.Id, 0, command.EvidenceRef);
        return wake;
    }
}

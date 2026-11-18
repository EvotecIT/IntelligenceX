using System.Globalization;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>
/// Lists, approves or denies reservations that asked for longer than policy allows. It runs on the Ledger host with
/// the operator credential from the coordination configuration, which ordinary sessions don't use.
/// </summary>
internal static class LeaseApprovalCommand {
    internal const string Usage =
        "       IntelligenceX.Ledger lease-approvals --config <local-json-file> [--approve <leaseId> [--minutes <n>] | --deny <leaseId>]";

    internal static bool Matches(string[] args) => args.Length >= 3 && args[0] == "lease-approvals" && args[1] == "--config";

    internal static async Task<int> RunAsync(string[] args) {
        if (!TryParse(args, out var approval)) { Console.Error.WriteLine("Usage:\n" + Usage); return 2; }
        var settings = LedgerSettings.Load(args[2]);
        if (settings.CoordinationConfigPath is null) { Console.Error.WriteLine("Lease approvals need a coordination configuration."); return 2; }
        var operatorPrincipal = CoordinationEndpoints.Load(settings.CoordinationConfigPath, settings).Principals
            .FirstOrDefault(principal => principal.Role == "operator");
        if (operatorPrincipal is null) { Console.Error.WriteLine("The coordination configuration has no operator."); return 2; }
        using var client = LedgerReadClient.ForCoordination(new("http://127.0.0.1:" + settings.Port.ToString(CultureInfo.InvariantCulture) + "/", operatorPrincipal.Token));
        Console.WriteLine(approval is null
            ? JsonSerializer.Serialize(await client.PendingLeaseApprovalsAsync(), LedgerReadJson.Default.LeaseApprovalList)
            : JsonSerializer.Serialize(await client.ApproveLeaseAsync(approval), LedgerReadJson.Default.LeaseReply));
        return 0;
    }

    internal static bool TryParse(string[] args, out LeaseApproval? approval) {
        approval = null;
        if (!Matches(args)) return false;
        var rest = args[3..];
        if (rest.Length == 0) return true;
        if (rest is ["--deny", var denied]) { approval = new(denied, false); return true; }
        if (rest is ["--approve", var approved]) { approval = new(approved, true); return true; }
        if (rest is ["--approve", var id, "--minutes", var text] &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes is > 0 and <= 7 * 24 * 60) {
            approval = new(id, true, minutes * 60);
            return true;
        }
        return false;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Cli.Ledger;

/// <summary>Thin coordination commands with an explicit credential file and mutation intent.</summary>
internal static class CoordinationRunner {
    internal static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || args[0] is "--help" or "-h") {
            Console.WriteLine("ix coordination status <project>|request <id>|goal <id>|policy --config <scoped-client.json>");
            Console.WriteLine("ix coordination submit <request.json>|checkpoint <goal-command.json>|policy-set <policy-command.json> --config <scoped-client.json> [--apply]");
            Console.WriteLine("ix coordination accept <request-id> <result-ref> --config <scoped-client.json> --apply");
            Console.WriteLine("ix coordination cancel <request-id> <command.json> --config <scoped-client.json> --apply");
            Console.WriteLine("ix coordination assigned|results|retirements [after-order] --config <scoped-client.json>");
            Console.WriteLine("ix coordination changes <cursor> --config <host-client.json>");
            Console.WriteLine("ix coordination host <observation.json>|worker <observation.json>|receipt <request-id> <stage> <receipt.json> --config <host-client.json> --apply");
            Console.WriteLine("ix coordination results|retirements|result <request-id>|retirement <request-id> --config <scoped-client.json>");
            Console.WriteLine("ix coordination result-content <request-id>|result-publish <request-id> <content.json> --config <scoped-client.json> [--apply]");
            Console.WriteLine("ix coordination reconcile <proof.json>|reauthorize <authorization.json> --config <scoped-client.json> --apply");
            Console.WriteLine("ix coordination bind <binding.json>|retire <step.json>|delivery-begin <request-id> <intent.json>|delivery-receipt <request-id> <receipt.json>|consume <request-id> <consumption.json> --config <scoped-client.json> --apply");
            Console.WriteLine("Submit previews by default. Code owns queue waiting; do not poll from repeated model turns.");
            return args.Length == 0 ? 2 : 0;
        }
        var index = Array.IndexOf(args, "--config");
        if (index < 1 || index + 1 >= args.Length) return 2;
        var apply = args.Contains("--apply", StringComparer.Ordinal);
        if (args.Length != index + 2 + (apply ? 1 : 0) || apply && args[^1] != "--apply") return 2;
        try {
            using var client = LedgerReadClient.ForCoordination(CoordinationClientSettings.Load(args[index + 1]));
            object result = args[0] switch {
                "status" when index == 2 => await client.CoordinationStatusAsync(args[1]).ConfigureAwait(false),
                "request" when index == 2 => await client.RequestAsync(args[1]).ConfigureAwait(false),
                "goal" when index == 2 => await client.GoalAsync(args[1]).ConfigureAwait(false),
                "policy" when index == 1 => await client.PolicyAsync().ConfigureAwait(false),
                "assigned" when index == 1 => await client.AssignedAsync().ConfigureAwait(false),
                "assigned" when index == 2 && long.TryParse(args[1], out var assignedOrder) && assignedOrder >= 0 => await client.AssignedAsync(afterOrder: assignedOrder).ConfigureAwait(false),
                "changes" when index == 2 && long.TryParse(args[1], out var cursor) && cursor >= 0 => await client.ChangesAsync(cursor).ConfigureAwait(false),
                "results" when index == 1 => await client.ResultsAsync().ConfigureAwait(false),
                "results" when index == 2 && long.TryParse(args[1], out var resultOrder) && resultOrder >= 0 => await client.ResultsAsync(afterOrder: resultOrder).ConfigureAwait(false),
                "retirements" when index == 1 => await client.RetirementsAsync().ConfigureAwait(false),
                "retirements" when index == 2 && long.TryParse(args[1], out var retirementOrder) && retirementOrder >= 0 => await client.RetirementsAsync(afterOrder: retirementOrder).ConfigureAwait(false),
                "result" when index == 2 => await client.ResultAsync(args[1]).ConfigureAwait(false),
                "result-content" when index == 2 => await client.ResultContentAsync(args[1]).ConfigureAwait(false),
                "result-publish" when index == 3 && apply => await client.PublishResultAsync(args[1], Read<CoordinationArtifactCommand>(args[2])).ConfigureAwait(false),
                "retirement" when index == 2 => await client.RetirementAsync(args[1]).ConfigureAwait(false),
                "submit" when index == 2 => await client.SubmitAsync(Read<CoordinationRequest>(args[1]), apply).ConfigureAwait(false),
                "checkpoint" when index == 2 && apply => await client.SetGoalAsync(Read<CoordinationGoalCommand>(args[1])).ConfigureAwait(false),
                "policy-set" when index == 2 && apply => await client.SetPolicyAsync(Read<CoordinationPolicyCommand>(args[1])).ConfigureAwait(false),
                "accept" when index == 3 && apply => await client.AcceptAsync(args[1], args[2]).ConfigureAwait(false),
                "cancel" when index == 3 && apply => await client.CancelAsync(args[1], Read<CoordinationCancellation>(args[2])).ConfigureAwait(false),
                "host" when index == 2 && apply => await client.HostAsync(Read<CoordinationHost>(args[1])).ConfigureAwait(false),
                "worker" when index == 2 && apply => await client.WorkerAsync(Read<CoordinationWorker>(args[1])).ConfigureAwait(false),
                "receipt" when index == 4 && apply => await client.ReceiptAsync(args[1], args[2], Read<ExecutionReceipt>(args[3])).ConfigureAwait(false),
                "bind" when index == 2 && apply => await client.BindSessionAsync(Read<CoordinationSessionBinding>(args[1])).ConfigureAwait(false),
                "retire" when index == 2 && apply => await client.RetireAsync(Read<CoordinationRetirementReceipt>(args[1])).ConfigureAwait(false),
                "delivery-begin" when index == 3 && apply => await client.BeginResultAsync(args[1], Read<CoordinationDeliveryIntent>(args[2])).ConfigureAwait(false),
                "delivery-receipt" when index == 3 && apply => await client.ResultReceiptAsync(args[1], Read<CoordinationDeliveryCommand>(args[2])).ConfigureAwait(false),
                "consume" when index == 3 && apply => await client.ConsumeResultAsync(args[1], Read<CoordinationConsumption>(args[2])).ConfigureAwait(false),
                "reconcile" when index == 2 && apply => await client.ReconcileAsync(Read<CoordinationReconciliation>(args[1])).ConfigureAwait(false),
                "reauthorize" when index == 2 && apply => await client.ReauthorizeAsync(Read<CoordinationReauthorization>(args[1])).ConfigureAwait(false),
                _ => throw new ArgumentException("Invalid coordination command or missing --apply.")
            };
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
            return 0;
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or
            JsonException or ArgumentException or System.Net.Http.HttpRequestException or TaskCanceledException) {
            Console.Error.WriteLine("Coordination command was not acknowledged. Retain its immutable id/body and reconcile request status before retrying.");
            return 1;
        }
    }
    private static T Read<T>(string path) {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Command exceeds its bound.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("Missing command.");
    }
}

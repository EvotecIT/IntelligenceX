using System.Text.Json.Serialization.Metadata;

namespace IntelligenceX.Ledger.Contracts;

public sealed partial class LedgerReadClient {
    /// <summary>Cancels this original requester's queued generation-zero intent with an immutable reason.</summary>
    public Task<CoordinationWork> CancelAsync(string id, CoordinationCancellation command, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/cancel", command,
            LedgerReadJson.Default.CoordinationCancellation, LedgerReadJson.Default.CoordinationWork, token);

    /// <summary>Records qualified exclusion for an expired or old-boot grant without retrying native execution.</summary>
    public Task<CoordinationWork> ReconcileAsync(CoordinationReconciliation command, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(command.RequestId) + "/reconcile", command,
            LedgerReadJson.Default.CoordinationReconciliation, LedgerReadJson.Default.CoordinationWork, token);

    /// <summary>Uses an operator principal to reauthorize exact unsent pending work after an authorized Resume.</summary>
    public Task<CoordinationWork> ReauthorizeAsync(CoordinationReauthorization command, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(command.RequestId) + "/reauthorize", command,
            LedgerReadJson.Default.CoordinationReauthorization, LedgerReadJson.Default.CoordinationWork, token);

    /// <summary>Publishes one bounded substantive result for the exact bound native assignment.</summary>
    public Task<CoordinationArtifact> PublishResultAsync(string id, CoordinationArtifactCommand command, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(id) + "/result/content", command,
            LedgerReadJson.Default.CoordinationArtifactCommand, LedgerReadJson.Default.CoordinationArtifact, token);

    /// <summary>Retrieves the actual answer for an authorized terminal request, independently of delivery acknowledgement.</summary>
    public Task<CoordinationArtifact> ResultContentAsync(string id, CancellationToken token = default) =>
        GetAsync(RequestRoute(id) + "/result/content", LedgerReadJson.Default.CoordinationArtifact, token);

    /// <summary>Reads bounded result intents visible to this requester or the enrolled destination host.</summary>
    public Task<CoordinationResultPage> ResultsAsync(CancellationToken token = default, long afterOrder = 0) =>
        GetAsync("api/coordination/v1/results?afterOrder=" + afterOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LedgerReadJson.Default.CoordinationResultPage, token);

    /// <summary>Reads outstanding cleanup obligations independently of active worker capacity.</summary>
    public Task<CoordinationRetirementPage> RetirementsAsync(CancellationToken token = default, long afterOrder = 0) =>
        GetAsync("api/coordination/v1/retirements?afterOrder=" + afterOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
            LedgerReadJson.Default.CoordinationRetirementPage, token);

    /// <summary>Reads the exact original recipient and delivery receipts for one substantive result.</summary>
    public Task<CoordinationResultOutbox> ResultAsync(string id, CancellationToken token = default) =>
        GetAsync(RequestRoute(id) + "/result", LedgerReadJson.Default.CoordinationResultOutbox, token);

    /// <summary>Reads the exact helper's retained cleanup steps and any protected-workspace hold.</summary>
    public Task<CoordinationRetirement> RetirementAsync(string id, CancellationToken token = default) =>
        GetAsync(RequestRoute(id) + "/retirement", LedgerReadJson.Default.CoordinationRetirement, token);

    /// <summary>Binds a host-observed native session and workspace before that helper executes the request.</summary>
    public Task<CoordinationSessionBinding> BindSessionAsync(CoordinationSessionBinding binding, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(binding.RequestId) + "/binding", binding,
            LedgerReadJson.Default.CoordinationSessionBinding, LedgerReadJson.Default.CoordinationSessionBinding, token);

    /// <summary>Commits the exclusive result-send intent before calling the native destination.</summary>
    public Task<CoordinationResultOutbox> BeginResultAsync(string id, CoordinationDeliveryIntent command, CancellationToken token = default) =>
        ResultCommandAsync(id, "begin", command, LedgerReadJson.Default.CoordinationDeliveryIntent, token);

    /// <summary>Records submitted, uncertain or qualified negative delivery evidence without replaying input.</summary>
    public Task<CoordinationResultOutbox> ResultReceiptAsync(string id, CoordinationDeliveryCommand command, CancellationToken token = default) =>
        ResultCommandAsync(id, "receipts", command, LedgerReadJson.Default.CoordinationDeliveryCommand, token);

    /// <summary>Records original-requester consumption during useful work, separately from transport acknowledgement.</summary>
    public Task<CoordinationResultOutbox> ConsumeResultAsync(string id, CoordinationConsumption command, CancellationToken token = default) =>
        ResultCommandAsync(id, "consume", command, LedgerReadJson.Default.CoordinationConsumption, token);

    /// <summary>Records one verified retirement step; it does not perform native archive or deletion itself.</summary>
    public Task<CoordinationRetirement> RetireAsync(CoordinationRetirementReceipt receipt, CancellationToken token = default) =>
        PostAsync("requests/" + Uri.EscapeDataString(receipt.Binding.RequestId) + "/retirement", receipt,
            LedgerReadJson.Default.CoordinationRetirementReceipt, LedgerReadJson.Default.CoordinationRetirement, token);

    private static string RequestRoute(string id) => "api/coordination/v1/requests/" + Uri.EscapeDataString(id);

    private Task<CoordinationResultOutbox> ResultCommandAsync<T>(string id, string action, T command,
        JsonTypeInfo<T> type, CancellationToken token) => PostAsync("requests/" + Uri.EscapeDataString(id) + "/result/" + action,
            command, type, LedgerReadJson.Default.CoordinationResultOutbox, token);
}

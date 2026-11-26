using System.Text.Json;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Explicit separate command enrollment. This credential never reuses an observation token.</summary>
public sealed record CoordinationClientSettings(string Endpoint, string Token, string? CertificateSha256 = null) {
    /// <inheritdoc cref="LedgerClientSettings.Fallback"/>
    public string? Fallback { get; init; }

    /// <summary>The machine a local store serves. Set only in the client file an agent writes for its local store.</summary>
    public string? MachineId { get; init; }

    public static CoordinationClientSettings Load(string path) {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Coordination client configuration exceeds its bound.");
        var value = JsonSerializer.Deserialize(File.ReadAllText(path), LedgerReadJson.Default.CoordinationClientSettings)
            ?? throw new InvalidDataException("Missing coordination client configuration.");
        value.TransportSettings().Validate();
        return value;
    }
    public LedgerClientSettings TransportSettings() => new() { Endpoint = Endpoint, ReadToken = Token, CertificateSha256 = CertificateSha256, Fallback = Fallback };
}

public sealed record CoordinationGoalCommand(CoordinationGoal Goal, long ExpectedRevision);
public sealed record CoordinationPolicyCommand(CoordinationPolicy Policy, long ExpectedRevision);
public sealed record CoordinationAcceptance(string ResultRef);
public sealed record CoordinationChanges(long NextCursor, CoordinationChange[] Changes);

/// <summary>Persisted send intent for one exact result generation, before touching the native recipient.</summary>
public sealed record CoordinationDeliveryIntent(long Generation, string AttemptId);

/// <summary>Transport reconciliation for the already persisted result-delivery attempt.</summary>
public sealed record CoordinationDeliveryCommand(long Generation, CoordinationDeliveryReceipt Receipt);

/// <summary>Substantive result consumption by its original requester, retaining the exact candidate.</summary>
public sealed record CoordinationConsumption(long Generation, string ResultRef, WorkCheckpoint Candidate);

/// <summary>One task answer spanning execution, substantive return and independent workspace retirement.</summary>
public sealed record CoordinationRequestView(CoordinationWork Work, CoordinationSessionBinding? Attempt,
    CoordinationResultOutbox? Result, CoordinationRetirement? Retirement) {
    /// <summary>Questions and notices for this request, oldest first.</summary>
    public CoordinationQuestion[] Questions { get; init; } = [];
}

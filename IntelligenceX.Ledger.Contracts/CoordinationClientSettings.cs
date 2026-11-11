using System.Text.Json;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Explicit separate command enrollment. This credential never reuses an observation token.</summary>
public sealed record CoordinationClientSettings(string Endpoint, string Token, string? CertificateSha256 = null) {
    public static CoordinationClientSettings Load(string path) {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Coordination client configuration exceeds its bound.");
        var value = JsonSerializer.Deserialize(File.ReadAllText(path), LedgerReadJson.Default.CoordinationClientSettings)
            ?? throw new InvalidDataException("Missing coordination client configuration.");
        value.TransportSettings().Validate();
        return value;
    }
    public LedgerClientSettings TransportSettings() => new() { Endpoint = Endpoint, ReadToken = Token, CertificateSha256 = CertificateSha256 };
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
    CoordinationResultOutbox? Result, CoordinationRetirement? Retirement);

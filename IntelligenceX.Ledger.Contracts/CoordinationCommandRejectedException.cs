using System.Net;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>A command rejected by the service with a bounded, validated error code.</summary>
public sealed class CoordinationCommandRejectedException : HttpRequestException {
    internal CoordinationCommandRejectedException(string errorCode)
        : base("Coordination command rejected: " + errorCode, null, HttpStatusCode.Conflict) => ErrorCode = errorCode;

    /// <summary>The service-owned code for selecting the required recovery action.</summary>
    public string ErrorCode { get; }
}

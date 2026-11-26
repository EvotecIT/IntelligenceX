using System.Net.Sockets;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>The client could not open a connection to the Ledger: the name did not resolve, the connection was refused,
/// the host could not be reached in time, or the TLS handshake or certificate pin failed. Commands use a fresh connection
/// for every request, so for a command this means the service never received it.</summary>
public sealed class LedgerUnreachableException : HttpRequestException {
    internal LedgerUnreachableException(string endpoint, string reason, string? fallback, Exception inner)
        : base(HttpRequestError.ConnectionError, "The Ledger at " + endpoint + " is unreachable (" + reason + ").", inner) {
        Endpoint = endpoint;
        Reason = reason;
        Fallback = fallback;
    }

    /// <summary>The configured service endpoint.</summary>
    public string Endpoint { get; }

    /// <summary>One of <c>name-resolution</c>, <c>connection-refused</c>, <c>host-unreachable</c>, <c>connect-timeout</c>,
    /// <c>tls</c> or <c>connection-failed</c>.</summary>
    public string Reason { get; }

    /// <summary>The configured <see cref="LedgerClientSettings.Fallback"/>, if any.</summary>
    public string? Fallback { get; }

    /// <summary>Returns the unreachable form of a transport failure that happened before a connection was open, or null
    /// when the request may have reached the service.</summary>
    internal static LedgerUnreachableException? From(HttpRequestException error, string endpoint, string? fallback = null) {
        var socket = error.InnerException as SocketException;
        var reason = error.HttpRequestError switch {
            HttpRequestError.NameResolutionError => "name-resolution",
            HttpRequestError.SecureConnectionError => "tls",
            HttpRequestError.ConnectionError => socket?.SocketErrorCode switch {
                SocketError.ConnectionRefused => "connection-refused",
                SocketError.HostDown or SocketError.HostUnreachable or SocketError.NetworkDown or SocketError.NetworkUnreachable => "host-unreachable",
                SocketError.TimedOut => "connect-timeout",
                SocketError.HostNotFound or SocketError.TryAgain or SocketError.NoData => "name-resolution",
                _ => "connection-failed"
            },
            _ => null
        };
        return reason is null ? null : new LedgerUnreachableException(endpoint, reason, fallback, error);
    }
}

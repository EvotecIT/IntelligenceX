using System;
using System.Net;

namespace IntelligenceX.OpenAI.CompatibleHttp;

/// <summary>
/// Options for connecting to an OpenAI-compatible HTTP endpoint (for example a local model server).
/// </summary>
public sealed class OpenAICompatibleHttpOptions {
    /// <summary>
    /// Base URL for the OpenAI-compatible API.
    /// Example: <c>http://127.0.0.1:11434/v1</c> or <c>http://localhost:1234/v1</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional API key to send as a Bearer token.
    /// Local providers often ignore this.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Whether to request streaming responses when supported by the provider.
    /// </summary>
    public bool Streaming { get; set; } = true;

    /// <summary>
    /// When true, allows insecure <c>http://</c> URLs (loopback only).
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>
    /// When true, allows insecure <c>http://</c> URLs for non-loopback hosts (dangerous).
    /// </summary>
    public bool AllowInsecureHttpNonLoopback { get; set; }

    internal void Validate() {
        if (string.IsNullOrWhiteSpace(BaseUrl)) {
            throw new ArgumentException("BaseUrl is required for CompatibleHttp transport.", nameof(BaseUrl));
        }

        if (!Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out var uri) || uri is null) {
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(BaseUrl));
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) {
            throw new ArgumentException("BaseUrl must use http or https.", nameof(BaseUrl));
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)) {
            if (!AllowInsecureHttp && !AllowInsecureHttpNonLoopback) {
                throw new ArgumentException(
                    "Insecure http BaseUrl is not allowed by default. Set AllowInsecureHttp=true for loopback, or AllowInsecureHttpNonLoopback=true for non-loopback.",
                    nameof(BaseUrl));
            }

            // Loopback is allowed with AllowInsecureHttp; non-loopback requires the stronger ack.
            var host = uri.DnsSafeHost;
            var isLoopback = uri.IsLoopback;
            if (!isLoopback) {
                // DNSSafeHost won't resolve, but this is a cheap allowlist for common safe-local names.
                if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
                    || string.Equals(host, "::1", StringComparison.Ordinal)
                    || IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip)) {
                    isLoopback = true;
                }
            }

            if (isLoopback) {
                if (!AllowInsecureHttp && AllowInsecureHttpNonLoopback) {
                    // Non-loopback flag implies acknowledgement, but keep the message consistent for loopback.
                }
            } else if (!AllowInsecureHttpNonLoopback) {
                throw new ArgumentException(
                    "Insecure http BaseUrl for non-loopback host requires AllowInsecureHttpNonLoopback=true.",
                    nameof(BaseUrl));
            }
        }
    }
}


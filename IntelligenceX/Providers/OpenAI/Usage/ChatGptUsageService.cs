using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>
/// Retrieves usage, limits, and credit information from ChatGPT using native auth.
/// </summary>
/// <example>
/// <code>
/// var options = new OpenAINativeOptions();
/// using var usage = new ChatGptUsageService(options);
/// var report = await usage.GetReportAsync(includeEvents: true);
/// </code>
/// </example>
public sealed class ChatGptUsageService : IDisposable {
    private readonly OpenAINativeOptions _options;
    private readonly OpenAINativeAuthManager _auth;
    private readonly ChatGptUsageClient _client = new();

    /// <summary>
    /// Creates a usage service using the provided native options.
    /// </summary>
    public ChatGptUsageService(OpenAINativeOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _auth = new OpenAINativeAuthManager(_options);
    }

    /// <summary>
    /// Fetches the current usage snapshot (rate limits, credits, plan info).
    /// </summary>
    public Task<ChatGptUsageSnapshot> GetUsageSnapshotAsync(CancellationToken cancellationToken = default) {
        return GetUsageSnapshotInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Fetches recent credit usage events (if available for the account).
    /// </summary>
    public Task<IReadOnlyList<ChatGptCreditUsageEvent>> GetCreditUsageEventsAsync(CancellationToken cancellationToken = default) {
        return GetCreditUsageEventsInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Builds a combined report from the current snapshot and optional credit events.
    /// </summary>
    public async Task<ChatGptUsageReport> GetReportAsync(bool includeEvents, CancellationToken cancellationToken = default) {
        var snapshot = await GetUsageSnapshotInternalAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ChatGptCreditUsageEvent> events = Array.Empty<ChatGptCreditUsageEvent>();
        if (includeEvents) {
            events = await GetCreditUsageEventsInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        return new ChatGptUsageReport(snapshot, events);
    }

    private async Task<ChatGptUsageSnapshot> GetUsageSnapshotInternalAsync(CancellationToken cancellationToken) {
        var bundle = await EnsureAuthAsync(cancellationToken).ConfigureAwait(false);
        var accountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        return await _client.GetUsageAsync(_options.ChatGptApiBaseUrl, bundle.AccessToken, accountId, _options.UserAgent, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ChatGptCreditUsageEvent>> GetCreditUsageEventsInternalAsync(CancellationToken cancellationToken) {
        var bundle = await EnsureAuthAsync(cancellationToken).ConfigureAwait(false);
        var accountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        return await _client.GetCreditUsageEventsAsync(_options.ChatGptApiBaseUrl, bundle.AccessToken, accountId, _options.UserAgent,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<AuthBundle> EnsureAuthAsync(CancellationToken cancellationToken) {
        var bundle = await _auth.TryGetValidBundleAsync(cancellationToken).ConfigureAwait(false);
        if (bundle is not null) {
            return bundle;
        }
        throw new InvalidOperationException("Not logged in. Run ChatGPT login first.");
    }

    public void Dispose() {
        _client.Dispose();
    }
}

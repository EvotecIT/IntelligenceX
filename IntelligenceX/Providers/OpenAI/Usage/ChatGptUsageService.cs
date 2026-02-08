using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>
/// Retrieves ChatGPT usage, rate limits, and credit usage events.
/// </summary>
public sealed class ChatGptUsageService : IDisposable {
    private readonly OpenAINativeOptions _options;
    private readonly OpenAINativeAuthManager _auth;
    private readonly ChatGptUsageClient _client = new();

    /// <summary>
    /// Initializes a new usage service using native auth options.
    /// </summary>
    /// <param name="options">Native options used for authentication and endpoints.</param>
    public ChatGptUsageService(OpenAINativeOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _auth = new OpenAINativeAuthManager(_options);
    }

    /// <summary>
    /// Retrieves the current usage snapshot.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ChatGptUsageSnapshot> GetUsageSnapshotAsync(CancellationToken cancellationToken = default) {
        return GetUsageSnapshotInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves recent credit usage events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<ChatGptCreditUsageEvent>> GetCreditUsageEventsAsync(CancellationToken cancellationToken = default) {
        return GetCreditUsageEventsInternalAsync(cancellationToken);
    }

    /// <summary>
    /// Retrieves a combined usage report.
    /// </summary>
    /// <param name="includeEvents">Whether to include credit usage events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
        throw new OpenAIAuthenticationRequiredException(OpenAIAuthenticationRequiredException.DefaultMessage);
    }

    /// <summary>
    /// Disposes the underlying usage client.
    /// </summary>
    public void Dispose() {
        _client.Dispose();
    }
}

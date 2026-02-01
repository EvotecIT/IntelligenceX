using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;

namespace IntelligenceX.OpenAI.Usage;

public sealed class ChatGptUsageService : IDisposable {
    private readonly OpenAINativeOptions _options;
    private readonly OpenAINativeAuthManager _auth;
    private readonly ChatGptUsageClient _client = new();

    public ChatGptUsageService(OpenAINativeOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _auth = new OpenAINativeAuthManager(_options);
    }

    public Task<ChatGptUsageSnapshot> GetUsageSnapshotAsync(CancellationToken cancellationToken = default) {
        return GetUsageSnapshotInternalAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ChatGptCreditUsageEvent>> GetCreditUsageEventsAsync(CancellationToken cancellationToken = default) {
        return GetCreditUsageEventsInternalAsync(cancellationToken);
    }

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

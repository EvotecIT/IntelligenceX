using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.OpenAI.Usage;

public sealed partial class ChatGptUsageService {
    /// <summary>Retrieves provider account-wide profile, daily analytics and plan history.
    /// Endpoint failures are independent and do not imply zero usage.</summary>
    public async Task<ChatGptAccountAnalytics> GetAccountAnalyticsAsync(CancellationToken cancellationToken = default) {
        var bundle = await EnsureAuthAsync(cancellationToken).ConfigureAwait(false);
        var accountId = bundle.AccountId ?? IntelligenceX.OpenAI.Auth.JwtDecoder.TryGetAccountId(bundle.AccessToken);
        return await _client.GetAccountAnalyticsAsync(_options.ChatGptApiBaseUrl, bundle.AccessToken, accountId,
            _options.UserAgent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Queries provider lifetime consumption for explicit root/descendant groups.
    /// Accepts up to 100 roots and 1,000 non-overlapping IDs. This read-only POST does not start a model turn.</summary>
    public async Task<ChatGptThreadUsage> QueryThreadUsageAsync(IReadOnlyList<ChatGptThreadUsageRequest> threads,
        CancellationToken cancellationToken = default) {
        var query = ChatGptThreadUsageRequest.BuildQuery(threads);
        var bundle = await EnsureAuthAsync(cancellationToken).ConfigureAwait(false);
        var accountId = bundle.AccountId ?? IntelligenceX.OpenAI.Auth.JwtDecoder.TryGetAccountId(bundle.AccessToken);
        return await _client.QueryThreadUsageAsync(_options.ChatGptApiBaseUrl, bundle.AccessToken, accountId,
            _options.UserAgent, query, cancellationToken).ConfigureAwait(false);
    }
}

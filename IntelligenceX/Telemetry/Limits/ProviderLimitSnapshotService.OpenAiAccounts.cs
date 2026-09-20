using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Limits;

public sealed partial class ProviderLimitSnapshotService {
    private static readonly TimeSpan AccountLimitTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Discovers saved accounts before making network requests. Each account retains its own
    /// result, including failures, and a background refresh never exports a different Codex login.
    /// </summary>
    internal static async Task<ProviderLimitSnapshot> FetchCodexAsync(
        string requestedProviderId,
        OpenAINativeOptions options,
        CancellationToken cancellationToken) {
        options.PersistCodexAuthJson = true;
        options.PreserveCodexLoginOnRefresh = true;
        var bundles = await ListOpenAiBundlesAsync(options.AuthStore, cancellationToken).ConfigureAwait(false);
        if (bundles.Count == 0) {
            return BuildUnavailableSnapshot(requestedProviderId,
                "No saved IX accounts found. Sign in through IX Chat to add an account for live limits.",
                "OpenAI usage API");
        }

        var accounts = new List<ProviderLimitAccountSnapshot>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var usageService = new ChatGptUsageService(options);
        // Keep refreshes sequential: the file auth store writes the whole collection when a
        // token is renewed. Prefer the newest saved credential when provider aliases overlap.
        foreach (var bundle in bundles.OrderByDescending(static value => value.ExpiresAt ?? DateTimeOffset.MinValue)) {
            cancellationToken.ThrowIfCancellationRequested();
            var accountId = NormalizeOptional(bundle.AccountId) ?? NormalizeOptional(JwtDecoder.TryGetAccountId(bundle.AccessToken));
            var email = (bundle.IdToken is null ? null : NormalizeOptional(JwtDecoder.TryGetEmail(bundle.IdToken)))
                        ?? NormalizeOptional(JwtDecoder.TryGetEmail(bundle.AccessToken));
            if (!seenKeys.Add(BuildOpenAiAccountKey(accountId, email, bundle.AccessToken))) {
                continue;
            }

            var isSelected = accountId is not null && string.Equals(accountId, options.AuthAccountId, StringComparison.OrdinalIgnoreCase);
            using var accountCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            accountCancellation.CancelAfter(AccountLimitTimeout);
            try {
                var snapshot = await usageService.GetUsageSnapshotAsync(bundle, accountCancellation.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var returnedAccountId = NormalizeOptional(snapshot.AccountId);
                if (accountId is not null && returnedAccountId is not null
                    && !string.Equals(accountId, returnedAccountId, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("Usage response account did not match the requested account.");
                }

                var presentation = BuildOpenAiLimitPresentation(snapshot);
                accounts.Add(new ProviderLimitAccountSnapshot(
                    returnedAccountId ?? accountId,
                    NormalizeOptional(snapshot.Email) ?? email ?? returnedAccountId ?? accountId,
                    presentation.PlanLabel, presentation.Windows, presentation.Summary, presentation.DetailMessage,
                    DateTimeOffset.UtcNow, isSelected));
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                // Never surface raw OAuth/API response bodies in the account card.
                var detail = ex is OperationCanceledException
                    ? "The account-limit request timed out. Retry to check this account."
                    : bundle.ExpiresAt.HasValue && bundle.ExpiresAt.Value <= DateTimeOffset.UtcNow
                        ? "The saved login could not be refreshed. Retry, or sign in to this account again in IX Chat."
                        : "Live limits could not be read. Retry, or check this account's sign-in in IX Chat.";
                accounts.Add(new ProviderLimitAccountSnapshot(accountId, email ?? accountId, null,
                    Array.Empty<ProviderLimitWindow>(), "Saved account · live limits unavailable", detail,
                    DateTimeOffset.UtcNow, isSelected));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Top-level windows remain those of the current account when it is known; do not
        // silently present a healthy alternative as the user's current login.
        var primary = accounts.FirstOrDefault(static account => account.IsSelected)
                      ?? accounts.FirstOrDefault(static account => account.IsAvailable)
                      ?? accounts.First();
        var availableCount = accounts.Count(static account => account.IsAvailable);
        var detailMessage = availableCount == accounts.Count
            ? null
            : "Live limit windows available for " + availableCount.ToString(CultureInfo.InvariantCulture)
              + " of " + accounts.Count.ToString(CultureInfo.InvariantCulture)
              + " saved accounts. See each account below for details.";
        return new ProviderLimitSnapshot(requestedProviderId, UsageTelemetryProviderCatalog.ResolveDisplayTitle("codex"),
            "OpenAI usage API", primary.PlanLabel, primary.AccountLabel, primary.Windows, primary.Summary,
            detailMessage, DateTimeOffset.UtcNow, accounts);
    }

    private static async Task<IReadOnlyList<AuthBundle>> ListOpenAiBundlesAsync(IAuthBundleStore authStore, CancellationToken cancellationToken) {
        var bundles = new List<AuthBundle>();
        foreach (var provider in new[] { OpenAICodexDefaults.Provider, "openai", "chatgpt" }) {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = await authStore.ListAsync(provider, cancellationToken).ConfigureAwait(false);
            bundles.AddRange(entries);
        }
        return bundles;
    }
}

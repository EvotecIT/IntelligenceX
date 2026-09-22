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
    private static readonly TimeSpan AccountScanTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Discovers saved accounts before making network requests. Each account retains its own
    /// result, including failures, and a background refresh never exports a different Codex login.
    /// </summary>
    internal static async Task<ProviderLimitSnapshot> FetchCodexAsync(
        string requestedProviderId,
        OpenAINativeOptions options,
        CancellationToken cancellationToken,
        TimeSpan? scanTimeout = null) {
        options.PreserveCodexLoginOnRefresh = true;
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        scanCancellation.CancelAfter(scanTimeout ?? AccountScanTimeout);
        IReadOnlyList<AuthBundle> bundles;
        try {
            bundles = await ListOpenAiBundlesAsync(options.AuthStore, scanCancellation.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && scanCancellation.IsCancellationRequested) {
            return BuildUnavailableSnapshot(requestedProviderId,
                "Saved-account discovery reached its time limit. Retry to check live limits.", "OpenAI usage API");
        }
        if (bundles.Count == 0) {
            return BuildUnavailableSnapshot(requestedProviderId,
                "No saved IX accounts found. Sign in through IX Chat to add an account for live limits.",
                "OpenAI usage API");
        }

        var uniqueBundles = new List<(AuthBundle Bundle, string? AccountId, string? Email)>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Prefer the newest saved credential when provider aliases overlap.
        foreach (var bundle in bundles.OrderByDescending(static value => value.ExpiresAt ?? DateTimeOffset.MinValue)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (scanCancellation.IsCancellationRequested) {
                return BuildUnavailableSnapshot(requestedProviderId,
                    "Saved-account discovery reached its time limit. Retry to check live limits.", "OpenAI usage API");
            }
            var accountId = NormalizeOptional(bundle.AccountId) ?? NormalizeOptional(JwtDecoder.TryGetAccountId(bundle.AccessToken));
            var email = (bundle.IdToken is null ? null : NormalizeOptional(JwtDecoder.TryGetEmail(bundle.IdToken)))
                        ?? NormalizeOptional(JwtDecoder.TryGetEmail(bundle.AccessToken));
            if (!seenKeys.Add(BuildOpenAiAccountKey(accountId, email, bundle.AccessToken))) {
                continue;
            }
            uniqueBundles.Add((bundle, accountId, email));
        }

        // Bound the whole inventory, including requests queued behind the semaphore.
        // Tray starts another automatic refresh after two minutes.
        using var concurrency = new SemaphoreSlim(3, 3);
        using var usageService = new ChatGptUsageService(options);
        var accounts = await Task.WhenAll(uniqueBundles.Select(async item => {
            var acquired = false;
            try {
                await concurrency.WaitAsync(scanCancellation.Token).ConfigureAwait(false);
                acquired = true;
                return await FetchAccountAsync(item.Bundle, item.AccountId, item.Email).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && scanCancellation.IsCancellationRequested) {
                return UnavailableAccount(item.AccountId, item.Email, "The account scan reached its time limit. Retry to check this account.");
            } finally {
                if (acquired) concurrency.Release();
            }
        })).ConfigureAwait(false);

        async Task<ProviderLimitAccountSnapshot> FetchAccountAsync(AuthBundle bundle, string? accountId, string? email) {
            var isSelected = accountId is not null && string.Equals(accountId, options.AuthAccountId, StringComparison.OrdinalIgnoreCase);
            using var accountCancellation = CancellationTokenSource.CreateLinkedTokenSource(scanCancellation.Token);
            accountCancellation.CancelAfter(AccountLimitTimeout);

            try {
                var snapshot = await usageService.GetUsageSnapshotAsync(bundle, accountCancellation.Token).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var returnedAccountId = NormalizeOptional(snapshot.AccountId);
                if (accountId is not null && returnedAccountId is not null
                    && !string.Equals(accountId, returnedAccountId, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("Usage response account did not match the requested account.");
                }

                var resolvedAccountId = returnedAccountId ?? accountId;
                isSelected = resolvedAccountId is not null
                    && string.Equals(resolvedAccountId, options.AuthAccountId, StringComparison.OrdinalIgnoreCase);
                var presentation = BuildOpenAiLimitPresentation(snapshot);
                return new ProviderLimitAccountSnapshot(
                    resolvedAccountId,
                    NormalizeOptional(snapshot.Email) ?? email ?? returnedAccountId ?? accountId,
                    presentation.PlanLabel, presentation.Windows, presentation.Summary, presentation.DetailMessage,
                    DateTimeOffset.UtcNow, isSelected);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception ex) {
                // Never surface raw OAuth/API response bodies in the account card.
                var detail = ex is OperationCanceledException
                    ? "The account-limit request timed out. Retry to check this account."
                    : bundle.ExpiresAt.HasValue && bundle.ExpiresAt.Value <= DateTimeOffset.UtcNow
                        ? "The saved login could not be refreshed. Retry, or sign in to this account again in IX Chat."
                        : "Live limits could not be read. Retry, or check this account's sign-in in IX Chat.";
                return UnavailableAccount(accountId, email, detail);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var distinctAccounts = CoalesceResolvedAccounts(accounts, options.AuthAccountId);
        // Top-level windows remain those of the current account when it is known; do not
        // silently present a healthy alternative as the user's current login.
        var primary = distinctAccounts.FirstOrDefault(static account => account.IsSelected)
                      ?? distinctAccounts.FirstOrDefault(static account => account.IsAvailable)
                      ?? distinctAccounts[0];
        var availableCount = distinctAccounts.Count(static account => account.IsAvailable);
        var detailMessage = availableCount == distinctAccounts.Count
            ? null
            : "Live limit windows available for " + availableCount.ToString(CultureInfo.InvariantCulture)
              + " of " + distinctAccounts.Count.ToString(CultureInfo.InvariantCulture)
              + " saved accounts. See each account below for details.";
        return new ProviderLimitSnapshot(requestedProviderId, UsageTelemetryProviderCatalog.ResolveDisplayTitle("codex"),
            "OpenAI usage API", primary.PlanLabel, primary.AccountLabel, primary.Windows, primary.Summary,
            detailMessage, DateTimeOffset.UtcNow, distinctAccounts);
    }

    private static ProviderLimitAccountSnapshot UnavailableAccount(string? accountId, string? email, string detail) =>
        new(accountId, email ?? accountId, null, Array.Empty<ProviderLimitWindow>(),
            "Saved account · live limits unavailable", detail, DateTimeOffset.UtcNow);

    internal static IReadOnlyList<ProviderLimitAccountSnapshot> CoalesceResolvedAccounts(
        IEnumerable<ProviderLimitAccountSnapshot> accounts, string? selectedAccountId) {
        var rows = accounts.ToArray();
        var result = new List<ProviderLimitAccountSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) {
            var id = NormalizeOptional(row.AccountId);
            // Only provider-confirmed identity is safe to merge: two unresolved
            // credentials with the same display label need not be the same account.
            if (id is null) {
                result.Add(row);
                continue;
            }
            if (!seen.Add(id)) continue;
            var duplicates = rows.Where(other => string.Equals(other.AccountId, id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var preferred = duplicates.Where(static other => other.IsAvailable)
                .OrderByDescending(static other => other.RetrievedAtUtc).FirstOrDefault() ?? duplicates[0];
            var selected = duplicates.Any(static other => other.IsSelected)
                || string.Equals(id, selectedAccountId, StringComparison.OrdinalIgnoreCase);
            result.Add(new ProviderLimitAccountSnapshot(id, preferred.AccountLabel, preferred.PlanLabel,
                preferred.Windows, preferred.Summary, preferred.DetailMessage, preferred.RetrievedAtUtc, selected));
        }
        return result;
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

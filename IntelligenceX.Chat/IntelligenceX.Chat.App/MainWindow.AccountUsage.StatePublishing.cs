using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private object[] BuildAccountUsageState() {
        lock (_turnDiagnosticsSync) {
            if (_accountUsageByKey.Count == 0) {
                return Array.Empty<object>();
            }

            var nowUtc = DateTime.UtcNow;
            var entries = new List<AccountUsageSnapshot>(_accountUsageByKey.Values);
            entries.Sort((left, right) => Nullable.Compare(right.LastSeenUtc, left.LastSeenUtc));
            if (entries.Count > MaxTrackedAccounts) {
                entries.RemoveRange(MaxTrackedAccounts, entries.Count - MaxTrackedAccounts);
            }

            var result = new object[entries.Count];
            for (var i = 0; i < entries.Count; i++) {
                var item = entries[i];
                result[i] = BuildAccountUsageStateEntry(item, nowUtc);
            }

            return result;
        }
    }

    private object? BuildActiveAccountUsageState() {
        var identity = ResolveActiveUsageIdentity();
        lock (_turnDiagnosticsSync) {
            if (!_accountUsageByKey.TryGetValue(identity.Key, out var snapshot)) {
                if (string.Equals(_localProviderTransport, TransportNative, StringComparison.OrdinalIgnoreCase)) {
                    var selectedAccountId = NormalizeLocalProviderOpenAIAccountId(_localProviderOpenAIAccountId);
                    if (selectedAccountId.Length > 0) {
                        _accountUsageByKey.TryGetValue(BuildNativeUsageKey(selectedAccountId), out snapshot);
                    }

                    if (snapshot is null) {
                        var authenticatedAccountId = NormalizeLocalProviderOpenAIAccountId(_authenticatedAccountId);
                        if (authenticatedAccountId.Length > 0) {
                            _accountUsageByKey.TryGetValue(BuildNativeUsageKey(authenticatedAccountId), out snapshot);
                        }
                    }
                }

                if (snapshot is null) {
                    return null;
                }
            }

            return BuildAccountUsageStateEntry(snapshot, DateTime.UtcNow);
        }
    }

    private static object BuildAccountUsageStateEntry(AccountUsageSnapshot snapshot, DateTime nowUtc) {
        var retryAfterUtc = snapshot.UsageLimitRetryAfterUtc;
        int? retryAfterMinutes = null;
        if (retryAfterUtc.HasValue) {
            var remaining = retryAfterUtc.Value - nowUtc;
            retryAfterMinutes = remaining > TimeSpan.Zero
                ? (int)Math.Ceiling(remaining.TotalMinutes)
                : 0;
        }

        var rateLimitWindowResetUtc = snapshot.RateLimitWindowResetUtc;
        int? rateLimitWindowResetMinutes = null;
        if (rateLimitWindowResetUtc.HasValue) {
            var remaining = rateLimitWindowResetUtc.Value - nowUtc;
            rateLimitWindowResetMinutes = remaining > TimeSpan.Zero
                ? (int)Math.Ceiling(remaining.TotalMinutes)
                : 0;
        }

        return new {
            key = snapshot.Key,
            label = snapshot.Label,
            promptTokens = snapshot.PromptTokens,
            completionTokens = snapshot.CompletionTokens,
            totalTokens = snapshot.TotalTokens,
            cachedPromptTokens = snapshot.CachedPromptTokens,
            reasoningTokens = snapshot.ReasoningTokens,
            turns = snapshot.Turns,
            lastSeenUtc = snapshot.LastSeenUtc?.ToString("O", CultureInfo.InvariantCulture),
            usageLimitHitUtc = snapshot.UsageLimitHitUtc?.ToString("O", CultureInfo.InvariantCulture),
            usageLimitRetryAfterUtc = retryAfterUtc?.ToString("O", CultureInfo.InvariantCulture),
            retryAfterMinutes,
            planType = snapshot.PlanType ?? string.Empty,
            email = snapshot.Email ?? string.Empty,
            rateLimitAllowed = snapshot.RateLimitAllowed,
            rateLimitReached = snapshot.RateLimitReached,
            rateLimitUsedPercent = snapshot.RateLimitUsedPercent,
            rateLimitWindowResetUtc = rateLimitWindowResetUtc?.ToString("O", CultureInfo.InvariantCulture),
            rateLimitWindowResetMinutes,
            usageSnapshotRetrievedAtUtc = snapshot.UsageSnapshotRetrievedAtUtc?.ToString("O", CultureInfo.InvariantCulture),
            usageSnapshotSource = snapshot.UsageSnapshotSource ?? string.Empty,
            creditsHasCredits = snapshot.CreditsHasCredits,
            creditsUnlimited = snapshot.CreditsUnlimited,
            creditsBalance = snapshot.CreditsBalance
        };
    }
}

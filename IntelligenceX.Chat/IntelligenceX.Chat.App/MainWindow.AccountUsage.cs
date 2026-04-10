using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using IntelligenceX.Chat.Abstractions.Protocol;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const int MaxTrackedAccounts = 12;
    private const string UnknownNativeUsageKey = "native:unknown";
    private static readonly TimeSpan UsageLimitDispatchCooldown = TimeSpan.FromMinutes(2);
    private static readonly Regex UsageRetryAfterMinutesRegex = new(
        @"(?:in\s+about|about)\s+(?<minutes>\d+)\s+minute",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private sealed record AccountUsageSnapshot(
        string Key,
        string Label,
        long PromptTokens,
        long CompletionTokens,
        long TotalTokens,
        long CachedPromptTokens,
        long ReasoningTokens,
        int Turns,
        DateTime? LastSeenUtc,
        DateTime? UsageLimitHitUtc,
        DateTime? UsageLimitRetryAfterUtc,
        string? PlanType,
        string? Email,
        bool? RateLimitAllowed,
        bool? RateLimitReached,
        double? RateLimitUsedPercent,
        DateTime? RateLimitWindowResetUtc,
        DateTime? UsageSnapshotRetrievedAtUtc,
        string? UsageSnapshotSource,
        bool? CreditsHasCredits,
        bool? CreditsUnlimited,
        double? CreditsBalance);

    private sealed record ActiveUsageIdentity(string Key, string Label);

    private ActiveUsageIdentity ResolveActiveUsageIdentity() {
        if (string.Equals(_localProviderTransport, TransportNative, StringComparison.OrdinalIgnoreCase)) {
            var accountId = (_authenticatedAccountId ?? string.Empty).Trim();
            return ResolveNativeUsageIdentity(accountId);
        }

        if (string.Equals(_localProviderTransport, TransportCopilotCli, StringComparison.OrdinalIgnoreCase)) {
            return new ActiveUsageIdentity("copilot-cli", "GitHub Copilot Subscription");
        }

        var baseUrl = (_localProviderBaseUrl ?? string.Empty).Trim();
        var compatibleIdentity = BuildCompatibleUsageIdentity(
            baseUrl,
            _localProviderOpenAIAuthMode,
            _localProviderOpenAIAccountId,
            _localProviderOpenAIBasicUsername);
        return new ActiveUsageIdentity(compatibleIdentity.Key, compatibleIdentity.Label);
    }

    internal static (string Key, string Label) BuildCompatibleUsageIdentity(
        string? baseUrl,
        string? openAIAuthMode,
        string? openAIAccountId,
        string? openAIBasicUsername) {
        var normalizedBaseUrl = (baseUrl ?? string.Empty).Trim();
        var canonicalBaseUrl = CanonicalizeCompatibleUsageBaseUrl(normalizedBaseUrl);
        var normalizedAccountId = (openAIAccountId ?? string.Empty).Trim();
        var normalizedAuthMode = (openAIAuthMode ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedBasicUsername = (openAIBasicUsername ?? string.Empty).Trim();

        string compatibleAccountIdentity;
        if (normalizedAccountId.Length > 0) {
            compatibleAccountIdentity = normalizedAccountId;
        } else if (string.Equals(normalizedAuthMode, "basic", StringComparison.OrdinalIgnoreCase)
                   && normalizedBasicUsername.Length > 0) {
            compatibleAccountIdentity = normalizedBasicUsername;
        } else {
            compatibleAccountIdentity = string.Empty;
        }

        if (canonicalBaseUrl.Length == 0) {
            if (compatibleAccountIdentity.Length == 0) {
                return ("compatible-http:unknown", "Compatible HTTP (unconfigured)");
            }

            var encodedAccountIdentity = EncodeCompatibleUsageKeyComponent(compatibleAccountIdentity);
            return (
                "compatible-http:unknown|acct:" + encodedAccountIdentity,
                "Compatible HTTP (unconfigured | " + compatibleAccountIdentity + ")");
        }

        var encodedBaseUrl = EncodeCompatibleUsageKeyComponent(canonicalBaseUrl);
        if (compatibleAccountIdentity.Length == 0) {
            return (
                "compatible-http:" + encodedBaseUrl,
                "Compatible HTTP (" + canonicalBaseUrl + ")");
        }

        var encodedAccount = EncodeCompatibleUsageKeyComponent(compatibleAccountIdentity);
        return (
            "compatible-http:" + encodedBaseUrl + "|acct:" + encodedAccount,
            "Compatible HTTP (" + canonicalBaseUrl + " | " + compatibleAccountIdentity + ")");
    }

    private static string EncodeCompatibleUsageKeyComponent(string value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : Uri.EscapeDataString(normalized);
    }

    private static string CanonicalizeCompatibleUsageBaseUrl(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed) || parsed is null) {
            return normalized;
        }

        var path = parsed.AbsolutePath.Length == 0 ? "/" : parsed.AbsolutePath;
        if (path.Length > 1) {
            path = path.TrimEnd('/');
            if (path.Length == 0) {
                path = "/";
            }
        }

        var defaultPort = parsed.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? 80
            : (parsed.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : -1);
        var includePort = parsed.Port > 0 && parsed.Port != defaultPort;
        var host = parsed.IdnHost.Length > 0 ? parsed.IdnHost : parsed.Host;

        var canonical = parsed.Scheme.ToLowerInvariant()
                        + "://"
                        + host.ToLowerInvariant()
                        + (includePort ? ":" + parsed.Port.ToString(CultureInfo.InvariantCulture) : string.Empty)
                        + path;
        if (!string.IsNullOrEmpty(parsed.Query)) {
            canonical += parsed.Query;
        }

        return canonical;
    }

    private static ActiveUsageIdentity ResolveNativeUsageIdentity(string? accountId, string? email = null) {
        var normalized = (accountId ?? string.Empty).Trim();
        var normalizedEmail = (email ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return normalizedEmail.Length == 0
                ? new ActiveUsageIdentity("native:unknown", "ChatGPT (unknown account)")
                : new ActiveUsageIdentity("native:unknown", "ChatGPT (" + normalizedEmail + ")");
        }

        var label = normalizedEmail.Length == 0
            ? "ChatGPT (" + normalized + ")"
            : "ChatGPT (" + normalizedEmail + " | " + normalized + ")";
        return new ActiveUsageIdentity(BuildNativeUsageKey(normalized), label);
    }

    private static AccountUsageSnapshot CreateEmptyUsageSnapshot(ActiveUsageIdentity identity) {
        return new AccountUsageSnapshot(
            Key: identity.Key,
            Label: identity.Label,
            PromptTokens: 0,
            CompletionTokens: 0,
            TotalTokens: 0,
            CachedPromptTokens: 0,
            ReasoningTokens: 0,
            Turns: 0,
            LastSeenUtc: null,
            UsageLimitHitUtc: null,
            UsageLimitRetryAfterUtc: null,
            PlanType: null,
            Email: null,
            RateLimitAllowed: null,
            RateLimitReached: null,
            RateLimitUsedPercent: null,
            RateLimitWindowResetUtc: null,
            UsageSnapshotRetrievedAtUtc: null,
            UsageSnapshotSource: null,
            CreditsHasCredits: null,
            CreditsUnlimited: null,
            CreditsBalance: null);
    }

    private static long SafeUsageValue(long? value) {
        return value.HasValue && value.Value > 0 ? value.Value : 0L;
    }

    private static int? TryExtractUsageRetryAfterMinutes(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        var match = UsageRetryAfterMinutesRegex.Match(normalized);
        if (!match.Success) {
            return null;
        }

        return int.TryParse(match.Groups["minutes"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0
            ? minutes
            : null;
    }

    private static string? NormalizeOptionalText(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static double? NormalizeRateLimitPercent(double? value) {
        if (!value.HasValue) {
            return null;
        }

        var normalized = value.Value;
        if (double.IsNaN(normalized) || double.IsInfinity(normalized)) {
            return null;
        }

        if (normalized < 0d) {
            normalized = 0d;
        }
        if (normalized > 100d) {
            normalized = 100d;
        }

        return normalized;
    }

    private static DateTime? TryResolveRateLimitWindowResetUtc(NativeRateLimitWindowDto? window, DateTime nowUtc) {
        if (window is null) {
            return null;
        }

        DateTime? fromUnix = null;
        if (window.ResetAtUnixSeconds.HasValue) {
            try {
                fromUnix = DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value).UtcDateTime;
            } catch {
                fromUnix = null;
            }
        }

        DateTime? fromAfter = null;
        if (window.ResetAfterSeconds.HasValue) {
            var seconds = window.ResetAfterSeconds.Value;
            if (seconds >= 0) {
                fromAfter = nowUtc.AddSeconds(seconds);
            }
        }

        return SelectSoonestFuture(nowUtc, fromUnix, fromAfter);
    }

    private static DateTime? TryResolveRateLimitResetUtc(NativeRateLimitStatusDto? rateLimit, DateTime nowUtc) {
        if (rateLimit is null) {
            return null;
        }

        var primary = TryResolveRateLimitWindowResetUtc(rateLimit.Primary, nowUtc);
        var secondary = TryResolveRateLimitWindowResetUtc(rateLimit.Secondary, nowUtc);
        return SelectSoonestFuture(nowUtc, primary, secondary);
    }

    private static DateTime? SelectSoonestFuture(DateTime nowUtc, DateTime? firstUtc, DateTime? secondUtc) {
        var first = firstUtc.HasValue ? EnsureUtc(firstUtc.Value) : (DateTime?)null;
        var second = secondUtc.HasValue ? EnsureUtc(secondUtc.Value) : (DateTime?)null;

        var firstFuture = first.HasValue && first.Value >= nowUtc ? first : null;
        var secondFuture = second.HasValue && second.Value >= nowUtc ? second : null;

        if (!firstFuture.HasValue) {
            return secondFuture;
        }

        if (!secondFuture.HasValue) {
            return firstFuture;
        }

        return firstFuture.Value <= secondFuture.Value ? firstFuture : secondFuture;
    }

    private static double? ResolveRateLimitUsedPercent(NativeRateLimitStatusDto? rateLimit) {
        var primary = NormalizeRateLimitPercent(rateLimit?.Primary?.UsedPercent);
        var secondary = NormalizeRateLimitPercent(rateLimit?.Secondary?.UsedPercent);
        if (!primary.HasValue) {
            return secondary;
        }

        if (!secondary.HasValue) {
            return primary;
        }

        return primary.Value >= secondary.Value ? primary : secondary;
    }

    private void UpdateAccountUsageFromMetrics(TokenUsageDto? usage) {
        if (usage is null) {
            return;
        }

        var promptTokens = SafeUsageValue(usage.PromptTokens);
        var completionTokens = SafeUsageValue(usage.CompletionTokens);
        var totalTokens = SafeUsageValue(usage.TotalTokens);
        if (totalTokens == 0 && (promptTokens > 0 || completionTokens > 0)) {
            totalTokens = promptTokens + completionTokens;
        }

        var cachedPromptTokens = SafeUsageValue(usage.CachedPromptTokens);
        var reasoningTokens = SafeUsageValue(usage.ReasoningTokens);
        if (promptTokens <= 0
            && completionTokens <= 0
            && totalTokens <= 0
            && cachedPromptTokens <= 0
            && reasoningTokens <= 0) {
            return;
        }

        var identity = ResolveActiveUsageIdentity();
        var nowUtc = DateTime.UtcNow;
        lock (_turnDiagnosticsSync) {
            if (!_accountUsageByKey.TryGetValue(identity.Key, out var current)) {
                current = CreateEmptyUsageSnapshot(identity);
            }

            _accountUsageByKey[identity.Key] = current with {
                Label = string.IsNullOrWhiteSpace(current.Label) ? identity.Label : current.Label,
                PromptTokens = checked(current.PromptTokens + promptTokens),
                CompletionTokens = checked(current.CompletionTokens + completionTokens),
                TotalTokens = checked(current.TotalTokens + totalTokens),
                CachedPromptTokens = checked(current.CachedPromptTokens + cachedPromptTokens),
                ReasoningTokens = checked(current.ReasoningTokens + reasoningTokens),
                Turns = checked(current.Turns + 1),
                LastSeenUtc = nowUtc
            };

            TrimAccountUsageCacheLocked();
            SyncAccountUsageToAppStateLocked();
        }
    }

    private void UpdateAccountUsageFromNativeLoginStatus(LoginStatusMessage login) {
        if (!login.IsAuthenticated || login.NativeUsage is null) {
            return;
        }

        var usage = login.NativeUsage;
        var accountId = NormalizeLocalProviderOpenAIAccountId(usage.AccountId);
        if (accountId.Length == 0) {
            accountId = NormalizeLocalProviderOpenAIAccountId(login.AccountId);
        }

        var planType = NormalizeOptionalText(usage.PlanType);
        var email = NormalizeOptionalText(usage.Email);
        var identity = ResolveNativeUsageIdentity(accountId, email);
        var nowUtc = DateTime.UtcNow;
        var rateLimit = usage.RateLimit;
        var rateLimitResetUtc = TryResolveRateLimitResetUtc(rateLimit, nowUtc);
        var rateLimitUsedPercent = ResolveRateLimitUsedPercent(rateLimit);
        var rateLimitReached = rateLimit is null ? (bool?)null : rateLimit.LimitReached;
        var rateLimitAllowed = rateLimit is null ? (bool?)null : rateLimit.Allowed;
        var usageRetrievedAtUtc = usage.RetrievedAtUtc.HasValue
            ? EnsureUtc(usage.RetrievedAtUtc.Value)
            : (DateTime?)null;
        var snapshotSource = NormalizeOptionalText(usage.Source);

        var credits = usage.Credits;
        var creditsHasCredits = credits is null ? (bool?)null : credits.HasCredits;
        var creditsUnlimited = credits is null ? (bool?)null : credits.Unlimited;
        var creditsBalance = credits?.Balance;
        if (creditsBalance.HasValue && (double.IsNaN(creditsBalance.Value) || double.IsInfinity(creditsBalance.Value))) {
            creditsBalance = null;
        }

        lock (_turnDiagnosticsSync) {
            if (!_accountUsageByKey.TryGetValue(identity.Key, out var current)) {
                current = CreateEmptyUsageSnapshot(identity);
            }

            if (!string.Equals(identity.Key, UnknownNativeUsageKey, StringComparison.OrdinalIgnoreCase)
                && _accountUsageByKey.TryGetValue(UnknownNativeUsageKey, out var unresolvedSnapshot)) {
                current = current with {
                    PromptTokens = checked(current.PromptTokens + unresolvedSnapshot.PromptTokens),
                    CompletionTokens = checked(current.CompletionTokens + unresolvedSnapshot.CompletionTokens),
                    TotalTokens = checked(current.TotalTokens + unresolvedSnapshot.TotalTokens),
                    CachedPromptTokens = checked(current.CachedPromptTokens + unresolvedSnapshot.CachedPromptTokens),
                    ReasoningTokens = checked(current.ReasoningTokens + unresolvedSnapshot.ReasoningTokens),
                    Turns = checked(current.Turns + unresolvedSnapshot.Turns),
                    LastSeenUtc = SelectLater(current.LastSeenUtc, unresolvedSnapshot.LastSeenUtc),
                    UsageLimitHitUtc = SelectLater(current.UsageLimitHitUtc, unresolvedSnapshot.UsageLimitHitUtc),
                    UsageLimitRetryAfterUtc = SelectLater(current.UsageLimitRetryAfterUtc, unresolvedSnapshot.UsageLimitRetryAfterUtc),
                    RateLimitWindowResetUtc = SelectLater(current.RateLimitWindowResetUtc, unresolvedSnapshot.RateLimitWindowResetUtc),
                    UsageSnapshotRetrievedAtUtc = SelectLater(current.UsageSnapshotRetrievedAtUtc, unresolvedSnapshot.UsageSnapshotRetrievedAtUtc),
                    UsageSnapshotSource = string.IsNullOrWhiteSpace(current.UsageSnapshotSource)
                        ? unresolvedSnapshot.UsageSnapshotSource
                        : current.UsageSnapshotSource,
                    PlanType = string.IsNullOrWhiteSpace(current.PlanType)
                        ? unresolvedSnapshot.PlanType
                        : current.PlanType,
                    Email = string.IsNullOrWhiteSpace(current.Email)
                        ? unresolvedSnapshot.Email
                        : current.Email,
                    RateLimitAllowed = current.RateLimitAllowed ?? unresolvedSnapshot.RateLimitAllowed,
                    RateLimitReached = current.RateLimitReached ?? unresolvedSnapshot.RateLimitReached,
                    RateLimitUsedPercent = current.RateLimitUsedPercent ?? unresolvedSnapshot.RateLimitUsedPercent,
                    CreditsHasCredits = current.CreditsHasCredits ?? unresolvedSnapshot.CreditsHasCredits,
                    CreditsUnlimited = current.CreditsUnlimited ?? unresolvedSnapshot.CreditsUnlimited,
                    CreditsBalance = current.CreditsBalance ?? unresolvedSnapshot.CreditsBalance
                };
                _accountUsageByKey.Remove(UnknownNativeUsageKey);
            }

            var usageLimitHitUtc = current.UsageLimitHitUtc;
            var usageLimitRetryAfterUtc = current.UsageLimitRetryAfterUtc;
            if (rateLimitReached == true) {
                usageLimitHitUtc ??= nowUtc;
                if (rateLimitResetUtc.HasValue) {
                    usageLimitRetryAfterUtc = rateLimitResetUtc;
                }
            } else if (rateLimitReached == false) {
                usageLimitHitUtc = null;
                usageLimitRetryAfterUtc = null;
            }

            _accountUsageByKey[identity.Key] = current with {
                Label = identity.Label,
                LastSeenUtc = nowUtc,
                UsageLimitHitUtc = usageLimitHitUtc,
                UsageLimitRetryAfterUtc = usageLimitRetryAfterUtc,
                PlanType = planType,
                Email = email,
                RateLimitAllowed = rateLimitAllowed,
                RateLimitReached = rateLimitReached,
                RateLimitUsedPercent = rateLimitUsedPercent,
                RateLimitWindowResetUtc = rateLimitResetUtc,
                UsageSnapshotRetrievedAtUtc = usageRetrievedAtUtc,
                UsageSnapshotSource = snapshotSource,
                CreditsHasCredits = creditsHasCredits,
                CreditsUnlimited = creditsUnlimited,
                CreditsBalance = creditsBalance
            };

            TrimAccountUsageCacheLocked();
            SyncAccountUsageToAppStateLocked();
        }
    }

    private static DateTime? SelectLater(DateTime? firstUtc, DateTime? secondUtc) {
        var first = firstUtc.HasValue ? EnsureUtc(firstUtc.Value) : (DateTime?)null;
        var second = secondUtc.HasValue ? EnsureUtc(secondUtc.Value) : (DateTime?)null;
        if (!first.HasValue) {
            return second;
        }

        if (!second.HasValue) {
            return first;
        }

        return first.Value >= second.Value ? first : second;
    }

    private void MarkUsageLimitForActiveAccount(string? detail) {
        var identity = ResolveActiveUsageIdentity();
        var nowUtc = DateTime.UtcNow;
        var retryAfterMinutes = TryExtractUsageRetryAfterMinutes(detail);
        var retryAfterUtc = retryAfterMinutes.HasValue
            ? nowUtc.AddMinutes(retryAfterMinutes.Value)
            : (DateTime?)null;

        lock (_turnDiagnosticsSync) {
            if (!_accountUsageByKey.TryGetValue(identity.Key, out var current)) {
                current = CreateEmptyUsageSnapshot(identity);
            }

            _accountUsageByKey[identity.Key] = current with {
                Label = string.IsNullOrWhiteSpace(current.Label) ? identity.Label : current.Label,
                UsageLimitHitUtc = nowUtc,
                UsageLimitRetryAfterUtc = retryAfterUtc
            };

            TrimAccountUsageCacheLocked();
            SyncAccountUsageToAppStateLocked();
        }
    }

    private bool IsActiveUsageLimitDispatchBlocked(out int? retryAfterMinutes) {
        var identity = ResolveActiveUsageIdentity();
        var nowUtc = DateTime.UtcNow;
        lock (_turnDiagnosticsSync) {
            if (!_accountUsageByKey.TryGetValue(identity.Key, out var snapshot)) {
                retryAfterMinutes = null;
                return false;
            }

            return IsUsageLimitDispatchBlocked(
                nowUtc,
                snapshot.UsageLimitHitUtc,
                snapshot.UsageLimitRetryAfterUtc,
                snapshot.RateLimitReached,
                out retryAfterMinutes);
        }
    }

    private string ResolveActiveUsageLabelForDisplay() {
        var identity = ResolveActiveUsageIdentity();
        lock (_turnDiagnosticsSync) {
            if (_accountUsageByKey.TryGetValue(identity.Key, out var snapshot)
                && !string.IsNullOrWhiteSpace(snapshot.Label)) {
                return snapshot.Label;
            }

            if (string.Equals(_localProviderTransport, TransportNative, StringComparison.OrdinalIgnoreCase)) {
                var selectedAccountId = NormalizeLocalProviderOpenAIAccountId(_localProviderOpenAIAccountId);
                if (selectedAccountId.Length > 0) {
                    var selectedKey = BuildNativeUsageKey(selectedAccountId);
                    if (_accountUsageByKey.TryGetValue(selectedKey, out var selectedSnapshot)
                        && !string.IsNullOrWhiteSpace(selectedSnapshot.Label)) {
                        return selectedSnapshot.Label;
                    }

                    return ResolveNativeUsageIdentity(selectedAccountId).Label;
                }
            }
        }

        return identity.Label;
    }

    private void ClearUsageLimitDispatchBlockForActiveAccount() {
        var identity = ResolveActiveUsageIdentity();
        lock (_turnDiagnosticsSync) {
            if (!_accountUsageByKey.TryGetValue(identity.Key, out var snapshot)) {
                return;
            }

            _accountUsageByKey[identity.Key] = snapshot with {
                UsageLimitHitUtc = null,
                UsageLimitRetryAfterUtc = null,
                RateLimitReached = false
            };
            SyncAccountUsageToAppStateLocked();
        }
    }

    internal static bool IsUsageLimitDispatchBlocked(
        DateTime nowUtc,
        DateTime? usageLimitHitUtc,
        DateTime? usageLimitRetryAfterUtc,
        bool? rateLimitReached,
        out int? retryAfterMinutes) {
        retryAfterMinutes = null;
        var normalizedNowUtc = EnsureUtc(nowUtc);

        if (usageLimitRetryAfterUtc.HasValue) {
            var remaining = EnsureUtc(usageLimitRetryAfterUtc.Value) - normalizedNowUtc;
            if (remaining > TimeSpan.Zero) {
                retryAfterMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
                return true;
            }
        }

        if (rateLimitReached == true) {
            return true;
        }

        if (!usageLimitHitUtc.HasValue) {
            return false;
        }

        var hitUtc = EnsureUtc(usageLimitHitUtc.Value);
        // If provider omitted Retry-After, keep a short cooldown to prevent immediate replay loops.
        return normalizedNowUtc <= hitUtc || normalizedNowUtc - hitUtc < UsageLimitDispatchCooldown;
    }

    private void RestoreAccountUsageFromAppState() {
        lock (_turnDiagnosticsSync) {
            _accountUsageByKey.Clear();
            if (_appState.AccountUsage is not { Count: > 0 }) {
                return;
            }

            for (var i = 0; i < _appState.AccountUsage.Count; i++) {
                var item = _appState.AccountUsage[i];
                var key = (item.Key ?? string.Empty).Trim();
                if (key.Length == 0) {
                    continue;
                }

                var label = (item.Label ?? string.Empty).Trim();
                if (label.Length == 0) {
                    label = key;
                }

                var rateLimitUsedPercent = item.RateLimitUsedPercent;
                if (rateLimitUsedPercent.HasValue && (double.IsNaN(rateLimitUsedPercent.Value) || double.IsInfinity(rateLimitUsedPercent.Value))) {
                    rateLimitUsedPercent = null;
                }

                var creditsBalance = item.CreditsBalance;
                if (creditsBalance.HasValue && (double.IsNaN(creditsBalance.Value) || double.IsInfinity(creditsBalance.Value))) {
                    creditsBalance = null;
                }

                _accountUsageByKey[key] = new AccountUsageSnapshot(
                    Key: key,
                    Label: label,
                    PromptTokens: Math.Max(0L, item.PromptTokens),
                    CompletionTokens: Math.Max(0L, item.CompletionTokens),
                    TotalTokens: Math.Max(0L, item.TotalTokens),
                    CachedPromptTokens: Math.Max(0L, item.CachedPromptTokens),
                    ReasoningTokens: Math.Max(0L, item.ReasoningTokens),
                    Turns: Math.Max(0, item.Turns),
                    LastSeenUtc: item.LastSeenUtc,
                    UsageLimitHitUtc: item.UsageLimitHitUtc,
                    UsageLimitRetryAfterUtc: item.UsageLimitRetryAfterUtc,
                    PlanType: NormalizeOptionalText(item.PlanType),
                    Email: NormalizeOptionalText(item.Email),
                    RateLimitAllowed: item.RateLimitAllowed,
                    RateLimitReached: item.RateLimitReached,
                    RateLimitUsedPercent: NormalizeRateLimitPercent(rateLimitUsedPercent),
                    RateLimitWindowResetUtc: item.RateLimitWindowResetUtc,
                    UsageSnapshotRetrievedAtUtc: item.UsageSnapshotRetrievedAtUtc,
                    UsageSnapshotSource: NormalizeOptionalText(item.UsageSnapshotSource),
                    CreditsHasCredits: item.CreditsHasCredits,
                    CreditsUnlimited: item.CreditsUnlimited,
                    CreditsBalance: creditsBalance);
            }

            TrimAccountUsageCacheLocked();
            SyncAccountUsageToAppStateLocked();
        }
    }

    private void SyncAccountUsageToAppStateLocked() {
        var snapshots = new List<ChatAccountUsageState>(_accountUsageByKey.Count);
        foreach (var snapshot in _accountUsageByKey.Values) {
            snapshots.Add(new ChatAccountUsageState {
                Key = snapshot.Key,
                Label = snapshot.Label,
                PromptTokens = snapshot.PromptTokens,
                CompletionTokens = snapshot.CompletionTokens,
                TotalTokens = snapshot.TotalTokens,
                CachedPromptTokens = snapshot.CachedPromptTokens,
                ReasoningTokens = snapshot.ReasoningTokens,
                Turns = snapshot.Turns,
                LastSeenUtc = snapshot.LastSeenUtc,
                UsageLimitHitUtc = snapshot.UsageLimitHitUtc,
                UsageLimitRetryAfterUtc = snapshot.UsageLimitRetryAfterUtc,
                PlanType = snapshot.PlanType,
                Email = snapshot.Email,
                RateLimitAllowed = snapshot.RateLimitAllowed,
                RateLimitReached = snapshot.RateLimitReached,
                RateLimitUsedPercent = snapshot.RateLimitUsedPercent,
                RateLimitWindowResetUtc = snapshot.RateLimitWindowResetUtc,
                UsageSnapshotRetrievedAtUtc = snapshot.UsageSnapshotRetrievedAtUtc,
                UsageSnapshotSource = snapshot.UsageSnapshotSource,
                CreditsHasCredits = snapshot.CreditsHasCredits,
                CreditsUnlimited = snapshot.CreditsUnlimited,
                CreditsBalance = snapshot.CreditsBalance
            });
        }

        snapshots.Sort((left, right) => Nullable.Compare(right.LastSeenUtc, left.LastSeenUtc));
        if (snapshots.Count > MaxTrackedAccounts) {
            snapshots.RemoveRange(MaxTrackedAccounts, snapshots.Count - MaxTrackedAccounts);
        }

        _appState.AccountUsage = snapshots;
    }

    private void TrimAccountUsageCacheLocked() {
        if (_accountUsageByKey.Count <= MaxTrackedAccounts) {
            return;
        }

        var entries = new List<AccountUsageSnapshot>(_accountUsageByKey.Values);
        entries.Sort((left, right) => Nullable.Compare(right.LastSeenUtc, left.LastSeenUtc));
        while (entries.Count > MaxTrackedAccounts) {
            var removed = entries[^1];
            entries.RemoveAt(entries.Count - 1);
            _accountUsageByKey.Remove(removed.Key);
        }
    }

}

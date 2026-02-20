using System;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const int NativeAccountSlotCount = 3;

    private static int NormalizeNativeAccountSlot(int value) {
        if (value < 1) {
            return 1;
        }

        if (value > NativeAccountSlotCount) {
            return NativeAccountSlotCount;
        }

        return value;
    }

    private static int NormalizeNativeAccountSlot(int? value) {
        return NormalizeNativeAccountSlot(value.GetValueOrDefault(1));
    }

    private static string NormalizeLocalProviderOpenAIAccountId(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
    }

    private static string BuildNativeUsageKey(string accountId) {
        return "native:" + accountId.Trim().ToLowerInvariant();
    }

    private string GetNativeAccountSlotId(int slot) {
        return _nativeAccountSlots[NormalizeNativeAccountSlot(slot) - 1];
    }

    private void SetNativeAccountSlotId(int slot, string? accountId) {
        _nativeAccountSlots[NormalizeNativeAccountSlot(slot) - 1] = NormalizeLocalProviderOpenAIAccountId(accountId);
    }

    private void RestoreNativeAccountSlotsFromAppState() {
        _activeNativeAccountSlot = NormalizeNativeAccountSlot(_appState.ActiveNativeAccountSlot);
        _nativeAccountSlots[0] = NormalizeLocalProviderOpenAIAccountId(_appState.NativeAccountSlot1);
        _nativeAccountSlots[1] = NormalizeLocalProviderOpenAIAccountId(_appState.NativeAccountSlot2);
        _nativeAccountSlots[2] = NormalizeLocalProviderOpenAIAccountId(_appState.NativeAccountSlot3);

        _localProviderOpenAIAccountId = NormalizeLocalProviderOpenAIAccountId(_appState.LocalProviderOpenAIAccountId);
        if (_localProviderOpenAIAccountId.Length == 0) {
            _localProviderOpenAIAccountId = GetNativeAccountSlotId(_activeNativeAccountSlot);
        }

        if (_localProviderOpenAIAccountId.Length > 0 && GetNativeAccountSlotId(_activeNativeAccountSlot).Length == 0) {
            SetNativeAccountSlotId(_activeNativeAccountSlot, _localProviderOpenAIAccountId);
        }

        SyncNativeAccountSlotsToAppState();
    }

    private void SyncNativeAccountSlotsToAppState() {
        _appState.ActiveNativeAccountSlot = NormalizeNativeAccountSlot(_activeNativeAccountSlot);
        _appState.NativeAccountSlot1 = GetNativeAccountSlotId(1);
        _appState.NativeAccountSlot2 = GetNativeAccountSlotId(2);
        _appState.NativeAccountSlot3 = GetNativeAccountSlotId(3);
        _appState.LocalProviderOpenAIAccountId = _localProviderOpenAIAccountId;
    }

    private void ApplyNativeAccountSlotSettings(int? requestedSlot, string? requestedSlotAccountId, string? requestedOpenAIAccountId) {
        var slot = requestedSlot.HasValue ? NormalizeNativeAccountSlot(requestedSlot.Value) : _activeNativeAccountSlot;
        _activeNativeAccountSlot = slot;

        if (requestedSlotAccountId is not null) {
            SetNativeAccountSlotId(slot, requestedSlotAccountId);
        }

        if (requestedOpenAIAccountId is not null) {
            var normalizedRequested = NormalizeLocalProviderOpenAIAccountId(requestedOpenAIAccountId);
            _localProviderOpenAIAccountId = normalizedRequested;
            SetNativeAccountSlotId(slot, normalizedRequested);
        } else {
            _localProviderOpenAIAccountId = GetNativeAccountSlotId(slot);
        }

        SyncNativeAccountSlotsToAppState();
    }

    private void CaptureAuthenticatedAccountIntoActiveSlot() {
        var accountId = NormalizeLocalProviderOpenAIAccountId(_authenticatedAccountId);
        if (accountId.Length == 0) {
            return;
        }

        SetNativeAccountSlotId(_activeNativeAccountSlot, accountId);
        _localProviderOpenAIAccountId = accountId;
        SyncNativeAccountSlotsToAppState();
    }

    private object[] BuildNativeAccountSlotState() {
        lock (_turnDiagnosticsSync) {
            var result = new object[NativeAccountSlotCount];
            for (var slot = 1; slot <= NativeAccountSlotCount; slot++) {
                var accountId = GetNativeAccountSlotId(slot);
                var usageTotalTokens = 0L;
                var usageTurns = 0;
                int? retryAfterMinutes = null;
                int? windowResetMinutes = null;
                string? planType = null;
                double? usedPercent = null;
                bool? limitReached = null;
                if (accountId.Length > 0 && _accountUsageByKey.TryGetValue(BuildNativeUsageKey(accountId), out var usage)) {
                    usageTotalTokens = Math.Max(0L, usage.TotalTokens);
                    usageTurns = Math.Max(0, usage.Turns);
                    if (usage.UsageLimitRetryAfterUtc.HasValue) {
                        var remaining = usage.UsageLimitRetryAfterUtc.Value - DateTime.UtcNow;
                        retryAfterMinutes = remaining > TimeSpan.Zero
                            ? (int)Math.Ceiling(remaining.TotalMinutes)
                            : 0;
                    }
                    if (usage.RateLimitWindowResetUtc.HasValue) {
                        var remaining = usage.RateLimitWindowResetUtc.Value - DateTime.UtcNow;
                        windowResetMinutes = remaining > TimeSpan.Zero
                            ? (int)Math.Ceiling(remaining.TotalMinutes)
                            : 0;
                    }
                    planType = usage.PlanType;
                    usedPercent = usage.RateLimitUsedPercent;
                    limitReached = usage.RateLimitReached;
                }

                result[slot - 1] = new {
                    slot,
                    accountId,
                    active = slot == _activeNativeAccountSlot,
                    usageTotalTokens,
                    usageTurns,
                    retryAfterMinutes,
                    windowResetMinutes,
                    planType = planType ?? string.Empty,
                    usedPercent,
                    limitReached
                };
            }

            return result;
        }
    }
}

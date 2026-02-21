using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const int DefaultNativeAccountSlotCount = 3;
    private const int MaxNativeAccountSlotCount = 32;
    private const string NativeAccountSlotCountEnvVar = "IXCHAT_NATIVE_ACCOUNT_SLOTS";

    private static int ResolveNativeAccountSlotCount() {
        var raw = Environment.GetEnvironmentVariable(NativeAccountSlotCountEnvVar);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return DefaultNativeAccountSlotCount;
        }

        if (parsed < 1) {
            return 1;
        }

        return Math.Min(parsed, MaxNativeAccountSlotCount);
    }

    private int GetNativeAccountSlotCount() {
        return _nativeAccountSlots.Length > 0 ? _nativeAccountSlots.Length : 1;
    }

    private int NormalizeNativeAccountSlot(int value) {
        var slotCount = GetNativeAccountSlotCount();
        if (value < 1) {
            return 1;
        }

        if (value > slotCount) {
            return slotCount;
        }

        return value;
    }

    private int NormalizeNativeAccountSlot(int? value) {
        return NormalizeNativeAccountSlot(value.GetValueOrDefault(1));
    }

    private static string NormalizeLocalProviderOpenAIAccountId(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? string.Empty : normalized;
    }

    private static string BuildNativeUsageKey(string accountId) {
        return "native:" + accountId.Trim().ToLowerInvariant();
    }

    private string GetNativeAccountSlotIdOrEmpty(int slot) {
        if (slot < 1 || slot > _nativeAccountSlots.Length) {
            return string.Empty;
        }

        return _nativeAccountSlots[slot - 1];
    }

    private string GetNativeAccountSlotId(int slot) {
        return GetNativeAccountSlotIdOrEmpty(NormalizeNativeAccountSlot(slot));
    }

    private void SetNativeAccountSlotId(int slot, string? accountId) {
        _nativeAccountSlots[NormalizeNativeAccountSlot(slot) - 1] = NormalizeLocalProviderOpenAIAccountId(accountId);
    }

    private static IReadOnlyList<string> ResolveLegacyNativeAccountSlotState(ChatAppState state) {
        return new[] { state.NativeAccountSlot1, state.NativeAccountSlot2, state.NativeAccountSlot3 };
    }

    private static IReadOnlyList<string> ResolvePersistedNativeAccountSlotState(ChatAppState state) {
        if (state.NativeAccountSlots is { Count: > 0 }) {
            return state.NativeAccountSlots;
        }

        return ResolveLegacyNativeAccountSlotState(state);
    }

    private string[] SnapshotNativeAccountSlots() {
        var snapshot = new string[_nativeAccountSlots.Length];
        Array.Copy(_nativeAccountSlots, snapshot, _nativeAccountSlots.Length);
        return snapshot;
    }

    private bool HaveNativeAccountSlotsChanged(IReadOnlyList<string> previous) {
        if (previous is null || previous.Count != _nativeAccountSlots.Length) {
            return true;
        }

        for (var i = 0; i < _nativeAccountSlots.Length; i++) {
            if (!string.Equals(previous[i], _nativeAccountSlots[i], StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private void RestoreNativeAccountSlotsFromSnapshot(IReadOnlyList<string> snapshot) {
        Array.Fill(_nativeAccountSlots, string.Empty);
        if (snapshot is null) {
            return;
        }

        var copyLength = Math.Min(_nativeAccountSlots.Length, snapshot.Count);
        for (var i = 0; i < copyLength; i++) {
            _nativeAccountSlots[i] = NormalizeLocalProviderOpenAIAccountId(snapshot[i]);
        }
    }

    private void RestoreNativeAccountSlotsFromAppState() {
        _activeNativeAccountSlot = NormalizeNativeAccountSlot(_appState.ActiveNativeAccountSlot);
        Array.Fill(_nativeAccountSlots, string.Empty);
        var persistedSlots = ResolvePersistedNativeAccountSlotState(_appState);
        var copyLength = Math.Min(_nativeAccountSlots.Length, persistedSlots.Count);
        for (var i = 0; i < copyLength; i++) {
            _nativeAccountSlots[i] = NormalizeLocalProviderOpenAIAccountId(persistedSlots[i]);
        }

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
        _appState.NativeAccountSlot1 = GetNativeAccountSlotIdOrEmpty(1);
        _appState.NativeAccountSlot2 = GetNativeAccountSlotIdOrEmpty(2);
        _appState.NativeAccountSlot3 = GetNativeAccountSlotIdOrEmpty(3);
        _appState.NativeAccountSlots = new List<string>(_nativeAccountSlots);
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
            var slotCount = GetNativeAccountSlotCount();
            var result = new object[slotCount];
            for (var slot = 1; slot <= slotCount; slot++) {
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

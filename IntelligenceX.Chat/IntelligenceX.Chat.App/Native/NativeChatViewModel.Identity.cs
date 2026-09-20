using System;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatViewModel {
    private string? _authenticatedAccountId;

    /// <summary>
    /// Account confirmed by the current login check, never inferred from a saved profile.
    /// Cleared when authentication is invalidated or rechecked.
    /// </summary>
    public string? AuthenticatedAccountId {
        get => _authenticatedAccountId;
        private set {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (string.Equals(_authenticatedAccountId, normalized, StringComparison.Ordinal)) {
                return;
            }
            _authenticatedAccountId = normalized;
            OnPropertyChanged();
        }
    }
}

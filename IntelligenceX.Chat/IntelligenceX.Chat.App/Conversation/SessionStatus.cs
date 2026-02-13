namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Typed session-status contract for UI status-chip text.
/// </summary>
internal readonly record struct SessionStatus(SessionStatusKind Kind) {
    public static SessionStatus Connected() => new(SessionStatusKind.Connected);
    public static SessionStatus SignInRequired() => new(SessionStatusKind.SignInRequired);
    public static SessionStatus Disconnected() => new(SessionStatusKind.Disconnected);
    public static SessionStatus Connecting() => new(SessionStatusKind.Connecting);
    public static SessionStatus ConnectFailed() => new(SessionStatusKind.ConnectFailed);
    public static SessionStatus PreviousRequestStillRunning() => new(SessionStatusKind.PreviousRequestStillRunning);
    public static SessionStatus WaitingForSignIn() => new(SessionStatusKind.WaitingForSignIn);
    public static SessionStatus NoActiveTurnToCancel() => new(SessionStatusKind.NoActiveTurnToCancel);
    public static SessionStatus Canceling() => new(SessionStatusKind.Canceling);
    public static SessionStatus ClipboardHasNoText() => new(SessionStatusKind.ClipboardHasNoText);
    public static SessionStatus ClipboardEmpty() => new(SessionStatusKind.ClipboardEmpty);
    public static SessionStatus CompleteSignInInBrowser() => new(SessionStatusKind.CompleteSignInInBrowser);
    public static SessionStatus SignInFailed() => new(SessionStatusKind.SignInFailed);
    public static SessionStatus DebugModeOn() => new(SessionStatusKind.DebugModeOn);
    public static SessionStatus CannotDeleteActiveConversationDuringTurn() => new(SessionStatusKind.CannotDeleteActiveConversationDuringTurn);
    public static SessionStatus OpeningSignIn() => new(SessionStatusKind.OpeningSignIn);
    public static SessionStatus UsageLimitReached() => new(SessionStatusKind.UsageLimitReached);
    public static SessionStatus ExportFailed() => new(SessionStatusKind.ExportFailed);
    public static SessionStatus Exporting() => new(SessionStatusKind.Exporting);

    public static SessionStatus ForConnectedAuth(bool isAuthenticated) => isAuthenticated ? Connected() : SignInRequired();

    public static SessionStatus ForConnection(bool isConnected, bool isAuthenticated) => !isConnected ? Disconnected() : ForConnectedAuth(isAuthenticated);
}

/// <summary>
/// Session-status categories.
/// </summary>
internal enum SessionStatusKind {
    Connected,
    SignInRequired,
    Disconnected,
    Connecting,
    ConnectFailed,
    PreviousRequestStillRunning,
    WaitingForSignIn,
    NoActiveTurnToCancel,
    Canceling,
    ClipboardHasNoText,
    ClipboardEmpty,
    CompleteSignInInBrowser,
    SignInFailed,
    DebugModeOn,
    CannotDeleteActiveConversationDuringTurn,
    OpeningSignIn,
    UsageLimitReached,
    ExportFailed,
    Exporting
}

/// <summary>
/// Visual tone used by the status chip.
/// </summary>
internal enum SessionStatusTone {
    Neutral,
    Ok,
    Warn,
    Bad
}

/// <summary>
/// Formats typed session statuses into UI text.
/// </summary>
internal static class SessionStatusFormatter {
    public static string Format(SessionStatus status) {
        return status.Kind switch {
            SessionStatusKind.Connected => "Ready",
            SessionStatusKind.SignInRequired => "Sign in to continue",
            SessionStatusKind.Disconnected => "Starting runtime...",
            SessionStatusKind.Connecting => "Starting runtime...",
            SessionStatusKind.ConnectFailed => "Runtime unavailable",
            SessionStatusKind.PreviousRequestStillRunning => "Previous request still running...",
            SessionStatusKind.WaitingForSignIn => "Waiting for sign-in...",
            SessionStatusKind.NoActiveTurnToCancel => "No active turn to cancel",
            SessionStatusKind.Canceling => "Canceling...",
            SessionStatusKind.ClipboardHasNoText => "Clipboard has no text",
            SessionStatusKind.ClipboardEmpty => "Clipboard empty",
            SessionStatusKind.CompleteSignInInBrowser => "Finish sign-in in browser...",
            SessionStatusKind.SignInFailed => "Sign in failed",
            SessionStatusKind.DebugModeOn => "Debug mode on",
            SessionStatusKind.CannotDeleteActiveConversationDuringTurn => "Cannot delete active conversation during a running turn",
            SessionStatusKind.OpeningSignIn => "Opening sign-in...",
            SessionStatusKind.UsageLimitReached => "Usage limit reached - switch account",
            SessionStatusKind.ExportFailed => "Export failed",
            SessionStatusKind.Exporting => "Exporting...",
            _ => "Starting runtime..."
        };
    }
}

/// <summary>
/// Maps typed session statuses to status-chip visual tones.
/// </summary>
internal static class SessionStatusToneResolver {
    public static SessionStatusTone Resolve(SessionStatus status) {
        return status.Kind switch {
            SessionStatusKind.Connected => SessionStatusTone.Ok,
            SessionStatusKind.ConnectFailed => SessionStatusTone.Bad,
            SessionStatusKind.SignInFailed => SessionStatusTone.Bad,
            SessionStatusKind.UsageLimitReached => SessionStatusTone.Bad,
            SessionStatusKind.WaitingForSignIn => SessionStatusTone.Warn,
            SessionStatusKind.SignInRequired => SessionStatusTone.Warn,
            SessionStatusKind.Connecting => SessionStatusTone.Warn,
            SessionStatusKind.Disconnected => SessionStatusTone.Warn,
            SessionStatusKind.OpeningSignIn => SessionStatusTone.Warn,
            SessionStatusKind.CompleteSignInInBrowser => SessionStatusTone.Warn,
            SessionStatusKind.PreviousRequestStillRunning => SessionStatusTone.Warn,
            SessionStatusKind.Canceling => SessionStatusTone.Warn,
            SessionStatusKind.DebugModeOn => SessionStatusTone.Warn,
            SessionStatusKind.Exporting => SessionStatusTone.Warn,
            SessionStatusKind.NoActiveTurnToCancel => SessionStatusTone.Neutral,
            SessionStatusKind.ClipboardHasNoText => SessionStatusTone.Neutral,
            SessionStatusKind.ClipboardEmpty => SessionStatusTone.Neutral,
            SessionStatusKind.CannotDeleteActiveConversationDuringTurn => SessionStatusTone.Neutral,
            SessionStatusKind.ExportFailed => SessionStatusTone.Bad,
            _ => SessionStatusTone.Neutral
        };
    }
}

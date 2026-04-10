namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Typed system-notice contract for transcript system messages.
/// </summary>
internal readonly record struct SystemNotice(SystemNoticeKind Kind, string? Detail = null, string? Code = null) {
    public static SystemNotice StateLoadFailed(string? detail) => new(SystemNoticeKind.StateLoadFailed, detail);
    public static SystemNotice StateSaveFailed(string? detail) => new(SystemNoticeKind.StateSaveFailed, detail);
    public static SystemNotice ServiceStagingError(string? detail) => new(SystemNoticeKind.ServiceStagingError, detail);
    public static SystemNotice EnsureLoginFailed(string? detail) => new(SystemNoticeKind.EnsureLoginFailed, detail);
    public static SystemNotice SignInFailed(string? detail) => new(SystemNoticeKind.SignInFailed, detail);
    public static SystemNotice LoginSubmitFailed(string? detail) => new(SystemNoticeKind.LoginSubmitFailed, detail);
    public static SystemNotice ServiceSidecarSourceFolderNotFound() => new(SystemNoticeKind.ServiceSidecarSourceFolderNotFound);
    public static SystemNotice ServiceSidecarStagingFailed() => new(SystemNoticeKind.ServiceSidecarStagingFailed);
    public static SystemNotice ServiceSidecarNotFoundNextToApp() => new(SystemNoticeKind.ServiceSidecarNotFoundNextToApp);
    public static SystemNotice ServiceStdOut(string? detail) => new(SystemNoticeKind.ServiceStdOut, detail);
    public static SystemNotice ServiceStdErr(string? detail) => new(SystemNoticeKind.ServiceStdErr, detail);
    public static SystemNotice ServiceExited() => new(SystemNoticeKind.ServiceExited);
    public static SystemNotice ServiceStartFailed(string? detail) => new(SystemNoticeKind.ServiceStartFailed, detail);
    public static SystemNotice ModelKickoffFailed(string? detail) => new(SystemNoticeKind.ModelKickoffFailed, detail);
    public static SystemNotice UiMessageError(string? detail) => new(SystemNoticeKind.UiMessageError, detail);
    public static SystemNotice ExportMissingRowsPayload() => new(SystemNoticeKind.ExportMissingRowsPayload);
    public static SystemNotice ExportMissingFormat() => new(SystemNoticeKind.ExportMissingFormat);
    public static SystemNotice ConnectProbeFailed(string? detail) => new(SystemNoticeKind.ConnectProbeFailed, detail);
    public static SystemNotice ConnectFailedAfterSidecarStart(string? detail) => new(SystemNoticeKind.ConnectFailedAfterSidecarStart, detail);
    public static SystemNotice ConnectFailed(string? detail) => new(SystemNoticeKind.ConnectFailed, detail);
    public static SystemNotice ServiceSidecarUnavailable() => new(SystemNoticeKind.ServiceSidecarUnavailable);
    public static SystemNotice HelloFailed(string? detail) => new(SystemNoticeKind.HelloFailed, detail);
    public static SystemNotice ListToolsFailed(string? detail) => new(SystemNoticeKind.ListToolsFailed, detail);
    public static SystemNotice SignInRequiredBeforeSendingMessages() => new(SystemNoticeKind.SignInRequiredBeforeSendingMessages);
    public static SystemNotice CancelRequestFailed(string? detail) => new(SystemNoticeKind.CancelRequestFailed, detail);
    public static SystemNotice LoginFailed(string? detail) => new(SystemNoticeKind.LoginFailed, detail);
    public static SystemNotice PromptQueuedAfterUsageLimit(string? detail = null) => new(SystemNoticeKind.PromptQueuedAfterUsageLimit, detail);
    public static SystemNotice ServiceError(string? error, string? code) => new(SystemNoticeKind.ServiceError, error, code);
    public static SystemNotice TranscriptExported(string? detail) => new(SystemNoticeKind.TranscriptExported, detail);
}

/// <summary>
/// System-notice categories.
/// </summary>
internal enum SystemNoticeKind {
    StateLoadFailed,
    StateSaveFailed,
    ServiceStagingError,
    EnsureLoginFailed,
    SignInFailed,
    LoginSubmitFailed,
    ServiceSidecarSourceFolderNotFound,
    ServiceSidecarStagingFailed,
    ServiceSidecarNotFoundNextToApp,
    ServiceStdOut,
    ServiceStdErr,
    ServiceExited,
    ServiceStartFailed,
    ModelKickoffFailed,
    UiMessageError,
    ExportMissingRowsPayload,
    ExportMissingFormat,
    ConnectProbeFailed,
    ConnectFailedAfterSidecarStart,
    ConnectFailed,
    ServiceSidecarUnavailable,
    HelloFailed,
    ListToolsFailed,
    SignInRequiredBeforeSendingMessages,
    CancelRequestFailed,
    LoginFailed,
    PromptQueuedAfterUsageLimit,
    ServiceError,
    TranscriptExported
}

/// <summary>
/// Formats typed system notices into transcript strings.
/// </summary>
internal static class SystemNoticeFormatter {
    public static string Format(SystemNotice notice) {
        return notice.Kind switch {
            SystemNoticeKind.StateLoadFailed => "State load failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.StateSaveFailed => "State save failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ServiceStagingError => "Background service setup failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.EnsureLoginFailed => "ensure_login failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.SignInFailed => "Sign-in failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.LoginSubmitFailed => "Login submit failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ServiceSidecarSourceFolderNotFound => "Background service files were not found.",
            SystemNoticeKind.ServiceSidecarStagingFailed => "Couldn't prepare background service files.",
            SystemNoticeKind.ServiceSidecarNotFoundNextToApp => "Background service executable is missing.",
            SystemNoticeKind.ServiceStdOut => "[service] " + DetailOrEmpty(notice.Detail),
            SystemNoticeKind.ServiceStdErr => "[service:err] " + DetailOrEmpty(notice.Detail),
            SystemNoticeKind.ServiceExited => "[service] exited",
            SystemNoticeKind.ServiceStartFailed => "Couldn't start the background service: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ModelKickoffFailed => "Model kickoff failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.UiMessageError => "UI message error: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ExportMissingRowsPayload => "Export failed: missing rows payload.",
            SystemNoticeKind.ExportMissingFormat => "Export failed: missing export format.",
            SystemNoticeKind.ConnectProbeFailed => "Connection probe failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ConnectFailedAfterSidecarStart => "Couldn't connect to local runtime after startup: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ConnectFailed => "Couldn't connect to local runtime: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ServiceSidecarUnavailable => "Local runtime is unavailable.",
            SystemNoticeKind.HelloFailed => "Runtime handshake failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.ListToolsFailed => "Tool catalog sync failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.SignInRequiredBeforeSendingMessages => "Sign-in is required before sending messages.",
            SystemNoticeKind.CancelRequestFailed => "Cancel request failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.LoginFailed => "Login failed: " + DetailOrUnknown(notice.Detail),
            SystemNoticeKind.PromptQueuedAfterUsageLimit =>
                BuildPromptQueuedAfterUsageLimitText(notice.Detail),
            SystemNoticeKind.ServiceError => "service error: " + DetailOrUnknown(notice.Detail) + " (" + DetailOrUnknown(notice.Code) + ")",
            SystemNoticeKind.TranscriptExported => "Exported transcript: " + DetailOrUnknown(notice.Detail),
            _ => "System notice."
        };
    }

    private static string BuildPromptQueuedAfterUsageLimitText(string? accountLabel) {
        var normalizedAccountLabel = (accountLabel ?? string.Empty).Trim();
        if (normalizedAccountLabel.Length == 0) {
            return "Prompt queued for retry. Use **Switch Account** in the top-right menu; after sign-in, the prompt will run automatically.";
        }

        return "Prompt queued for retry because " + normalizedAccountLabel + " hit its usage limit. Use **Switch Account** in the top-right menu; after sign-in, the prompt will run automatically.";
    }

    private static string DetailOrUnknown(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Unknown error." : normalized;
    }

    private static string DetailOrEmpty(string? detail) {
        return (detail ?? string.Empty).Trim();
    }
}

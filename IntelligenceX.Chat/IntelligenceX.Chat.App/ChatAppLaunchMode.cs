using System;

namespace IntelligenceX.Chat.App;

internal enum ChatAppLaunchMode {
    MinimalWindow,
    WebViewSmoke,
    NativeWinUI,
    LegacyWebView
}

internal static class ChatAppLaunchModeResolver {
    public const string MinimalWindowEnvVar = "IXCHAT_MIN_WINDOW";
    public const string WebViewSmokeEnvVar = "IXCHAT_WEBVIEW_SMOKE";
    public const string NativeWinUiEnvVar = "IXCHAT_NATIVE_WINUI";
    public const string LegacyWebViewEnvVar = "IXCHAT_LEGACY_WEBVIEW";

    public static ChatAppLaunchMode Resolve(Func<string, string?> getEnvironmentVariable) {
        if (getEnvironmentVariable == null) throw new ArgumentNullException(nameof(getEnvironmentVariable));
        if (IsTruthy(getEnvironmentVariable(MinimalWindowEnvVar))) {
            return ChatAppLaunchMode.MinimalWindow;
        }

        if (IsTruthy(getEnvironmentVariable(WebViewSmokeEnvVar))) {
            return ChatAppLaunchMode.WebViewSmoke;
        }

        if (IsTruthy(getEnvironmentVariable(LegacyWebViewEnvVar))) {
            return ChatAppLaunchMode.LegacyWebView;
        }

        return ChatAppLaunchMode.NativeWinUI;
    }

    internal static bool IsTruthy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var v = value.Trim();
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }
}

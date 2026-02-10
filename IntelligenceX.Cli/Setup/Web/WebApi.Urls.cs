using System;

namespace IntelligenceX.Cli.Setup.Web;

internal sealed partial class WebApi {
    private static bool TryGetApiBaseUrl(string? requested, out string apiBaseUrl, out string error) {
        apiBaseUrl = "https://api.github.com";
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requested)) {
            return true;
        }
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var uri)) {
            error = "ApiBaseUrl must be a valid absolute URL.";
            return false;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        error = "ApiBaseUrl must use https (http allowed only for localhost).";
        return false;
    }

    private static bool TryGetAuthBaseUrl(string? requested, out string authBaseUrl, out string error) {
        authBaseUrl = "https://github.com";
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requested)) {
            return true;
        }
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var uri)) {
            error = "AuthBaseUrl must be a valid absolute URL.";
            return false;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            authBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback) {
            authBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        error = "AuthBaseUrl must use https (http allowed only for localhost).";
        return false;
    }

    private static string ResolveAuthBaseUrl(string? requested) {
        return TryGetAuthBaseUrl(requested, out var resolved, out _)
            ? resolved
            : "https://github.com";
    }

    private static bool TryGetChatGptApiBaseUrl(string? requested, out string apiBaseUrl, out string error) {
        apiBaseUrl = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requested)) {
            return true;
        }
        if (!Uri.TryCreate(requested, UriKind.Absolute, out var uri)) {
            error = "ChatGptApiBaseUrl must be a valid absolute URL.";
            return false;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback) {
            apiBaseUrl = uri.ToString().TrimEnd('/');
            return true;
        }
        error = "ChatGptApiBaseUrl must use https (http allowed only for localhost).";
        return false;
    }

    private static bool TryNormalizeHttpUrl(string url, out string normalized) {
        normalized = string.Empty;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return false;
        }
        if (!string.IsNullOrEmpty(uri.UserInfo)) {
            return false;
        }
        var allow =
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
        if (!allow) {
            return false;
        }

        var builder = new UriBuilder(uri) {
            Fragment = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        };
        normalized = builder.Uri.ToString();
        return true;
    }
}

using System;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.Auth;

internal static class JwtDecoder {
    public static string? TryGetAccountId(string accessToken) {
        if (string.IsNullOrWhiteSpace(accessToken)) {
            return null;
        }
        var parts = accessToken.Split('.');
        if (parts.Length < 2) {
            return null;
        }
        var payloadJson = TryDecodeBase64Url(parts[1]);
        if (payloadJson is null) {
            return null;
        }
        var value = JsonLite.Parse(payloadJson);
        var obj = value?.AsObject();
        if (obj is null) {
            return null;
        }
        if (!obj.TryGetValue(OpenAICodexDefaults.AuthClaim, out var authNode)) {
            return null;
        }
        var authObj = authNode?.AsObject();
        if (authObj is null) {
            return null;
        }
        var accountId = authObj.GetString(OpenAICodexDefaults.AccountIdClaim);
        return string.IsNullOrWhiteSpace(accountId) ? null : accountId;
    }

    private static string? TryDecodeBase64Url(string payload) {
        var buffer = payload.Replace('-', '+').Replace('_', '/');
        switch (buffer.Length % 4) {
            case 2:
                buffer += "==";
                break;
            case 3:
                buffer += "=";
                break;
        }
        try {
            var bytes = Convert.FromBase64String(buffer);
            return Encoding.UTF8.GetString(bytes);
        } catch {
            return null;
        }
    }
}

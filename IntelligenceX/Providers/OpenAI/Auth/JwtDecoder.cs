using System;
using System.Text;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Auth;

internal static class JwtDecoder {
    public static string? TryGetAccountId(string accessToken) {
        var authObj = TryGetOpenAiAuthObject(accessToken);
        if (authObj is null) {
            return null;
        }
        var accountId = authObj.GetString(OpenAICodexDefaults.AccountIdClaim);
        return string.IsNullOrWhiteSpace(accountId) ? null : accountId;
    }

    public static string? TryGetEmail(string token) {
        var payload = TryGetPayloadObject(token);
        if (payload is null) {
            return null;
        }

        var directEmail = NormalizeOptional(payload.GetString("email"));
        if (!string.IsNullOrWhiteSpace(directEmail)) {
            return directEmail;
        }

        return NormalizeOptional(payload.GetObject("https://api.openai.com/profile")?.GetString("email"));
    }

    public static string? TryGetPlanType(string accessToken) {
        var authObj = TryGetOpenAiAuthObject(accessToken);
        return NormalizeOptional(authObj?.GetString("chatgpt_plan_type"));
    }

    private static JsonObject? TryGetOpenAiAuthObject(string token) {
        var obj = TryGetPayloadObject(token);
        if (obj is null || !obj.TryGetValue(OpenAICodexDefaults.AuthClaim, out var authNode)) {
            return null;
        }

        return authNode?.AsObject();
    }

    private static JsonObject? TryGetPayloadObject(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length < 2) {
            return null;
        }

        var payloadJson = TryDecodeBase64Url(parts[1]);
        if (payloadJson is null) {
            return null;
        }

        return JsonLite.Parse(payloadJson)?.AsObject();
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

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Serializes and deserializes <see cref="AuthBundle"/> instances.
/// </summary>
public static class AuthBundleSerializer {
    /// <summary>Serializes an auth bundle to JSON.</summary>
    /// <param name="bundle">The bundle to serialize.</param>
    public static string Serialize(AuthBundle bundle) {
        var obj = ToJson(bundle);
        return JsonLite.Serialize(JsonValue.From(obj));
    }

    internal static string SerializeFile(AuthBundleFile file) {
        var obj = new JsonObject()
            .Add("version", file.Version)
            .Add("bundles", ToBundlesJson(file));
        return JsonLite.Serialize(JsonValue.From(obj));
    }

    /// <summary>Deserializes an auth bundle from JSON.</summary>
    /// <param name="json">The JSON payload.</param>
    public static AuthBundle? Deserialize(string json) {
        var value = JsonLite.Parse(json);
        var obj = value?.AsObject();
        if (obj is null) {
            return null;
        }
        return FromJson(obj);
    }

    internal static AuthBundleFile? DeserializeFile(string json) {
        var value = JsonLite.Parse(json);
        var obj = value?.AsObject();
        if (obj is null) {
            return null;
        }
        var version = obj.GetInt64("version") ?? 1;
        var bundlesObj = obj.GetObject("bundles");
        if (bundlesObj is null) {
            return new AuthBundleFile((int)version, new Dictionary<string, AuthBundle>());
        }
        var bundles = new Dictionary<string, AuthBundle>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in bundlesObj) {
            var bundleObj = entry.Value?.AsObject();
            if (bundleObj is null) {
                continue;
            }
            var bundle = FromJson(bundleObj);
            if (bundle is null) {
                continue;
            }
            bundles[entry.Key] = bundle;
        }
        return new AuthBundleFile((int)version, bundles);
    }

    private static JsonObject ToJson(AuthBundle bundle) {
        var obj = new JsonObject()
            .Add("provider", bundle.Provider)
            .Add("access_token", bundle.AccessToken)
            .Add("refresh_token", bundle.RefreshToken);

        if (bundle.ExpiresAt.HasValue) {
            obj.Add("expires_at", bundle.ExpiresAt.Value.ToUnixTimeMilliseconds());
        }
        if (!string.IsNullOrWhiteSpace(bundle.TokenType)) {
            obj.Add("token_type", bundle.TokenType);
        }
        if (!string.IsNullOrWhiteSpace(bundle.Scope)) {
            obj.Add("scope", bundle.Scope);
        }
        if (!string.IsNullOrWhiteSpace(bundle.AccountId)) {
            obj.Add("account_id", bundle.AccountId);
        }
        if (!string.IsNullOrWhiteSpace(bundle.IdToken)) {
            obj.Add("id_token", bundle.IdToken);
        }
        return obj;
    }

    private static JsonObject ToBundlesJson(AuthBundleFile file) {
        var bundlesObj = new JsonObject();
        foreach (var entry in file.Bundles) {
            bundlesObj.Add(entry.Key, ToJson(entry.Value));
        }
        return bundlesObj;
    }

    private static AuthBundle? FromJson(JsonObject obj) {
        var provider = obj.GetString("provider") ?? obj.GetString("Provider");
        var accessToken = obj.GetString("access_token") ?? obj.GetString("accessToken") ?? obj.GetString("access");
        var refreshToken = obj.GetString("refresh_token") ?? obj.GetString("refreshToken") ?? obj.GetString("refresh");
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(refreshToken)) {
            return null;
        }
        DateTimeOffset? expiresAt = null;
        var expiresAtValue = obj.GetInt64("expires_at") ?? obj.GetInt64("expiresAt") ?? obj.GetInt64("expires");
        if (expiresAtValue.HasValue && expiresAtValue.Value > 0) {
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expiresAtValue.Value);
        }
        var bundle = new AuthBundle(provider!, accessToken!, refreshToken!, expiresAt) {
            TokenType = obj.GetString("token_type") ?? obj.GetString("tokenType"),
            Scope = obj.GetString("scope"),
            AccountId = obj.GetString("account_id") ?? obj.GetString("accountId"),
            IdToken = obj.GetString("id_token") ?? obj.GetString("idToken")
        };
        return bundle;
    }
}

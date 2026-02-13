using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Cli.Auth;

internal sealed record AuthStoreEntry(string Provider, string? AccountId, DateTimeOffset? ExpiresAt);

internal static class AuthStoreUtils {
    public static string DecryptAuthStoreIfNeeded(string content, string? authKeyBase64 = null) {
        if (string.IsNullOrWhiteSpace(content)) {
            return content;
        }
        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith("{\"encrypted\":", StringComparison.OrdinalIgnoreCase)) {
            return content;
        }

        var keyBase64 = string.IsNullOrWhiteSpace(authKeyBase64)
            ? Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY")
            : authKeyBase64;
        if (string.IsNullOrWhiteSpace(keyBase64)) {
            throw new InvalidOperationException("Auth store is encrypted but INTELLIGENCEX_AUTH_KEY is not set.");
        }
        byte[] key;
        try {
            key = Convert.FromBase64String(keyBase64);
        } catch {
            throw new InvalidOperationException("INTELLIGENCEX_AUTH_KEY must be base64.");
        }
        if (key.Length != 32) {
            throw new InvalidOperationException("INTELLIGENCEX_AUTH_KEY must decode to 32 bytes.");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var nonce = Convert.FromBase64String(root.GetProperty("nonce").GetString() ?? string.Empty);
        var cipher = Convert.FromBase64String(root.GetProperty("ciphertext").GetString() ?? string.Empty);
        var tag = Convert.FromBase64String(root.GetProperty("tag").GetString() ?? string.Empty);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public static List<AuthStoreEntry> ParseAuthStoreEntries(string json) {
        var list = new List<AuthStoreEntry>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            return list;
        }
        if (root.TryGetProperty("bundles", out var bundles) && bundles.ValueKind == JsonValueKind.Object) {
            foreach (var prop in bundles.EnumerateObject()) {
                if (prop.Value.ValueKind != JsonValueKind.Object) {
                    continue;
                }
                var provider = prop.Value.TryGetProperty("provider", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(provider)) {
                    continue;
                }
                var accountId = prop.Value.TryGetProperty("account_id", out var a) ? a.GetString() : null;
                DateTimeOffset? expiresAt = null;
                if (prop.Value.TryGetProperty("expires_at", out var exp) && exp.ValueKind == JsonValueKind.Number &&
                    exp.TryGetInt64(out var expMs) && expMs > 0) {
                    expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expMs);
                }
                list.Add(new AuthStoreEntry(provider!, accountId, expiresAt));
            }
            return list;
        }

        // Single-bundle format (supported by reviewer env ingestion).
        if (root.TryGetProperty("provider", out var providerProp) && providerProp.ValueKind == JsonValueKind.String) {
            var provider = providerProp.GetString();
            var accountId = root.TryGetProperty("account_id", out var a) ? a.GetString() : null;
            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expires_at", out var exp) && exp.ValueKind == JsonValueKind.Number &&
                exp.TryGetInt64(out var expMs) && expMs > 0) {
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expMs);
            }
            if (!string.IsNullOrWhiteSpace(provider)) {
                list.Add(new AuthStoreEntry(provider!, accountId, expiresAt));
            }
        }
        return list;
    }
}

using System;
using System.IO;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Auth;

#pragma warning disable CS1591

public sealed class CodexAuthProfile {
    public CodexAuthProfile(string? accountId, string? email, string? planType) {
        AccountId = NormalizeOptional(accountId);
        Email = NormalizeOptional(email);
        PlanType = NormalizeOptional(planType);
    }

    public string? AccountId { get; }
    public string? Email { get; }
    public string? PlanType { get; }
    public string? AccountLabel => Email;

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// Utilities for working with Codex auth.json files.
/// </summary>
public static class CodexAuthStore {
    /// <summary>
    /// Resolves the Codex home directory, honoring CODEX_HOME when set.
    /// </summary>
    public static string ResolveCodexHome() {
        var overridePath = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(overridePath)) {
            return overridePath;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }
        return Path.Combine(home, ".codex");
    }

    /// <summary>
    /// Resolves the Codex auth.json path.
    /// </summary>
    /// <param name="codexHome">Optional override Codex home.</param>
    public static string ResolveAuthPath(string? codexHome = null) {
        var home = string.IsNullOrWhiteSpace(codexHome) ? ResolveCodexHome() : codexHome!;
        return Path.Combine(home, "auth.json");
    }

    /// <summary>
    /// Reads a best-effort Codex auth profile from auth.json.
    /// </summary>
    public static CodexAuthProfile? TryReadProfile(string authPath) {
        if (string.IsNullOrWhiteSpace(authPath) || !File.Exists(authPath)) {
            return null;
        }

        try {
            var root = JsonLite.Parse(File.ReadAllText(authPath)).AsObject();
            var tokens = root?.GetObject("tokens");
            var accessToken = NormalizeOptional(tokens?.GetString("access_token"));
            var idToken = NormalizeOptional(tokens?.GetString("id_token"));
            var accountId = NormalizeOptional(tokens?.GetString("account_id"))
                            ?? (accessToken is null ? null : JwtDecoder.TryGetAccountId(accessToken))
                            ?? (idToken is null ? null : JwtDecoder.TryGetAccountId(idToken));
            var email = NormalizeOptional(root?.GetString("email"))
                        ?? (idToken is null ? null : JwtDecoder.TryGetEmail(idToken))
                        ?? (accessToken is null ? null : JwtDecoder.TryGetEmail(accessToken));
            var planType = NormalizeOptional(root?.GetString("plan_type"))
                           ?? (accessToken is null ? null : JwtDecoder.TryGetPlanType(accessToken));
            if (accountId is null && email is null && planType is null) {
                return null;
            }

            return new CodexAuthProfile(accountId, email, planType);
        } catch {
            return null;
        }
    }

    /// <summary>
    /// Builds Codex auth.json content from an auth bundle.
    /// </summary>
    /// <param name="bundle">Auth bundle.</param>
    /// <param name="lastRefresh">Optional refresh timestamp.</param>
    /// <param name="openAiApiKey">Optional API key to embed.</param>
    public static string BuildAuthJson(AuthBundle bundle, DateTimeOffset? lastRefresh = null, string? openAiApiKey = null) {
        if (bundle is null) {
            throw new ArgumentNullException(nameof(bundle));
        }
        if (string.IsNullOrWhiteSpace(bundle.IdToken)) {
            throw new InvalidOperationException("Bundle is missing id_token. Re-run login to capture it.");
        }
        var tokens = new JsonObject()
            .Add("id_token", bundle.IdToken)
            .Add("access_token", bundle.AccessToken)
            .Add("refresh_token", bundle.RefreshToken);
        var accountId = bundle.AccountId ?? JwtDecoder.TryGetAccountId(bundle.AccessToken);
        if (!string.IsNullOrWhiteSpace(accountId)) {
            tokens.Add("account_id", accountId);
        }

        var root = new JsonObject();
        if (!string.IsNullOrWhiteSpace(openAiApiKey)) {
            root.Add("OPENAI_API_KEY", openAiApiKey);
        }
        root.Add("tokens", tokens);
        if (lastRefresh.HasValue) {
            root.Add("last_refresh", lastRefresh.Value.ToUniversalTime().ToString("O"));
        }

        return JsonLite.Serialize(JsonValue.From(root));
    }

    /// <summary>
    /// Writes Codex auth.json to disk.
    /// </summary>
    /// <param name="bundle">Auth bundle.</param>
    /// <param name="codexHome">Optional Codex home override.</param>
    /// <param name="lastRefresh">Optional refresh timestamp.</param>
    /// <param name="openAiApiKey">Optional API key to embed.</param>
    public static void WriteAuthJson(AuthBundle bundle, string? codexHome = null, DateTimeOffset? lastRefresh = null,
        string? openAiApiKey = null) {
        var path = ResolveAuthPath(codexHome);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        var content = BuildAuthJson(bundle, lastRefresh, openAiApiKey);
        File.WriteAllText(path, content);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

#pragma warning restore CS1591

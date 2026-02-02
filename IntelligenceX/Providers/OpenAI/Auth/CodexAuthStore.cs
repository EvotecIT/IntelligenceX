using System;
using System.IO;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Helpers for writing Codex-style auth bundles to disk.
/// </summary>
public static class CodexAuthStore {
    /// <summary>Resolves the Codex home directory.</summary>
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

    /// <summary>Resolves the auth.json path for the Codex home directory.</summary>
    /// <param name="codexHome">Optional Codex home override.</param>
    public static string ResolveAuthPath(string? codexHome = null) {
        var home = string.IsNullOrWhiteSpace(codexHome) ? ResolveCodexHome() : codexHome!;
        return Path.Combine(home, "auth.json");
    }

    /// <summary>Builds the auth.json content for the Codex store.</summary>
    /// <param name="bundle">The auth bundle to store.</param>
    /// <param name="lastRefresh">Optional last refresh timestamp.</param>
    /// <param name="openAiApiKey">Optional OpenAI API key.</param>
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

    /// <summary>Writes auth.json content to the Codex store location.</summary>
    /// <param name="bundle">The auth bundle to store.</param>
    /// <param name="codexHome">Optional Codex home override.</param>
    /// <param name="lastRefresh">Optional last refresh timestamp.</param>
    /// <param name="openAiApiKey">Optional OpenAI API key.</param>
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
}

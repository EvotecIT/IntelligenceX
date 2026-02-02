using System;
using System.IO;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Auth;

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
}

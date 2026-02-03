namespace IntelligenceX.Cli.Setup;

/// <summary>
/// Default values for IntelligenceX setup.
/// </summary>
public static class IntelligenceXDefaults {
    /// <summary>
    /// Default GitHub OAuth App Client ID for device flow authentication.
    /// This is the official IntelligenceX Review app.
    /// </summary>
    public const string GitHubClientId = "Iv23li0wcHDzWa25HKz3";

    /// <summary>
    /// Default GitHub App name shown during setup.
    /// </summary>
    public const string GitHubAppName = "IntelligenceX Review";

    /// <summary>
    /// URL to the IntelligenceX Review GitHub App.
    /// </summary>
    public const string GitHubAppUrl = "https://github.com/apps/intelligencex-review";

    /// <summary>
    /// Environment variable to override the default GitHub Client ID.
    /// </summary>
    public const string GitHubClientIdEnvVar = "INTELLIGENCEX_GITHUB_CLIENT_ID";

    /// <summary>
    /// Default OAuth scopes for GitHub authentication.
    /// </summary>
    public const string GitHubScopes = "repo workflow read:org";

    /// <summary>
    /// Default review provider.
    /// </summary>
    public const string DefaultProvider = "openai";

    /// <summary>
    /// Gets the effective GitHub Client ID, checking environment variable first.
    /// </summary>
    public static string GetEffectiveGitHubClientId() {
        var envValue = System.Environment.GetEnvironmentVariable(GitHubClientIdEnvVar);
        return string.IsNullOrWhiteSpace(envValue) ? GitHubClientId : envValue;
    }

    /// <summary>
    /// Returns true if the default Client ID is being used (not overridden).
    /// </summary>
    public static bool IsUsingDefaultClientId() {
        var envValue = System.Environment.GetEnvironmentVariable(GitHubClientIdEnvVar);
        return string.IsNullOrWhiteSpace(envValue);
    }
}

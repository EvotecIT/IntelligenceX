namespace IntelligenceX.Cli.Setup.Wizard;

internal enum GitHubAuthMode {
    /// <summary>
    /// Sign in with GitHub using the default IntelligenceX app (recommended).
    /// </summary>
    DefaultDeviceFlow,
    /// <summary>
    /// Create or use your own GitHub App for bot identity.
    /// </summary>
    AppInstallation,
    /// <summary>
    /// OAuth device flow with custom Client ID.
    /// </summary>
    CustomDeviceFlow,
    /// <summary>
    /// Manual personal access token.
    /// </summary>
    PersonalAccessToken
}

internal enum SetupScope {
    SingleRepo,
    MultipleRepos,
    Manual
}

internal enum ConfigMode {
    None,
    Preset,
    Existing,
    CustomJson
}

internal enum ConfigSource {
    Editor,
    Path,
    Paste
}

internal enum WizardOperation {
    Setup,
    UpdateSecret,
    Cleanup
}

internal enum SecretTarget {
    Repo,
    Org
}

internal enum ConfigPreset {
    Balanced,
    Strict,
    Security,
    Minimal,
    Performance,
    Tests
}

internal sealed class WizardState {
    public string? RepoFullName { get; set; }
    public List<string> SelectedRepos { get; } = new();
    public string? GitHubClientId { get; set; }
    public string? GitHubToken { get; set; }
    public string? AuthBundlePath { get; set; }
    public long? GitHubAppId { get; set; }
    public string? GitHubAppKeyPem { get; set; }
    public string? GitHubAppKeyPath { get; set; }
    public long? GitHubInstallationId { get; set; }
    public bool WithConfig { get; set; }
    public bool SkipSecret { get; set; }
    public bool ManualSecret { get; set; }
    public bool ExplicitSecrets { get; set; }
    public bool KeepSecret { get; set; }
    public bool DryRun { get; set; }
    public string? BranchName { get; set; }
    public string Provider { get; set; } = "openai";
    public SecretTarget SecretTarget { get; set; } = SecretTarget.Repo;
    public string? SecretOrg { get; set; }
    public string SecretVisibility { get; set; } = "all";
    public bool DeleteRepoSecretsAfterOrgSecret { get; set; } = true;
    public string? OpenAiAuthB64 { get; set; }
    public GitHubAuthMode AuthMode { get; set; } = GitHubAuthMode.DefaultDeviceFlow;
    public SetupScope Scope { get; set; } = SetupScope.SingleRepo;
    public ConfigMode ConfigMode { get; set; } = ConfigMode.Preset;
    public ConfigPreset Preset { get; set; } = ConfigPreset.Balanced;
    public string? ConfigPath { get; set; }
    public string? ConfigJson { get; set; }
    public string? ConfigSourceLabel { get; set; }
    public bool? AnalysisEnabled { get; set; } = true;
    public bool? AnalysisGateEnabled { get; set; } = false;
    public string? AnalysisPacks { get; set; }
    public string? AnalysisExportPath { get; set; }
    public bool Force { get; set; }
    public bool Upgrade { get; set; }
    public WizardOperation Operation { get; set; } = WizardOperation.Setup;
}

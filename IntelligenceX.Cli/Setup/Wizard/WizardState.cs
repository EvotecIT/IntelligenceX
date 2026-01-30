namespace IntelligenceX.Cli.Setup.Wizard;

internal enum GitHubAuthMode {
    AppInstallation,
    DeviceFlow,
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
    public GitHubAuthMode AuthMode { get; set; } = GitHubAuthMode.DeviceFlow;
    public SetupScope Scope { get; set; } = SetupScope.SingleRepo;
    public ConfigMode ConfigMode { get; set; } = ConfigMode.Preset;
    public ConfigPreset Preset { get; set; } = ConfigPreset.Balanced;
    public string? ConfigPath { get; set; }
    public string? ConfigJson { get; set; }
    public bool Force { get; set; }
    public bool Upgrade { get; set; }
    public WizardOperation Operation { get; set; } = WizardOperation.Setup;
}

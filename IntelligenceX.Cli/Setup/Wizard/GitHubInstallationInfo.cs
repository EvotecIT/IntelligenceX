namespace IntelligenceX.Cli.Setup.Wizard;

internal sealed class GitHubInstallationInfo {
    public GitHubInstallationInfo(long id, string accountLogin) {
        Id = id;
        AccountLogin = accountLogin;
    }

    public long Id { get; }
    public string AccountLogin { get; }
}

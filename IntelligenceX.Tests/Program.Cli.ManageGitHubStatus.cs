namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestManageGitHubCliStatusWithTokenIsAuthenticated() {
        var status = global::IntelligenceX.Cli.Program.EvaluateGitHubCliStatus("token-value", authStatusExitCode: 1);
        AssertEqual(true, status.Installed, "github status token installed");
        AssertEqual(true, status.Authenticated, "github status token authenticated");
    }

    private static void TestManageGitHubCliStatusExitCodeZeroAuthenticated() {
        var status = global::IntelligenceX.Cli.Program.EvaluateGitHubCliStatus(null, authStatusExitCode: 0);
        AssertEqual(true, status.Installed, "github status exit0 installed");
        AssertEqual(true, status.Authenticated, "github status exit0 authenticated");
    }

    private static void TestManageGitHubCliStatusExitCodeNonZeroUnauthenticated() {
        var status = global::IntelligenceX.Cli.Program.EvaluateGitHubCliStatus(null, authStatusExitCode: 1);
        AssertEqual(true, status.Installed, "github status exit1 installed");
        AssertEqual(false, status.Authenticated, "github status exit1 authenticated");
    }

    private static void TestManageGitHubCliStatusMissingCli() {
        var status = global::IntelligenceX.Cli.Program.EvaluateGitHubCliStatus(null, authStatusExitCode: int.MinValue);
        AssertEqual(false, status.Installed, "github status missing installed");
        AssertEqual(false, status.Authenticated, "github status missing authenticated");
    }
#endif
}

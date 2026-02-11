namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestWebSetupBuildSetupArgsPropagatesRequestDryRun() {
        var fromRequest = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForDryRunPropagationTests(
            routeDryRun: false,
            requestDryRun: true);
        AssertEqual(true, Array.IndexOf(fromRequest, "--dry-run") >= 0, "web setup args request dry-run");

        var fromRoute = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForDryRunPropagationTests(
            routeDryRun: true,
            requestDryRun: false);
        AssertEqual(true, Array.IndexOf(fromRoute, "--dry-run") >= 0, "web setup args route dry-run");

        var none = IntelligenceX.Cli.Setup.Web.WebApi.BuildSetupArgsForDryRunPropagationTests(
            routeDryRun: false,
            requestDryRun: false);
        AssertEqual(false, Array.IndexOf(none, "--dry-run") >= 0, "web setup args no dry-run");
    }

    private static void TestWebSetupResolveWithConfigFromArgs() {
        AssertEqual(true, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo", "--with-config"), "web setup resolve with-config flag");
        AssertEqual(true, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo", "--config-path", ".intelligencex/reviewer.json"), "web setup resolve with-config config-path");
        AssertEqual(true, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo", "--config-json", "{\"review\":{}}"), "web setup resolve with-config config-json");
        AssertEqual(false, IntelligenceX.Cli.Setup.Web.WebApi.ResolveWithConfigFromArgsForTests(
            "--repo", "owner/repo"), "web setup resolve with-config none");
    }

    private static void TestWebSetupRunProcessTimeoutReturnsPromptly() {
        var command = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(command)) {
            command = "dotnet";
        }

        var timer = Stopwatch.StartNew();
        var result = IntelligenceX.Cli.Setup.Web.WebApi.RunSetupProcessForTests(
            command!,
            new[] { "--help" },
            timeoutMs: 1).GetAwaiter().GetResult();
        timer.Stop();

        AssertEqual(true, result.TimedOut, "web setup timeout flag");
        AssertEqual(124, result.ExitCode, "web setup timeout exit code");
        AssertContainsText(result.StdErr, "timed out", "web setup timeout stderr");
        if (timer.Elapsed > TimeSpan.FromSeconds(10)) {
            throw new InvalidOperationException($"Expected timeout test to return promptly, got {timer.Elapsed}.");
        }
    }
#endif
}

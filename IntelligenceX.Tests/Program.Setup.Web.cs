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

    private static void TestWebSetupPostApplyVerifySkipsCallbackWhenApplyFails() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            ExitSuccess = false
        };

        var verifyCalls = 0;
        var verify = IntelligenceX.Cli.Setup.Web.WebApi.ResolvePostApplyVerificationForTests(
            context,
            () => {
                verifyCalls++;
                return System.Threading.Tasks.Task.FromResult(new SetupPostApplyVerification {
                    Repo = "owner/repo",
                    Operation = "setup",
                    Passed = true
                });
            }).GetAwaiter().GetResult();

        AssertEqual(0, verifyCalls, "web setup post-apply verify callback skipped on failed apply");
        AssertEqual(true, verify.Skipped, "web setup post-apply verify skipped on failed apply");
        AssertEqual(false, verify.Passed, "web setup post-apply verify failed status on failed apply");
        AssertContainsText(verify.Note ?? string.Empty, "failed", "web setup post-apply verify failure note");
    }

    private static void TestWebSetupResolveOrgSecretVerificationContext() {
        var setupWithOrg = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            secretTarget: "org",
            secretOrg: null);
        AssertEqual(true, setupWithOrg.ExpectOrgSecret, "web setup org secret expected for org target without explicit org");
        AssertEqual(null, setupWithOrg.SecretOrg, "web setup org secret remains null when not provided");

        var updateWithOrg = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: true,
            provider: "chatgpt",
            secretTarget: "org",
            secretOrg: "EvotecIT");
        AssertEqual(true, updateWithOrg.ExpectOrgSecret, "web update-secret org secret expected for org target");
        AssertEqual("EvotecIT", updateWithOrg.SecretOrg, "web update-secret org secret value");

        var repoTarget = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: false,
            provider: "openai",
            secretTarget: "repo",
            secretOrg: "EvotecIT");
        AssertEqual(false, repoTarget.ExpectOrgSecret, "web setup repo target does not expect org secret");
        AssertEqual(null, repoTarget.SecretOrg, "web setup repo target does not pass org secret");

        var nonOpenAiProvider = IntelligenceX.Cli.Setup.Web.WebApi.ResolveOrgSecretVerificationContextForTests(
            cleanup: false,
            updateSecret: false,
            provider: "copilot",
            secretTarget: "org",
            secretOrg: "EvotecIT");
        AssertEqual(false, nonOpenAiProvider.ExpectOrgSecret, "web setup org target non-openai provider does not expect org secret");
        AssertEqual(null, nonOpenAiProvider.SecretOrg, "web setup org target non-openai provider does not pass org secret");
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

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestSetupPostApplyVerifySetupPassesWithManagedWorkflowAndSecret() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/12",
            PullRequestUrl = "https://github.com/owner/repo/pull/12"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-setup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = true,
            WorkflowManaged = true,
            ConfigExists = true,
            RepoSecretLookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult.Present()
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(false, verify.Skipped, "post-apply setup verify skipped");
        AssertEqual(true, verify.Passed, "post-apply setup verify passed");
        AssertEqual("pull-request", verify.CheckedRefSource, "post-apply setup verify ref source");
        AssertEqual("intelligencex-setup/20260211", verify.CheckedRef, "post-apply setup verify ref");
    }

    private static void TestSetupPostApplyVerifyCleanupDetectsResidualConfig() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Cleanup,
            KeepSecret = false,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Cleanup complete. PR created: https://github.com/owner/repo/pull/21",
            PullRequestUrl = "https://github.com/owner/repo/pull/21"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-cleanup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = false,
            WorkflowManaged = false,
            ConfigExists = true,
            RepoSecretLookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult.Missing()
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(false, verify.Passed, "post-apply cleanup verify failed");

        var configCheck = verify.Checks.Find(check => check.Name == "Reviewer config");
        AssertNotNull(configCheck, "post-apply cleanup config check exists");
        AssertEqual(false, configCheck!.Passed, "post-apply cleanup config check failed");
    }

    private static void TestSetupPostApplyVerifySetupAllowsUnknownBranchStateWithPr() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            SkipSecret = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/13",
            PullRequestUrl = "https://github.com/owner/repo/pull/13"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = null,
            CheckRefSource = "pull-request",
            WorkflowExists = null,
            WorkflowManaged = null,
            ConfigExists = null,
            RepoSecretLookup = null
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(true, verify.Passed, "post-apply setup verify passes when PR exists and branch state is unknown");
        AssertEqual(true, verify.Checks.Exists(check => check.Name == "Workflow" && check.Skipped),
            "post-apply setup workflow check skipped when branch state unknown");
    }

    private static void TestSetupPostApplyVerifySecretLookupUnauthorizedFailsDeterministically() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.UpdateSecret,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Secret updated: INTELLIGENCEX_AUTH_B64"
        };
        var observed = new SetupPostApplyObservedState {
            RepoSecretLookup = IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.SecretLookupResult.Unauthorized(
                "GitHub API returned 401 Unauthorized.")
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        AssertEqual(false, verify.Passed, "post-apply unauthorized secret lookup fails");
        var secretCheck = verify.Checks.Find(check => check.Name == "Repo secret");
        AssertNotNull(secretCheck, "post-apply unauthorized secret check exists");
        AssertEqual(false, secretCheck!.Skipped, "post-apply unauthorized secret check not skipped");
        AssertEqual(false, secretCheck.Passed, "post-apply unauthorized secret check failed");
        AssertEqual("unauthorized", secretCheck.Actual, "post-apply unauthorized secret check actual");
    }

    private static void TestSetupPostApplyVerifyIncludesLatestWorkflowRunLink() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            SkipSecret = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/22",
            PullRequestUrl = "https://github.com/owner/repo/pull/22"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-setup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = true,
            WorkflowManaged = true,
            ConfigExists = true,
            LatestWorkflowRun = new IntelligenceX.Cli.Setup.Wizard.GitHubRepoClient.WorkflowRunInfo(
                id: 120,
                url: "https://github.com/owner/repo/actions/runs/120",
                status: "completed",
                conclusion: "success",
                headBranch: "main",
                @event: "pull_request",
                createdAt: DateTimeOffset.Parse("2026-02-11T19:45:00Z", System.Globalization.CultureInfo.InvariantCulture))
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        var latestRun = verify.Checks.Find(check => check.Name == "Latest workflow run");
        AssertNotNull(latestRun, "post-apply latest workflow run check exists");
        AssertEqual(false, latestRun!.Skipped, "post-apply latest workflow run check not skipped");
        AssertEqual(true, latestRun.Passed, "post-apply latest workflow run check passed");
        AssertContainsText(latestRun.Note ?? string.Empty, "actions/runs/120", "post-apply latest workflow run note includes url");
    }

    private static void TestSetupPostApplyVerifyWorkflowRunLookupFailureIsNotReportedAsNone() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            WithConfig = true,
            SkipSecret = true,
            Provider = "openai",
            ExitSuccess = true,
            Output = "Setup complete. PR created: https://github.com/owner/repo/pull/22",
            PullRequestUrl = "https://github.com/owner/repo/pull/22"
        };
        var observed = new SetupPostApplyObservedState {
            DefaultBranch = "main",
            CheckRef = "intelligencex-setup/20260211",
            CheckRefSource = "pull-request",
            WorkflowExists = true,
            WorkflowManaged = true,
            ConfigExists = true,
            WorkflowRunLookupStatus = "forbidden",
            WorkflowRunLookupNote = "GitHub API returned 403 Forbidden."
        };

        var verify = SetupPostApplyVerifier.EvaluateForTests(context, observed);
        var latestRun = verify.Checks.Find(check => check.Name == "Latest workflow run");
        AssertNotNull(latestRun, "post-apply latest workflow run failure check exists");
        AssertEqual(true, latestRun!.Skipped, "post-apply latest workflow run failure check skipped");
        AssertEqual("forbidden", latestRun.Actual, "post-apply latest workflow run failure status");
        AssertContainsText(latestRun.Note ?? string.Empty, "403", "post-apply latest workflow run failure note");
    }

    private static void TestSetupPostApplyVerifyDoesNotSwallowUnexpectedWorkflowLookupExceptions() {
        using var client = CreateGitHubRepoClientForTests((_, _) => throw new NullReferenceException("boom"));
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.UpdateSecret,
            Provider = "copilot",
            ExitSuccess = true,
            Output = "Secret updated."
        };

        AssertThrows<NullReferenceException>(() =>
                SetupPostApplyVerifier.VerifyAsync(client, context).GetAwaiter().GetResult(),
            "post-apply verify unexpected workflow lookup exception");
    }

    private static void TestWizardPostApplyVerifySkipsCallbackWhenApplyFails() {
        var context = new SetupPostApplyContext {
            Repo = "owner/repo",
            Operation = SetupApplyOperation.Setup,
            ExitSuccess = false
        };

        var verifyCalls = 0;
        var verify = IntelligenceX.Cli.Setup.Wizard.WizardRunner.ResolvePostApplyVerificationForTests(
            context,
            () => {
                verifyCalls++;
                return System.Threading.Tasks.Task.FromResult(new SetupPostApplyVerification {
                    Repo = "owner/repo",
                    Operation = "setup",
                    Passed = true
                });
            }).GetAwaiter().GetResult();

        AssertEqual(0, verifyCalls, "wizard post-apply verify callback skipped on failed apply");
        AssertEqual(true, verify.Skipped, "wizard post-apply verify skipped on failed apply");
        AssertEqual(false, verify.Passed, "wizard post-apply verify failed status on failed apply");
        AssertContainsText(verify.Note ?? string.Empty, "failed", "wizard post-apply verify failure note");
    }

#endif
}

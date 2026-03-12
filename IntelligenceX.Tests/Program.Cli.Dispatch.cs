namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static readonly object CliDispatchConsoleLock = new();

    private static void TestCliDispatchNoArgsInteractiveRunsManage() {
        string[]? forwardedArgs = null;
        var exit = global::IntelligenceX.Cli.Program.DispatchAsync(
                Array.Empty<string>(),
                () => true,
                args => {
                    forwardedArgs = args;
                    return Task.FromResult(0);
                })
            .GetAwaiter()
            .GetResult();

        AssertEqual(0, exit, "dispatch no-args interactive exit");
        AssertNotNull(forwardedArgs, "dispatch no-args interactive forwarded args");
        AssertEqual(0, forwardedArgs!.Length, "dispatch no-args interactive forwarded args length");
    }

    private static void TestCliDispatchNoArgsNonInteractiveShowsHelp() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            Array.Empty<string>(),
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(1, exit, "dispatch no-args non-interactive exit");
        AssertContainsText(stdout, "Usage:", "dispatch no-args non-interactive help output");
        AssertContainsText(stdout, "intelligencex heatmap <usage|chatgpt|github>", "dispatch no-args non-interactive heatmap usage");
        AssertEqual(string.Empty, stderr, "dispatch no-args non-interactive stderr");
    }

    private static void TestCliDispatchManageCommandRoutesToManage() {
        string[]? forwardedArgs = null;
        var exit = global::IntelligenceX.Cli.Program.DispatchAsync(
                new[] { "manage", "--help", "--foo" },
                () => false,
                args => {
                    forwardedArgs = args;
                    return Task.FromResult(7);
                })
            .GetAwaiter()
            .GetResult();

        AssertEqual(7, exit, "dispatch manage command exit");
        AssertNotNull(forwardedArgs, "dispatch manage command forwarded args");
        AssertSequenceEqual(new[] { "--help", "--foo" }, forwardedArgs!, "dispatch manage command forwarded args");
    }

    private static void TestCliDispatchNoArgsManageFailureShowsFallbackError() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            Array.Empty<string>(),
            () => true,
            _ => throw new InvalidOperationException("boom"));

        AssertEqual(2, exit, "dispatch no-args manage failure exit");
        AssertContainsText(stderr, "Failed to launch management hub.", "dispatch no-args manage failure stderr");
        AssertContainsText(stderr, "INTELLIGENCEX_DEBUG=1", "dispatch no-args manage failure debug hint");
        if (stderr.IndexOf("boom", StringComparison.Ordinal) >= 0) {
            throw new InvalidOperationException("Expected no-args manage failure stderr to hide raw exception details by default.");
        }
        AssertContainsText(stdout, "Usage:", "dispatch no-args manage failure help output");
    }

    private static void TestCliDispatchManageCommandFailureShowsFallbackError() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "manage" },
            () => false,
            _ => throw new InvalidOperationException("boom"));

        AssertEqual(2, exit, "dispatch manage command failure exit");
        AssertContainsText(stderr, "Failed to launch management hub.", "dispatch manage command failure stderr");
        AssertContainsText(stderr, "INTELLIGENCEX_DEBUG=1", "dispatch manage command failure debug hint");
        if (stderr.IndexOf("boom", StringComparison.Ordinal) >= 0) {
            throw new InvalidOperationException("Expected manage command failure stderr to hide raw exception details by default.");
        }
        AssertContainsText(stdout, "Usage:", "dispatch manage command failure help output");
    }

    private static void TestCliDispatchManageCommandUnexpectedFailureShowsFallbackError() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "manage" },
            () => false,
            _ => throw new Exception("unexpected"));

        AssertEqual(2, exit, "dispatch manage command unexpected failure exit");
        AssertContainsText(stderr, "Failed to launch management hub.", "dispatch manage command unexpected failure stderr");
        AssertContainsText(stderr, "INTELLIGENCEX_DEBUG=1", "dispatch manage command unexpected failure debug hint");
        if (stderr.IndexOf("unexpected", StringComparison.Ordinal) >= 0) {
            throw new InvalidOperationException("Expected unexpected manage failure stderr to hide raw exception details by default.");
        }
        AssertContainsText(stdout, "Usage:", "dispatch manage command unexpected failure help output");
    }

    private static void TestCliDispatchDetailedErrorFlagParsing() {
        var previousDebug = Environment.GetEnvironmentVariable("INTELLIGENCEX_DEBUG");
        var previousVerbose = Environment.GetEnvironmentVariable("INTELLIGENCEX_VERBOSE");
        try {
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "1", true);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "true", true);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "YES", true);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "on", true);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "0", false);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "false", false);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "no", false);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_DEBUG", "off", false);

            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", null);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_VERBOSE", "1", true);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_VERBOSE", "yes", true);
            AssertShouldShowDetailedErrorsFor("INTELLIGENCEX_VERBOSE", "0", false);

            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", "on");
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", "0");
            AssertEqual(true, global::IntelligenceX.Cli.Program.ShouldShowDetailedErrors(), "dispatch detailed errors: debug overrides verbose falsy");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", previousDebug);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", previousVerbose);
        }
    }

    private static void TestCliAuthSyncCodexHelpSupportsOptions() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "auth", "sync-codex", "--help" },
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(0, exit, "auth sync-codex help exit");
        AssertContainsText(stdout, "--provider", "auth sync-codex help provider option");
        AssertContainsText(stdout, "--account-id", "auth sync-codex help account option");
        AssertEqual(string.Empty, stderr, "auth sync-codex help stderr");
    }

    private static void TestCliAuthSyncCodexMissingProviderValueShowsHelp() {
        var (exit, stdout, stderr) = RunCliDispatchWithCapturedOutput(
            new[] { "auth", "sync-codex", "--provider" },
            () => false,
            _ => Task.FromResult(0));

        AssertEqual(1, exit, "auth sync-codex missing provider exit");
        AssertContainsText(stderr, "Missing value for --provider.", "auth sync-codex missing provider stderr");
        AssertContainsText(stdout, "Usage:", "auth sync-codex missing provider help");
    }

    private static void TestCliModelsHelpRoutes() {
        var (exitRoot, stdoutRoot, stderrRoot) = RunCliDispatchWithCapturedOutput(
            new[] { "models", "--help" },
            () => false,
            _ => Task.FromResult(0));
        AssertEqual(0, exitRoot, "models help exit");
        AssertContainsText(stdoutRoot, "intelligencex models list", "models root help usage");
        AssertContainsText(stdoutRoot, "--account-id", "models root help account option");
        AssertEqual(string.Empty, stderrRoot, "models root help stderr");

        var (exitList, stdoutList, stderrList) = RunCliDispatchWithCapturedOutput(
            new[] { "models", "list", "--help" },
            () => false,
            _ => Task.FromResult(0));
        AssertEqual(0, exitList, "models list help exit");
        AssertContainsText(stdoutList, "--model-url", "models list help model-url option");
        AssertEqual(string.Empty, stderrList, "models list help stderr");
    }

    private static void TestResolveDefaultRepoNormalizesEnvironmentValue() {
        var previousRepo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO");
        var previousGitHubRepo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO", "owner/repository/");
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", null);
            AssertEqual("owner/repository", global::IntelligenceX.Cli.Program.ResolveDefaultRepo(), "resolve default repo normalizes env value");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO", previousRepo);
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", previousGitHubRepo);
        }
    }

    private static void AssertShouldShowDetailedErrorsFor(string envVar, string? value, bool expected) {
        var previousDebug = Environment.GetEnvironmentVariable("INTELLIGENCEX_DEBUG");
        var previousVerbose = Environment.GetEnvironmentVariable("INTELLIGENCEX_VERBOSE");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", null);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", null);
            Environment.SetEnvironmentVariable(envVar, value);
            AssertEqual(expected, global::IntelligenceX.Cli.Program.ShouldShowDetailedErrors(), $"dispatch detailed errors: {envVar}={value ?? "<null>"}");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", previousDebug);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", previousVerbose);
        }
    }

    private static (int ExitCode, string StdOut, string StdErr) RunCliDispatchWithCapturedOutput(
        string[] args,
        Func<bool> canLaunchManageHub,
        Func<string[], Task<int>> runManageAsync) {
        lock (CliDispatchConsoleLock) {
            var originalOut = Console.Out;
            var originalErr = Console.Error;
            using var outWriter = new StringWriter();
            using var errWriter = new StringWriter();
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            try {
                var exit = global::IntelligenceX.Cli.Program.DispatchAsync(args, canLaunchManageHub, runManageAsync)
                    .GetAwaiter()
                    .GetResult();
                return (exit, outWriter.ToString(), errWriter.ToString());
            } finally {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
    }
#endif
}

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

        AssertEqual(1, exit, "dispatch no-args manage failure exit");
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

        AssertEqual(1, exit, "dispatch manage command failure exit");
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

        AssertEqual(1, exit, "dispatch manage command unexpected failure exit");
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
            AssertEqual(true, InvokeShouldShowDetailedErrors(), "dispatch detailed errors: debug overrides verbose falsy");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", previousDebug);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", previousVerbose);
        }
    }

    private static void AssertShouldShowDetailedErrorsFor(string envVar, string? value, bool expected) {
        var previousDebug = Environment.GetEnvironmentVariable("INTELLIGENCEX_DEBUG");
        var previousVerbose = Environment.GetEnvironmentVariable("INTELLIGENCEX_VERBOSE");
        try {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", null);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", null);
            Environment.SetEnvironmentVariable(envVar, value);
            AssertEqual(expected, InvokeShouldShowDetailedErrors(), $"dispatch detailed errors: {envVar}={value ?? "<null>"}");
        } finally {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_DEBUG", previousDebug);
            Environment.SetEnvironmentVariable("INTELLIGENCEX_VERBOSE", previousVerbose);
        }
    }

    private static bool InvokeShouldShowDetailedErrors() {
        var flags = global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static;
        var method = typeof(global::IntelligenceX.Cli.Program).GetMethod("ShouldShowDetailedErrors", flags);
        AssertNotNull(method, "dispatch detailed errors method lookup");
        return (bool)method!.Invoke(null, null)!;
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

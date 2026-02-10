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

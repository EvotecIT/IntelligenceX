namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
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

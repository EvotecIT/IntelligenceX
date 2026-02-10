namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestManageRunExternalCommandTimeoutReturnsPromptly() {
        var timer = Stopwatch.StartNew();
        var result = global::IntelligenceX.Cli.Program.RunExternalCommandForTests("dotnet", "--help", timeoutMs: 1);
        timer.Stop();

        if (result.ExitCode == int.MinValue) {
            throw new InvalidOperationException($"Expected dotnet command to start for timeout test, got error: {result.StdErr}");
        }
        AssertEqual(124, result.ExitCode, "manage external timeout exit code");
        AssertContainsText(result.StdErr, "timed out", "manage external timeout stderr");
        if (timer.Elapsed > TimeSpan.FromSeconds(5)) {
            throw new InvalidOperationException($"Expected timeout test to return promptly, got {timer.Elapsed}.");
        }
    }
#endif
}

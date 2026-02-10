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

    private static void TestManageRunExternalCommandCapturesHelpTailLine() {
        var cliPath = ResolveCliDllPathForTests();
        var args = $"\"{cliPath}\" manage --help";
        var result = global::IntelligenceX.Cli.Program.RunExternalCommandForTests("dotnet", args, timeoutMs: 10000);
        if (result.ExitCode == int.MinValue) {
            throw new InvalidOperationException($"Expected CLI help command to start, got error: {result.StdErr}");
        }
        AssertEqual(0, result.ExitCode, "manage external help exit code");
        AssertContainsText(result.StdOut, "IntelligenceX management hub", "manage external help title");
        AssertContainsText(result.StdOut, "  intelligencex", "manage external help final line");
    }

    private static void TestManageRunExternalCommandStartFailureReturnsPromptly() {
        var timer = Stopwatch.StartNew();
        var result = global::IntelligenceX.Cli.Program.RunExternalCommandForTests("intelligencex-this-command-does-not-exist", "", timeoutMs: 5000);
        timer.Stop();

        AssertEqual(int.MinValue, result.ExitCode, "manage external start failure exit code");
        if (timer.Elapsed > TimeSpan.FromSeconds(2)) {
            throw new InvalidOperationException($"Expected startup failure test to return promptly, got {timer.Elapsed}.");
        }
    }

    private static void TestManageRunExternalCommandNonTimeoutFailureIsNotTimeout() {
        var result = global::IntelligenceX.Cli.Program.RunExternalCommandForTests("dotnet", "--help", timeoutMs: -2);
        AssertEqual(int.MinValue, result.ExitCode, "manage external non-timeout failure exit code");
        if (result.StdErr.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) {
            throw new InvalidOperationException("Expected non-timeout external command failure to avoid timeout classification.");
        }
    }

    private static string ResolveCliDllPathForTests() {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var candidates = new[] {
            Path.Combine(root, "IntelligenceX.Cli", "bin", "Release", "net10.0", "IntelligenceX.Cli.dll"),
            Path.Combine(root, "IntelligenceX.Cli", "bin", "Release", "net8.0", "IntelligenceX.Cli.dll")
        };
        foreach (var candidate in candidates) {
            if (File.Exists(candidate)) {
                return candidate;
            }
        }
        throw new InvalidOperationException("Could not locate built IntelligenceX.Cli.dll for external command capture test.");
    }
#endif
}

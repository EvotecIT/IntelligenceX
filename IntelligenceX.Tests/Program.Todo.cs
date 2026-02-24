namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestTodoHelp() {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Todo.TodoRunner.RunAsync(new[] { "--help" }).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            var output = outWriter.ToString() + errWriter.ToString();
            AssertEqual(0, exitCode, "todo help exit");
            AssertContainsText(output, "TODO commands:", "todo help header");
            AssertContainsText(output, "sync-bot-feedback", "todo help includes command");
            AssertContainsText(output, "build-triage-index", "todo help includes triage command");
            AssertContainsText(output, "backtest-pr-signals", "todo help includes backtest command");
            AssertContainsText(output, "issue-review", "todo help includes issue review command");
            AssertContainsText(output, "vision-check", "todo help includes vision command");
            AssertContainsText(output, "project-init", "todo help includes project init command");
            AssertContainsText(output, "project-sync", "todo help includes project sync command");
            AssertContainsText(output, "project-bootstrap", "todo help includes project bootstrap command");
            AssertContainsText(output, "project-view-checklist", "todo help includes project view checklist command");
            AssertContainsText(output, "project-view-apply", "todo help includes project view apply command");
            AssertContainsText(output, "pr-watch", "todo help includes pr-watch command");
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
#endif
}

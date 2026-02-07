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
            AssertEqual(1, exitCode, "todo help exit");
            AssertContainsText(output, "TODO commands:", "todo help header");
            AssertContainsText(output, "sync-bot-feedback", "todo help includes command");
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
#endif
}


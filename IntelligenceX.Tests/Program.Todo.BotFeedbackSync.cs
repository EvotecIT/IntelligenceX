namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestTodoUnknownCommandShowsMessage() {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try {
            var exitCode = IntelligenceX.Cli.Todo.TodoRunner.RunAsync(new[] { "nope" }).GetAwaiter().GetResult();
            outWriter.Flush();
            errWriter.Flush();
            AssertEqual(1, exitCode, "todo unknown exit");
            AssertContainsText(errWriter.ToString(), "Unknown todo command:", "todo unknown stderr");
            AssertContainsText(outWriter.ToString(), "TODO commands:", "todo unknown help header");
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private static void TestBotFeedbackRenderHonorsLfNewlines() {
        var pr = new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.PrTasks(
            123,
            "Title",
            "https://example/pr/123",
            new[] {
                new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(false, "Do thing", string.Empty),
                new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(true, "Fix stuff", "https://link")
            });

        var text = IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.RenderPrBlock(pr, "\n");
        if (text.Contains("\r\n", StringComparison.Ordinal)) {
            throw new InvalidOperationException("Expected LF-only output.");
        }
        AssertContainsText(text, "<summary>PR #123 Title</summary>\n", "render summary");
        AssertContainsText(text, "- [ ] Do thing\n", "render unchecked");
        AssertContainsText(text, "- [x] Fix stuff. Links: https://link\n", "render checked");
    }

    private static void TestBotFeedbackParseExistingPrBlockExtractsTasks() {
        var section =
            "## Review Feedback Backlog (Bots)\n" +
            "<details>\n" +
            "<summary>PR #50 Something</summary>\n" +
            "\n" +
            "- [ ] Needs work. Links: https://example/a\n" +
            "- [x] Already fixed\n" +
            "</details>\n\n";

        var block = IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TryParseExistingPrBlock(section, 50);
        AssertNotNull(block, "existing block");
        AssertEqual(50, block!.Number, "existing block number");
        AssertEqual(2, block.Tasks.Count, "existing block tasks count");
        AssertEqual(false, block.Tasks[0].Checked, "existing task 0 checked");
        AssertEqual("Needs work", block.Tasks[0].Text, "existing task 0 text");
        AssertEqual("https://example/a", block.Tasks[0].Url, "existing task 0 url");
        AssertEqual(true, block.Tasks[1].Checked, "existing task 1 checked");
        AssertEqual("Already fixed", block.Tasks[1].Text, "existing task 1 text");
    }

    private static void TestBotFeedbackMergePreservesManualCheckedStateAndOrder() {
        var existing = new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.ExistingPrBlock(
            1,
            new[] {
                new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(true, "A", string.Empty),
                new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(false, "B", string.Empty)
            });

        var current = new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.PrTasks(
            1,
            "T",
            "U",
            new[] {
                new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(false, "A", "https://a"),
                new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(false, "C", string.Empty)
            });

        var merged = IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.MergeTasks(existing, current);
        AssertEqual(3, merged.Tasks.Count, "merged tasks count");
        AssertEqual("A", merged.Tasks[0].Text, "merged[0] text");
        AssertEqual(true, merged.Tasks[0].Checked, "merged[0] checked preserved");
        AssertEqual("https://a", merged.Tasks[0].Url, "merged[0] url filled");
        AssertEqual("B", merged.Tasks[1].Text, "merged[1] order");
        AssertEqual("C", merged.Tasks[2].Text, "merged[2] new task");
    }

    private static void TestBotFeedbackUpdateSectionIsDeterministicAndNoDuplicates() {
        var section =
            "## Review Feedback Backlog (Bots)\n" +
            "<details>\n" +
            "<summary>PR #1 One</summary>\n" +
            "\n" +
            "- [ ] Old item\n" +
            "</details>\n\n";

        var prs = new[] {
            new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.PrTasks(
                1,
                "One",
                "https://example/pr/1",
                new[] { new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(false, "New item", string.Empty) }),
            new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.PrTasks(
                2,
                "Two",
                "https://example/pr/2",
                new[] { new IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.TaskItem(false, "Task", string.Empty) })
        };

        var updated = IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.UpdateSection(section, prs, "\n", out var changed);
        AssertEqual(true, changed, "section changed");
        AssertEqual(2, CountOccurrences(updated, "<summary>PR #"), "PR blocks count");
        AssertContainsText(updated, "<summary>PR #1 One</summary>\n", "pr1 summary");
        AssertContainsText(updated, "- [ ] New item\n", "pr1 updated task");
        AssertContainsText(updated, "<summary>PR #2 Two</summary>\n", "pr2 inserted");
    }

    private static void TestBotFeedbackParseTasksUsesMergeBlockerSections() {
        var body = string.Join("\n", new[] {
            "## Summary",
            "- [ ] Non-blocking checklist item",
            "## Todo List ✅",
            "- [ ] Fix cancellation race",
            "## Critical Issues ⚠️ (if any)",
            "- [x] Already addressed"
        });
        var tasks = IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.ParseTasksForTests(body, "https://example/comment");
        AssertEqual(2, tasks.Count, "section tasks count");
        AssertEqual("Fix cancellation race", tasks[0].Text, "section task todo text");
        AssertEqual(false, tasks[0].Checked, "section task todo checked");
        AssertEqual("Already addressed", tasks[1].Text, "section task critical text");
        AssertEqual(true, tasks[1].Checked, "section task critical checked");
    }

    private static void TestBotFeedbackParseTasksLegacyFallbackWithoutHeaders() {
        var body = string.Join("\n", new[] {
            "- [ ] Fix cancellation race",
            "- [x] Already addressed"
        });
        var tasks = IntelligenceX.Cli.Todo.BotFeedbackSyncRunner.ParseTasksForTests(body, "https://example/comment");
        AssertEqual(2, tasks.Count, "legacy fallback task count");
        AssertEqual("Fix cancellation race", tasks[0].Text, "legacy fallback todo");
        AssertEqual(false, tasks[0].Checked, "legacy fallback todo checked");
        AssertEqual("Already addressed", tasks[1].Text, "legacy fallback checked task");
        AssertEqual(true, tasks[1].Checked, "legacy fallback checked state");
    }
#endif
}

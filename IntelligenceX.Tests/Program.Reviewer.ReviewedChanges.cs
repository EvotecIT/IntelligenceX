namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestReviewedChangesBlockSummarizesFileScope() {
        var files = new[] {
            new PullRequestFile("src/Foo.cs", "modified", string.Join("\n", new[] {
                "@@ -1,2 +1,3 @@",
                " public sealed class Foo {",
                "-    old();",
                "+    current();",
                "+    added();",
                " }"
            })),
            new PullRequestFile("assets/logo.png", "modified", null)
        };
        var promptFiles = new[] {
            new PullRequestFile(files[0].Filename, files[0].Status, files[0].Patch),
            new PullRequestFile(files[1].Filename, files[1].Status, null)
        };

        var block = ReviewReviewedChangesBuilder.BuildCommentBlock(files, promptFiles, reviewFailed: false);

        AssertContainsText(block, "## Reviewed Changes 📋", "reviewed changes heading");
        AssertContainsText(block, "| `src/Foo.cs` | Modified | +2/-1 across 1 hunk | diff patch |",
            "reviewed changes diff row");
        AssertContainsText(block, "| `assets/logo.png` | Modified | patch unavailable | path/status only |",
            "reviewed changes no patch row");
    }

    private static void TestReviewedChangesBlockMarksPromptOmissions() {
        var sourcePatch = string.Join("\n", new[] {
            "@@ -1,1 +1,2 @@",
            "-old();",
            "+new();",
            "+extra();"
        });
        var files = new[] {
            new PullRequestFile("src/Foo.cs", "modified", sourcePatch),
            new PullRequestFile("src/Bar.cs", "added", "@@ -0,0 +1,1 @@\n+bar();")
        };
        var promptFiles = new[] {
            new PullRequestFile("src/Foo.cs", "modified", "@@ -1,1 +1,2 @@\n-new();"),
            new PullRequestFile("src\\Foo.cs", "modified", "@@ -1,1 +1,2 @@\n-new();")
        };

        var block = ReviewReviewedChangesBuilder.BuildCommentBlock(files, promptFiles, reviewFailed: false);

        AssertContainsText(block, "| `src/Foo.cs` | Modified | +2/-1 across 1 hunk | trimmed diff patch |",
            "reviewed changes trimmed patch row");
        AssertContainsText(block, "| `src/Bar.cs` | Added | +1/-0 across 1 hunk | not included in prompt |",
            "reviewed changes omitted prompt row");
        AssertContainsText(block, "_1 selected file was outside the LLM prompt file budget._",
            "reviewed changes prompt budget note");
    }
}
#endif

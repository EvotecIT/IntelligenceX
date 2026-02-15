namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestProjectBootstrapRenderWorkflowTemplateInjectsProjectTarget() {
        const string template = """
owner={{Owner}}
project={{ProjectNumber}}
max={{MaxItems}}
""";

        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            321,
            750);

        AssertContainsText(rendered, "owner=EvotecIT", "owner token replaced");
        AssertContainsText(rendered, "project=321", "project token replaced");
        AssertContainsText(rendered, "max=750", "max items token replaced");
        AssertEqual(false, rendered.Contains("{{Owner}}", StringComparison.Ordinal), "owner placeholder removed");
        AssertEqual(false, rendered.Contains("{{ProjectNumber}}", StringComparison.Ordinal), "project placeholder removed");
        AssertEqual(false, rendered.Contains("{{MaxItems}}", StringComparison.Ordinal), "max items placeholder removed");
    }

    private static void TestProjectBootstrapRenderWorkflowTemplateClampsMaxItems() {
        const string template = "max={{MaxItems}}";

        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            123,
            0);

        AssertContainsText(rendered, "max=1", "max items clamped to minimum");
    }

    private static void TestProjectBootstrapRenderVisionTemplateInjectsContext() {
        const string template = """
repo={{Repo}}
owner={{Owner}}
project={{ProjectNumber}}
""";

        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderVisionTemplate(
            template,
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            654);

        AssertContainsText(rendered, "repo=EvotecIT/IntelligenceX", "repo token replaced");
        AssertContainsText(rendered, "owner=EvotecIT", "owner token replaced");
        AssertContainsText(rendered, "project=654", "project token replaced");
        AssertEqual(false, rendered.Contains("{{Repo}}", StringComparison.Ordinal), "repo placeholder removed");
        AssertEqual(false, rendered.Contains("{{Owner}}", StringComparison.Ordinal), "owner placeholder removed");
        AssertEqual(false, rendered.Contains("{{ProjectNumber}}", StringComparison.Ordinal), "project placeholder removed");
    }

    private static void TestProjectBootstrapWorkflowTemplateEnablesApplyLabels() {
        var template = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.LoadWorkflowTemplate();
        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            654,
            300);

        AssertContainsText(rendered, "todo project-sync", "workflow contains project sync step");
        AssertContainsText(rendered, "--apply-labels", "workflow enables label application");
        AssertContainsText(rendered, "--apply-link-comments", "workflow enables PR issue suggestion comments");
    }

    private static void TestProjectBootstrapWorkflowTemplateUpsertsControlIssueSummaryComment() {
        var template = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.LoadWorkflowTemplate();
        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            654,
            300);

        AssertContainsText(rendered, "intelligencex:triage-project-sync-summary", "workflow summary marker present");
        AssertContainsText(rendered, "issues/$ISSUE/comments", "workflow reads control issue comments");
        AssertContainsText(rendered, "--method PATCH", "workflow updates existing summary comment");
        AssertContainsText(rendered, "--method POST", "workflow creates summary comment when missing");
    }

    private static void TestProjectBootstrapBuildControlIssueBodyIncludesProjectContext() {
        var body = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.BuildControlIssueBody(
            "EvotecIT/IntelligenceX",
            "EvotecIT",
            789);

        AssertContainsText(body, "`EvotecIT/IntelligenceX`", "control issue body includes repo");
        AssertContainsText(body, "`EvotecIT#789`", "control issue body includes project target");
        AssertContainsText(body, "IX_TRIAGE_CONTROL_ISSUE", "control issue body includes variable name");
    }

    private static void TestProjectBootstrapParseIssueNumberFromGhOutputParsesIssueUrl() {
        var parsed = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.TryParseIssueNumberFromGhOutput(
            "https://github.com/EvotecIT/IntelligenceX/issues/456\n",
            out var issueNumber);

        AssertEqual(true, parsed, "issue number parsed from gh output");
        AssertEqual(456, issueNumber, "parsed issue number");
    }

    private static void TestProjectBootstrapParseIssueNumberFromGhOutputParsesTrailingInteger() {
        var parsed = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.TryParseIssueNumberFromGhOutput(
            "created issue #912",
            out var issueNumber);

        AssertEqual(true, parsed, "issue number parsed from trailing integer");
        AssertEqual(912, issueNumber, "parsed trailing issue number");
    }

    private static void TestProjectBootstrapRejectsConflictingControlIssueOptions() {
        var exitCode = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RunAsync(new[] {
            "--help",
            "--create-control-issue",
            "--control-issue", "42"
        }).GetAwaiter().GetResult();

        AssertEqual(1, exitCode, "conflicting control issue options should fail parse");
    }
#endif
}

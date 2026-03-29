namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestProjectBootstrapRenderWorkflowTemplateInjectsProjectTarget() {
        const string template = """
owner={{Owner}}
project={{ProjectNumber}}
max={{MaxItems}}
drift={{VisionDriftThreshold}}
""";

        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            321,
            750,
            0.85);

        AssertContainsText(rendered, "owner=EvotecIT", "owner token replaced");
        AssertContainsText(rendered, "project=321", "project token replaced");
        AssertContainsText(rendered, "max=750", "max items token replaced");
        AssertContainsText(rendered, "drift=0.85", "drift threshold token replaced");
        AssertEqual(false, rendered.Contains("{{Owner}}", StringComparison.Ordinal), "owner placeholder removed");
        AssertEqual(false, rendered.Contains("{{ProjectNumber}}", StringComparison.Ordinal), "project placeholder removed");
        AssertEqual(false, rendered.Contains("{{MaxItems}}", StringComparison.Ordinal), "max items placeholder removed");
        AssertEqual(false, rendered.Contains("{{VisionDriftThreshold}}", StringComparison.Ordinal), "drift threshold placeholder removed");
    }

    private static void TestProjectBootstrapRenderWorkflowTemplateClampsMaxItems() {
        const string template = "max={{MaxItems}}";

        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            123,
            0,
            0.70);

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

    private static void TestProjectBootstrapVisionTemplateIncludesStrictContractSections() {
        var template = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.LoadVisionTemplate();

        AssertContainsText(template, "## Goals", "vision template includes goals section");
        AssertContainsText(template, "## Non-Goals", "vision template includes non-goals section");
        AssertContainsText(template, "## In Scope", "vision template includes in-scope section");
        AssertContainsText(template, "## Out Of Scope", "vision template includes out-of-scope section");
        AssertContainsText(template, "## Decision Principles", "vision template includes decision principles section");
        AssertContainsText(template, "`aligned`:", "vision template includes aligned policy bullet");
        AssertContainsText(template, "`likely-out-of-scope`:", "vision template includes out-of-scope policy bullet");
        AssertContainsText(template, "`needs-human-review`:", "vision template includes review policy bullet");
    }

    private static void TestProjectBootstrapWorkflowTemplateEnablesApplyLabels() {
        var template = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.LoadWorkflowTemplate();
        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            654,
            300,
            0.70);

        AssertContainsText(rendered, "todo project-sync", "workflow contains project sync step");
        AssertContainsText(rendered, "todo issue-review", "workflow contains issue review step");
        AssertContainsText(rendered, "--issue-review artifacts/triage/ix-issue-review.json", "workflow passes issue review artifact to project sync");
        AssertContainsText(rendered, "--apply-labels", "workflow enables label application");
        AssertContainsText(rendered, "--apply-link-comments", "workflow enables PR issue suggestion comments");
        AssertContainsText(rendered, "apply_pr_watch_governance_labels", "workflow exposes governance label input");
        AssertContainsText(rendered, "IX_TRIAGE_APPLY_PR_WATCH_GOVERNANCE_LABELS", "workflow reads governance label repo variable");
        AssertContainsText(rendered, "--apply-pr-watch-governance-labels", "workflow can opt into governance label sync");
        AssertContainsText(rendered, "apply_pr_watch_governance_fields", "workflow exposes governance field input");
        AssertContainsText(rendered, "IX_TRIAGE_APPLY_PR_WATCH_GOVERNANCE_FIELDS", "workflow reads governance field repo variable");
        AssertContainsText(rendered, "--apply-pr-watch-governance-fields", "workflow can opt into governance field sync");
        AssertContainsText(rendered, "EXTRA_ARGS=()", "workflow uses shell arrays for optional sync args");
        AssertContainsText(rendered, "\"${EXTRA_ARGS[@]}\"", "workflow applies optional sync args via array expansion");
        AssertContainsText(rendered, "include_pr_watch_governance_views", "workflow exposes governance view input");
        AssertContainsText(rendered, "IX_TRIAGE_INCLUDE_PR_WATCH_GOVERNANCE_VIEWS", "workflow reads governance view repo variable");
        AssertContainsText(rendered, "--include-pr-watch-governance-views", "workflow can opt into governance view profile");
        AssertContainsText(rendered, "VIEW_ARGS=()", "workflow uses shell arrays for optional view args");
        AssertContainsText(rendered, "\"${VIEW_ARGS[@]}\"", "workflow applies optional view args via array expansion");
        AssertContainsText(rendered, "--enforce-contract", "workflow enforces vision contract");
        AssertContainsText(rendered, "--fail-on-drift", "workflow enables vision drift gate");
        AssertContainsText(rendered, "default: \"0.70\"", "workflow dispatch default includes drift threshold");
        AssertContainsText(rendered, "DRIFT_THRESHOLD=\"${{ github.event.inputs.drift_threshold }}\"", "workflow reads drift threshold input");
        AssertContainsText(rendered, "--drift-threshold \"$DRIFT_THRESHOLD\"", "workflow applies drift threshold input");
    }

    private static void TestProjectBootstrapWorkflowTemplateUpsertsControlIssueSummaryComment() {
        var template = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.LoadWorkflowTemplate();
        var rendered = IntelligenceX.Cli.Todo.ProjectBootstrapRunner.RenderWorkflowTemplate(
            template,
            "EvotecIT",
            654,
            300,
            0.70);

        AssertContainsText(rendered, "intelligencex:triage-project-sync-summary", "workflow summary marker present");
        AssertContainsText(rendered, "intelligencex:triage-control-dashboard", "workflow dashboard marker present");
        AssertContainsText(rendered, "IX_PROJECT_VIEW_APPLY_ISSUE", "workflow dashboard includes project-view issue variable");
        AssertContainsText(rendered, "artifacts/triage/ix-project-config.json", "workflow dashboard reads project config when present");
        AssertContainsText(rendered, "intelligencex:triage-bootstrap-links", "workflow dashboard links bootstrap marker when present");
        AssertContainsText(rendered, "issues/$ISSUE/comments", "workflow reads control issue comments");
        AssertContainsText(rendered, "--method PATCH", "workflow updates existing summary comment");
        AssertContainsText(rendered, "--method POST", "workflow creates summary comment when missing");
        AssertContainsText(rendered, "Latest Summaries", "workflow dashboard includes latest summaries section");
        AssertContainsText(rendered, "PR Babysit Governance", "workflow dashboard includes pr-watch governance section");
        AssertContainsText(rendered, "pr-watch-rollup-tracker:weekly-governance", "workflow dashboard looks for weekly governance tracker");
        AssertContainsText(rendered, "pr-watch-rollup-tracker:schedule", "workflow dashboard falls back to nightly schedule tracker");
        AssertContainsText(rendered, "Governance status:", "workflow dashboard includes governance status line");
        AssertContainsText(rendered, "Maintainer Quick Links", "workflow dashboard includes quick links section");
    }

    private static void TestTriageIndexWorkflowTemplateUpsertsControlIssueSummaryComment() {
        var templatePath = Path.Combine("IntelligenceX.Cli", "Templates", "triage-index-scheduled.yml");
        var rendered = File.ReadAllText(templatePath);

        AssertContainsText(rendered, "intelligencex:triage-index-summary", "index workflow summary marker present");
        AssertContainsText(rendered, "intelligencex:triage-control-dashboard", "index workflow dashboard marker present");
        AssertContainsText(rendered, "IX_PROJECT_VIEW_APPLY_ISSUE", "index workflow dashboard includes project-view issue variable");
        AssertContainsText(rendered, "artifacts/triage/ix-project-config.json", "index workflow dashboard reads project config when present");
        AssertContainsText(rendered, "intelligencex:triage-bootstrap-links", "index workflow dashboard links bootstrap marker when present");
        AssertContainsText(rendered, "issues/$ISSUE/comments", "index workflow reads control issue comments");
        AssertContainsText(rendered, "--method PATCH", "index workflow updates existing summary comment");
        AssertContainsText(rendered, "--method POST", "index workflow creates summary comment when missing");
        AssertContainsText(rendered, "Latest Summaries", "index workflow dashboard includes latest summaries section");
        AssertContainsText(rendered, "PR Babysit Governance", "index workflow dashboard includes pr-watch governance section");
        AssertContainsText(rendered, "pr-watch-rollup-tracker:weekly-governance", "index workflow dashboard looks for weekly governance tracker");
        AssertContainsText(rendered, "pr-watch-rollup-tracker:schedule", "index workflow dashboard falls back to nightly schedule tracker");
        AssertContainsText(rendered, "Governance status:", "index workflow dashboard includes governance status line");
        AssertContainsText(rendered, "Maintainer Quick Links", "index workflow dashboard includes quick links section");
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

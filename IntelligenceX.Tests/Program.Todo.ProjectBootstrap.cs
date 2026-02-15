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
#endif
}

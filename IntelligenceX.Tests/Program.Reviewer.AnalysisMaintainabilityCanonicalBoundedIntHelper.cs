namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleFlagsLegacyHelperUsage() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-canonical-bounded-int-helper-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleLegacyBoundedIntHelperTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleLegacyBoundedIntHelperTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_legacy_bounded_int_helper_tool",
        "Sample tool with legacy option-bounded helper usage.",
        ToolSchema.Object(("max_results", ToolSchema.Integer("Maximum rows."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = ToolArgs.GetPositiveOptionBoundedInt32OrDefault(arguments, "max_results", 100, 100);
        var meta = ToolOutputHints.Meta(count: 0, truncated: false);
        ToolBase.AddMaxResultsMeta(meta, maxResults);
        return Task.FromResult(ToolResponse.Ok(new JsonObject().Add("rows", new JsonArray()), meta));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run canonical bounded-int helper missing canonical path exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL004", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleLegacyBoundedIntHelperTool.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run canonical bounded-int helper missing canonical path finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleAcceptsCanonicalHelperUsage() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-canonical-bounded-int-helper-good-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleCanonicalBoundedIntHelperTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleCanonicalBoundedIntHelperTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_canonical_bounded_int_helper_tool",
        "Sample tool with canonical option-bounded helper usage.",
        ToolSchema.Object(("max_results", ToolSchema.Integer("Maximum rows."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = ToolArgs.GetOptionBoundedInt32(
            arguments,
            "max_results",
            optionMaxInclusive: 100,
            minInclusive: 1,
            nonPositiveBehavior: ToolArgs.NonPositiveInt32Behavior.UseDefault,
            defaultValue: 100);
        var meta = ToolOutputHints.Meta(count: 0, truncated: false);
        ToolBase.AddMaxResultsMeta(meta, maxResults);
        return Task.FromResult(ToolResponse.Ok(new JsonObject().Add("rows", new JsonArray()), meta));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run canonical bounded-int helper canonical path exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL004", StringComparison.OrdinalIgnoreCase)),
                "analyze run canonical bounded-int helper canonical path no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleIgnoresToolArgsImplementationFile() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-canonical-bounded-int-helper-toolargs-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            var commonDir = Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Common");
            Directory.CreateDirectory(commonDir);
            File.WriteAllText(Path.Combine(commonDir, "ToolArgs.cs"), """
namespace IntelligenceX.Tools.Common;

public static class ToolArgs {
    public static int GetPositiveOptionBoundedInt32OrDefault(object? arguments, string key, int defaultValue, int optionMaxInclusive) {
        return defaultValue;
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run canonical bounded-int helper ToolArgs implementation scope exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL004", StringComparison.OrdinalIgnoreCase)),
                "analyze run canonical bounded-int helper ToolArgs implementation scope no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalCanonicalBoundedIntHelperRuleIgnoresToolsTestsProject() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-canonical-bounded-int-helper-tools-tests-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            var testsDir = Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Tests");
            Directory.CreateDirectory(testsDir);
            File.WriteAllText(Path.Combine(testsDir, "SampleLegacyBoundedIntHelperTests.cs"), """
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Tests;

public sealed class SampleLegacyBoundedIntHelperTests {
    public static int ReadLimit(object? arguments) {
        return ToolArgs.GetPositiveOptionBoundedInt32OrDefault(arguments, "max_results", 100, 100);
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run canonical bounded-int helper tools tests scope exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL004", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("IntelligenceX.Tools/IntelligenceX.Tools.Tests/SampleLegacyBoundedIntHelperTests.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run canonical bounded-int helper tools tests scope no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif

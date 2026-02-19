namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalWriteToolSchemaRuleFlagsMissingHelpers() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-write-tool-schema-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample", "SampleBadTool.cs"), """
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleBadTool {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_bad_tool",
        "Sample bad write tool.",
        ToolSchema.Object(
                ("apply", ToolSchema.Boolean("When true, applies changes.")))
            .WithWriteGovernanceMetadata()
            .NoAdditionalProperties(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run write-tool schema missing helper exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL001", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleBadTool.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run write-tool schema missing helper finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalWriteToolSchemaRuleAcceptsCanonicalHelpers() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-write-tool-schema-good-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample", "SampleGoodTool.cs"), """
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleGoodTool {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_good_tool",
        "Sample good write tool.",
        ToolSchema.Object(
                ("apply", ToolSchema.Boolean("When true, applies changes.")))
            .WithWriteGovernanceDefaults(),
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));
}
""");

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample", "SampleGoodProbeTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleGoodProbeTool {
    private static readonly JsonObject Parameters = ToolSchema.Object(
            ("apply", ToolSchema.Boolean("When true, applies changes.")))
        .WithWriteGovernanceAndAuthenticationProbe();

    private static readonly ToolDefinition DefinitionValue = new(
        "sample_good_probe_tool",
        "Sample good write tool with probe metadata.",
        Parameters,
        writeGovernance: ToolWriteGovernanceConventions.BooleanFlagTrue(
            intentArgumentName: "apply",
            confirmationArgumentName: "apply"));
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run write-tool schema canonical helper exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL001", StringComparison.OrdinalIgnoreCase)),
                "analyze run write-tool schema canonical helpers no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalWriteToolSchemaRuleIgnoresReadOnlyTools() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-write-tool-schema-readonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample", "SampleReadOnlyTool.cs"), """
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleReadOnlyTool {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_read_only_tool",
        "Sample read-only tool.",
        ToolSchema.Object(
                ("query", ToolSchema.String("Query value.")))
            .NoAdditionalProperties());
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run write-tool schema read-only exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL001", StringComparison.OrdinalIgnoreCase)),
                "analyze run write-tool schema read-only no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalWriteToolSchemaRuleIgnoresAuthenticationOnlyToolDefinitions() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-write-tool-schema-authonly-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample", "SampleAuthOnlyTool.cs"), """
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleAuthOnlyTool {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_auth_only_tool",
        "Sample auth-only tool.",
        ToolSchema.Object(
                ("probe", ToolSchema.Boolean("When true, probes connectivity.")))
            .NoAdditionalProperties(),
        authentication: ToolAuthenticationConventions.HostManaged(
            requiresAuthentication: true));
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run write-tool schema auth-only exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL001", StringComparison.OrdinalIgnoreCase)),
                "analyze run write-tool schema auth-only no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif

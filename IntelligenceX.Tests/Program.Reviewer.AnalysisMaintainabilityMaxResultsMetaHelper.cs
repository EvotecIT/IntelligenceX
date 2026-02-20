namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsDirectMetaAdd() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleBadMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleBadMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_bad_max_results_meta_tool",
        "Sample tool with direct max_results metadata add.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                meta.Add("max_results", maxResults);
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper missing canonical helper exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasFinding(findings, "IXTOOL003",
                "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleBadMaxResultsMetaTool.cs",
                "analyze run max-results metadata helper missing canonical helper finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleAcceptsCanonicalHelper() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-good-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleGoodMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleGoodMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_good_max_results_meta_tool",
        "Sample tool with canonical max_results metadata helper.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                AddMaxResultsMeta(meta, maxResults);
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper canonical helper exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertNoFinding(findings, "IXTOOL003",
                "analyze run max-results metadata helper canonical helper no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleIgnoresNonToolFiles() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleBadMaxResultsMetaHelper.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Sample;

public static class SampleBadMaxResultsMetaHelper {
    public static void AddMeta(JsonObject meta, int maxResults) {
        meta.Add("max_results", maxResults);
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper non-tool file exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertNoFinding(findings, "IXTOOL003",
                "analyze run max-results metadata helper ignores non-tool files");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleIgnoresNearMissMetadataKeys() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-nearmiss-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleNearMissMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleNearMissMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_near_miss_max_results_meta_tool",
        "Sample tool with near-miss metadata key.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                meta.Add("max_results_extra", maxResults);
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper near-miss key exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertNoFinding(findings, "IXTOOL003",
                "analyze run max-results metadata helper near-miss key no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleAcceptsQualifiedCanonicalHelperCall() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-qualified-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleQualifiedMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleQualifiedMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_qualified_max_results_meta_tool",
        "Sample tool with qualified canonical helper usage.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                SampleQualifiedMaxResultsMetaTool.AddMaxResultsMeta(meta, maxResults);
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper qualified canonical helper exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertNoFinding(findings, "IXTOOL003",
                "analyze run max-results metadata helper qualified canonical helper no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsIndexerAssignment() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-indexer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleIndexerMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleIndexerMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_indexer_max_results_meta_tool",
        "Sample tool with indexer-based max_results metadata write.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                meta["max_results"] = maxResults;
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper indexer assignment exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasFinding(findings, "IXTOOL003",
                "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleIndexerMaxResultsMetaTool.cs",
                "analyze run max-results metadata helper indexer assignment finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsCaseVariantMetadataKey() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-casevariant-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleCaseVariantMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleCaseVariantMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_case_variant_max_results_meta_tool",
        "Sample tool with case-variant max_results metadata key.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                meta.Add("Max_Results", maxResults);
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper case-variant key exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasFinding(findings, "IXTOOL003",
                "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleCaseVariantMaxResultsMetaTool.cs",
                "analyze run max-results metadata helper case-variant key finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleFlagsOnlyMaxResultsInMixedMetaAdds() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleMixedMetaAddsTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleMixedMetaAddsTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_mixed_meta_adds_tool",
        "Sample tool with mixed metadata adds.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => {
                meta.Add("mode", "raw");
                meta.Add("max_results", maxResults);
                meta.Add("max_results_extra", maxResults);
            }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper mixed meta adds exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasExactlyOneFinding(findings, "IXTOOL003",
                "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleMixedMetaAddsTool.cs",
                "analyze run max-results metadata helper mixed meta adds only max_results finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalMaxResultsMetaHelperRuleDeduplicatesSameLineMatches() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analyze-max-results-meta-sameline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleSameLineMaxResultsMetaTool.cs"), """
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleSameLineMaxResultsMetaTool : ToolBase {
    private static readonly ToolDefinition DefinitionValue = new(
        "sample_same_line_max_results_meta_tool",
        "Sample tool with same-line max_results metadata writes.",
        ToolSchema.Object(("query", ToolSchema.String("Query value."))).NoAdditionalProperties());

    public override ToolDefinition Definition => DefinitionValue;

    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        var maxResults = 100;
        return Task.FromResult(BuildAutoTableResponse(
            arguments: arguments,
            model: new { rows = Array.Empty<object>() },
            sourceRows: Array.Empty<object>(),
            viewRowsPath: "rows",
            title: "Sample",
            baseTruncated: false,
            scanned: 0,
            maxTop: 1000,
            metaMutate: meta => { meta.Add("max_results", maxResults); meta["max_results"] = maxResults; }));
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run max-results metadata helper same-line dedupe exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertHasExactlyOneFinding(findings, "IXTOOL003",
                "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleSameLineMaxResultsMetaTool.cs",
                "analyze run max-results metadata helper same-line dedupe single finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif

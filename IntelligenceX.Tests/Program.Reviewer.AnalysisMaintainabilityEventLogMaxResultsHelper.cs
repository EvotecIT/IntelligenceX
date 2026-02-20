namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalEventLogMaxResultsHelperRuleFlagsBoundedMaxResultsPath() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-eventlog-max-helper-bounded-max-results-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.EventLog",
                "SampleLegacyEventLogBoundedTool.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.EventLog;

public sealed class SampleLegacyEventLogBoundedTool {
    public int Read(JsonObject? arguments) {
        return ResolveBoundedOptionLimit(arguments, "max_results");
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run EventLog max helper bounded max_results exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL005", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("IntelligenceX.Tools/IntelligenceX.Tools.EventLog/SampleLegacyEventLogBoundedTool.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run EventLog max helper bounded max_results finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalEventLogMaxResultsHelperRuleFlagsLegacyResolveMaxResults() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-eventlog-max-helper-legacy-resolve-max-results-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.EventLog",
                "SampleLegacyEventLogResolveMaxResultsTool.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.EventLog;

public sealed class SampleLegacyEventLogResolveMaxResultsTool {
    public int Read(JsonObject? arguments) {
        return ResolveMaxResults(arguments);
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run EventLog max helper legacy ResolveMaxResults exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(true, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL005", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals(
                        "IntelligenceX.Tools/IntelligenceX.Tools.EventLog/SampleLegacyEventLogResolveMaxResultsTool.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run EventLog max helper legacy ResolveMaxResults finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalEventLogMaxResultsHelperRuleAcceptsExplicitEventLogHelpers() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-eventlog-max-helper-explicit-helpers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.EventLog",
                "SampleCanonicalEventLogMaxResultsTool.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.EventLog;

public sealed class SampleCanonicalEventLogMaxResultsTool {
    public int ReadOptionBounded(JsonObject? arguments) {
        return ResolveOptionBoundedMaxResults(arguments);
    }

    public int ReadCapped(JsonObject? arguments) {
        return ResolveCappedMaxResults(arguments, defaultValue: 100, maxInclusive: 200);
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run EventLog max helper explicit helpers exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL005", StringComparison.OrdinalIgnoreCase)),
                "analyze run EventLog max helper explicit helpers no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalEventLogMaxResultsHelperRuleAllowsBoundedOptionForNonMaxResultsArgs() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-eventlog-max-helper-non-max-results-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.EventLog",
                "SampleCanonicalEventLogMaxEventsTool.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.EventLog;

public sealed class SampleCanonicalEventLogMaxEventsTool {
    public int Read(JsonObject? arguments) {
        return ResolveBoundedOptionLimit(arguments, "max_events");
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run EventLog max helper non-max_results bounded path exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item => item.RuleId.Equals("IXTOOL005", StringComparison.OrdinalIgnoreCase)),
                "analyze run EventLog max helper non-max_results bounded path no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalyzeRunInternalEventLogMaxResultsHelperRuleIgnoresNonEventLogTools() {
        var temp = Path.Combine(Path.GetTempPath(),
            "ix-analyze-eventlog-max-helper-scope-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            SetupToolContractAnalysisWorkspace(temp);

            File.WriteAllText(Path.Combine(temp, "IntelligenceX.Tools", "IntelligenceX.Tools.Sample",
                "SampleNonEventLogBoundedMaxResultsTool.cs"), """
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Sample;

public sealed class SampleNonEventLogBoundedMaxResultsTool {
    public int Read(JsonObject? arguments) {
        return ResolveBoundedOptionLimit(arguments, "max_results");
    }
}
""");

            var output = Path.Combine(temp, "artifacts");
            var exit = IntelligenceX.Cli.Analysis.AnalyzeRunCommand.RunAsync(new[] {
                "--workspace", temp,
                "--config", Path.Combine(temp, ".intelligencex", "reviewer.json"),
                "--out", output
            }).GetAwaiter().GetResult();

            AssertEqual(0, exit, "analyze run EventLog max helper non-eventlog scope exit");
            var findings = ReadFindingsRulePathPairs(Path.Combine(output, "intelligencex.findings.json"));
            AssertEqual(false, findings.Any(item =>
                    item.RuleId.Equals("IXTOOL005", StringComparison.OrdinalIgnoreCase) &&
                    item.Path.Equals("IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleNonEventLogBoundedMaxResultsTool.cs",
                        StringComparison.OrdinalIgnoreCase)),
                "analyze run EventLog max helper non-eventlog scope no finding");
        } finally {
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }
}
#endif

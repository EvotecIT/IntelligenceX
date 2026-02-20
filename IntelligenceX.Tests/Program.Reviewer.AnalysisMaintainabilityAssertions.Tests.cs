namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalMaintainabilityHelpersRejectEmptyRuleId() {
        var findings = new List<(string RuleId, string Path)> {
            ("IXTOOL001", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleBadTool.cs")
        };

        AssertThrows<ArgumentException>(() => AssertHasFinding(findings, "", "reject empty rule id"),
            "analyze run internal maintainability helper rejects empty rule id");
    }

    private static void TestAnalyzeRunInternalMaintainabilityHelpersRejectEmptyPathSuffix() {
        var findings = new List<(string RuleId, string Path)> {
            ("IXDUP001", "src/test.py")
        };

        AssertThrows<ArgumentException>(() => AssertHasFindingWithPathSuffix(findings, "IXDUP001", "", "reject empty path suffix"),
            "analyze run internal maintainability helper rejects empty path suffix");
    }

    private static void TestAnalyzeRunInternalMaintainabilityHelpersRejectEmptyAssertionMessage() {
        var findings = new List<(string RuleId, string Path)> {
            ("IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SampleBadMaxResultsMetaTool.cs")
        };

        AssertThrows<ArgumentException>(() => AssertNoFinding(findings, "IXTOOL003", " "),
            "analyze run internal maintainability helper rejects empty assertion message");
    }

    private static void TestAnalyzeRunInternalMaintainabilityHelpersRejectNullFindings() {
        AssertThrows<ArgumentNullException>(() => AssertHasFinding(null!, "IXTOOL001", "reject null findings"),
            "analyze run internal maintainability helper rejects null findings");
    }
}
#endif

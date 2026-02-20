namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalyzeRunInternalMaintainabilityHelpersPositivePaths() {
        var findings = new List<(string RuleId, string Path)> {
            ("IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/FirstTool.cs"),
            ("IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SecondTool.cs"),
            ("IXDUP001", "src/check.py")
        };

        AssertHasFinding(findings, "IXTOOL003", "positive helper has rule");
        AssertHasFinding(findings, "IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/FirstTool.cs",
            "positive helper has rule+path");
        AssertHasExactlyOneFinding(findings, "IXTOOL003",
            "IntelligenceX.Tools/IntelligenceX.Tools.Sample/SecondTool.cs",
            "positive helper has exactly one");
        AssertNoFinding(findings, "IXTOOL999", "positive helper no unmatched rule");
        AssertNoFinding(findings, "IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/MissingTool.cs",
            "positive helper no unmatched rule+path");
        AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".py", "positive helper suffix match");
        AssertNoFindingWithPathSuffix(findings, "IXDUP001", ".cs", "positive helper suffix miss");
    }

    private static void TestAnalyzeRunInternalMaintainabilityHelpersFailureIncludesMatchCount() {
        var findings = new List<(string RuleId, string Path)> {
            ("IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/OnlyTool.cs")
        };

        try {
            AssertHasFinding(findings, "IXTOOL999", "missing rule should fail");
            throw new InvalidOperationException("Expected missing-rule helper assertion to fail.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "missing rule should fail (matches=0)",
                "analyze run internal maintainability helper match-count message for missing rule");
        }

        try {
            AssertNoFinding(findings, "IXTOOL003", "existing rule should fail");
            throw new InvalidOperationException("Expected existing-rule helper assertion to fail.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "existing rule should fail (matches=1)",
                "analyze run internal maintainability helper match-count message for existing rule");
        }
    }

    private static void TestAnalyzeRunInternalMaintainabilityHelpersFailureIncludesMatchCountForPathAndSuffix() {
        var findings = new List<(string RuleId, string Path)> {
            ("IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/OnlyTool.cs"),
            ("IXDUP001", "src/check.py")
        };

        try {
            AssertHasFinding(findings, "IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/MissingTool.cs",
                "missing path should fail");
            throw new InvalidOperationException("Expected missing-path helper assertion to fail.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "missing path should fail (matches=0)",
                "analyze run internal maintainability helper match-count message for missing path");
        }

        try {
            AssertNoFinding(findings, "IXTOOL003", "IntelligenceX.Tools/IntelligenceX.Tools.Sample/OnlyTool.cs",
                "existing path should fail");
            throw new InvalidOperationException("Expected existing-path helper assertion to fail.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "existing path should fail (matches=1)",
                "analyze run internal maintainability helper match-count message for existing path");
        }

        try {
            AssertHasFindingWithPathSuffix(findings, "IXDUP001", ".cs", "missing suffix should fail");
            throw new InvalidOperationException("Expected missing-suffix helper assertion to fail.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "missing suffix should fail (matches=0)",
                "analyze run internal maintainability helper match-count message for missing suffix");
        }

        try {
            AssertNoFindingWithPathSuffix(findings, "IXDUP001", ".py", "existing suffix should fail");
            throw new InvalidOperationException("Expected existing-suffix helper assertion to fail.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "existing suffix should fail (matches=1)",
                "analyze run internal maintainability helper match-count message for existing suffix");
        }
    }

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

namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisPolicyEnabledRulePreviewSupportsNonBmpUnicodeTitles() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-preview-unicode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            var longUnicodeTitle = string.Concat(Enumerable.Repeat("👩‍💻", AnalysisPolicyFormatting.MaxRulePreviewTitleTextElements + 5));
            var expectedTruncatedTitle = BuildExpectedTruncatedTitle(longUnicodeTitle);
            File.WriteAllText(Path.Combine(rulesDir, "IXUNI001.json"), $$"""
{
  "id": "IXUNI001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXUNI001",
  "title": "{{longUnicodeTitle}}",
  "description": "unicode",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");
            File.WriteAllText(Path.Combine(packsDir, "ix-unicode-pack.json"), """
{
  "id": "ix-unicode-pack",
  "label": "IX Unicode Pack",
  "rules": ["IXUNI001"]
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-unicode-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), new AnalysisLoadReport(1, 1, 1, 0)));
            var preview = GetPolicyLineValue(policy, "Enabled rules preview", "analysis policy unicode preview line");

            AssertContainsText(preview, $"IXUNI001 ({expectedTruncatedTitle})",
                "analysis policy unicode truncated preview");
            AssertEqual(false, preview.Contains('\uFFFD'),
                "analysis policy unicode replacement-char absence");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisPolicyMarksPartialWhenOnlyOutsideFindingsAndEnabledRulesExist() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-enabled-outside-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var findings = new[] {
                new AnalysisFinding("scripts/test.ps1", 3, "Unknown rule payload", "warning", "PS9999", "PSScriptAnalyzer")
            };

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(findings, report));

            AssertPolicyLineEquals(policy, "Status", "partial", "analysis policy enabled-outside-only status");
            AssertPolicyLineEquals(policy, "Rule outcomes", "0 with findings, 2 clean, 1 outside enabled packs",
                "analysis policy enabled-outside-only outcomes");
            AssertPolicyLineEquals(policy, "Failing rules", "none", "analysis policy enabled-outside-only failing rules");
            AssertPolicyLineEquals(policy, "Clean rules", "IXTEST001 (Rule one), IXTEST002 (Rule two)",
                "analysis policy enabled-outside-only clean rules");
            AssertPolicyLineEquals(policy, "Outside-pack rules", "PS9999=1",
                "analysis policy enabled-outside-only outside rules");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisPolicyHandlesNullFindingsWhenReportExists() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-null-findings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(null!, report));

            AssertPolicyLineEquals(policy, "Status", "pass", "analysis policy null-findings status");
            AssertPolicyLineEquals(policy, "Failing rules", "none", "analysis policy null-findings failing rules");
            AssertPolicyLineEquals(policy, "Clean rules", "IXTEST001 (Rule one), IXTEST002 (Rule two)",
                "analysis policy null-findings clean rules");
            AssertPolicyLineEquals(policy, "Outside-pack rules", "none", "analysis policy null-findings outside rules");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

    private static void TestAnalysisPolicyRuleOutcomePreviewsUseDeterministicOrdering() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-ordering-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(2, 2, 2, 0);
            var findings = new[] {
                new AnalysisFinding("src/A.cs", 10, "rule one", "warning", "IXTEST001", "Roslyn"),
                new AnalysisFinding("src/B.cs", 11, "rule two first", "warning", "IXTEST002", "Roslyn"),
                new AnalysisFinding("src/C.cs", 12, "rule two second", "warning", "IXTEST002", "Roslyn"),
                new AnalysisFinding("src/D.cs", 13, "outside z", "warning", "PSZ", "PSScriptAnalyzer"),
                new AnalysisFinding("src/E.cs", 14, "outside a", "warning", "PSA", "PSScriptAnalyzer"),
                new AnalysisFinding("src/F.cs", 15, "outside a again", "warning", "PSA", "PSScriptAnalyzer"),
                new AnalysisFinding("src/G.cs", 16, "outside z again", "warning", "PSZ", "PSScriptAnalyzer")
            };

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(findings, report));

            AssertPolicyLineEquals(policy, "Failing rules", "IXTEST002 (Rule two)=2, IXTEST001 (Rule one)=1",
                "analysis policy deterministic failing order");
            AssertPolicyLineEquals(policy, "Outside-pack rules", "PSA=2, PSZ=2",
                "analysis policy deterministic outside order");
            AssertPolicyLineEquals(policy, "Clean rules", "none", "analysis policy deterministic clean rules");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            DeleteDirectoryIfExistsWithRetries(temp);
        }
    }

}
#endif

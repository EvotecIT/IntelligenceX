namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestAnalysisPolicyReportsRuleOutcomes() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-outcomes-" + Guid.NewGuid().ToString("N"));
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
                new AnalysisFinding("src/FileA.cs", 42, "Dispose object", "warning", "IXTEST001", "Roslyn"),
                new AnalysisFinding("scripts/test.ps1", 3, "Unknown rule payload", "warning", "PS9999", "PSScriptAnalyzer")
            };

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(findings, report));
            AssertContainsText(policy, "Status: fail", "analysis policy status");
            AssertContainsText(policy, "Rule outcomes: 1 with findings, 1 clean", "analysis policy outcomes");
            AssertContainsText(policy, "Findings outside enabled packs: 1 rule(s)", "analysis policy external findings");
            AssertContainsText(policy, "Rules with findings: IXTEST001=1, PS9999=1", "analysis policy rules with findings");
            AssertContainsText(policy, "Result files: 2 input patterns, 2 matched, 2 parsed, 0 failed",
                "analysis policy file stats");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyShowsUnavailableWhenNoResultFiles() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-no-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(2, 0, 0, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), report));

            AssertContainsText(policy, "Status: unavailable", "analysis policy no files status");
            AssertContainsText(policy, "Rule outcomes: unavailable (no analysis result files matched configured inputs)",
                "analysis policy no files outcomes");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyMarksPartialWhenOnlyOutsidePackFindingsExist() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-outside-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = Array.Empty<string>();
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(2, 1, 1, 0);
            var findings = new[] {
                new AnalysisFinding("scripts/test.ps1", 3, "Unknown rule payload", "warning", "PS9999", "PSScriptAnalyzer")
            };

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(findings, report));
            AssertContainsText(policy, "Status: partial", "analysis policy outside-only status");
            AssertContainsText(policy, "Rule outcomes: 0 with findings, 0 clean, 1 outside enabled packs",
                "analysis policy outside-only outcomes");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyShowsUnavailableWhenNoEnabledRulesAndNoFindings() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-no-enabled-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = Array.Empty<string>();
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), report));

            AssertContainsText(policy, "Status: unavailable", "analysis policy no-enabled-rules status");
            AssertContainsText(policy, "Rule outcomes: unavailable (no enabled rules configured)",
                "analysis policy no-enabled-rules outcomes");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyKeepsPassWhenOnlyOutsideFindingsAndEnabledRulesExist() {
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

            AssertContainsText(policy, "Status: pass", "analysis policy enabled-outside-only status");
            AssertContainsText(policy, "Rule outcomes: 0 with findings, 2 clean, 1 outside enabled packs",
                "analysis policy enabled-outside-only outcomes");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisSummaryShowsZeroFindings() {
        var results = new AnalysisResultsSettings {
            Summary = true,
            MinSeverity = "warning"
        };
        var report = new AnalysisLoadReport(2, 1, 1, 0);

        var summary = IntelligenceX.Reviewer.AnalysisSummaryBuilder.BuildSummary(Array.Empty<AnalysisFinding>(), results, report);
        AssertContainsText(summary, "### Static Analysis 🔎", "analysis summary header");
        AssertContainsText(summary, "Findings: 0", "analysis summary no findings");
    }

    private static void TestAnalysisSummaryShowsUnavailableWhenNoInputFiles() {
        var results = new AnalysisResultsSettings {
            Summary = true,
            MinSeverity = "warning"
        };
        var report = new AnalysisLoadReport(2, 0, 0, 0);

        var summary = IntelligenceX.Reviewer.AnalysisSummaryBuilder.BuildSummary(Array.Empty<AnalysisFinding>(), results, report);
        AssertContainsText(summary, "Findings: unavailable", "analysis summary unavailable");
    }

    private static void TestAnalysisLoadReportDoesNotDoubleCountFailedFiles() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(artifactsDir);
            var lockedFile = Path.Combine(artifactsDir, "locked.findings.json");
            File.WriteAllText(lockedFile, "{ \"schema\": \"intelligencex.findings.v1\", \"items\": [] }");

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.Inputs = new[] { "artifacts/locked.findings.json" };

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            using var stream = new FileStream(lockedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var result = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());

            AssertEqual(1, result.Report.ResolvedInputFiles, "analysis load resolved files");
            AssertEqual(0, result.Report.ParsedInputFiles, "analysis load parsed files");
            AssertEqual(1, result.Report.FailedInputFiles, "analysis load failed files");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisLoadReportDoesNotCountEmptyFilesAsParsed() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(artifactsDir);
            var emptyFile = Path.Combine(artifactsDir, "empty.findings.json");
            File.WriteAllText(emptyFile, string.Empty);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.Inputs = new[] { "artifacts/empty.findings.json" };

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var result = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
            AssertEqual(1, result.Report.ResolvedInputFiles, "analysis load empty file resolved");
            AssertEqual(0, result.Report.ParsedInputFiles, "analysis load empty file parsed");
            AssertEqual(0, result.Report.FailedInputFiles, "analysis load empty file failed");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void WriteAnalysisCatalogFixture(string root) {
        var rulesDir = Path.Combine(root, "Analysis", "Catalog", "rules", "internal");
        var packsDir = Path.Combine(root, "Analysis", "Packs");
        Directory.CreateDirectory(rulesDir);
        Directory.CreateDirectory(packsDir);

        File.WriteAllText(Path.Combine(rulesDir, "IXTEST001.json"), """
{
  "id": "IXTEST001",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXTEST001",
  "title": "Rule one",
  "description": "Rule one",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");

        File.WriteAllText(Path.Combine(rulesDir, "IXTEST002.json"), """
{
  "id": "IXTEST002",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "IXTEST002",
  "title": "Rule two",
  "description": "Rule two",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");

        File.WriteAllText(Path.Combine(packsDir, "ix-test-pack.json"), """
{
  "id": "ix-test-pack",
  "label": "IX Test Pack",
  "rules": ["IXTEST001", "IXTEST002"]
}
""");
    }
}
#endif

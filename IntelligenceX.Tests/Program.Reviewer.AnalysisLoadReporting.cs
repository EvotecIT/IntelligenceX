namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
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

    private static void TestAnalysisSummaryShowsZeroFindingsWithoutLoadReport() {
        var results = new AnalysisResultsSettings {
            Summary = true,
            MinSeverity = "warning"
        };

        var summary = IntelligenceX.Reviewer.AnalysisSummaryBuilder.BuildSummary(Array.Empty<AnalysisFinding>(), results, null);
        AssertContainsText(summary, "### Static Analysis 🔎", "analysis summary no report header");
        AssertContainsText(summary, "Findings: 0", "analysis summary no report findings");
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

    private static void TestAnalysisLoadReportCountsParsedForZeroFindingsAcrossFormats() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-zero-findings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(artifactsDir);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            void AssertCase(string fileName, string content, string label) {
                var filePath = Path.Combine(artifactsDir, fileName);
                File.WriteAllText(filePath, content);

                var settings = new ReviewSettings();
                settings.Analysis.Enabled = true;
                settings.Analysis.Results.Inputs = new[] { $"artifacts/{fileName}" };

                var result = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
                AssertEqual(1, result.Report.ResolvedInputFiles, $"analysis load zero-findings resolved {label}");
                AssertEqual(1, result.Report.ParsedInputFiles, $"analysis load zero-findings parsed {label}");
                AssertEqual(0, result.Report.FailedInputFiles, $"analysis load zero-findings failed {label}");
                AssertEqual(0, result.Findings.Count, $"analysis load zero-findings findings {label}");
            }

            AssertCase("zero.findings.json",
                "{ \"schema\": \"intelligencex.findings.v1\", \"items\": [] }",
                "findings-json-empty-items");
            AssertCase("zero-empty-runs.sarif",
                "{ \"version\": \"2.1.0\", \"runs\": [] }",
                "sarif-empty-runs");
            AssertCase("zero-empty-results.sarif",
                "{ \"version\": \"2.1.0\", \"runs\": [ { \"tool\": { \"driver\": { \"name\": \"demo\" } }, \"results\": [] } ] }",
                "sarif-empty-results");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
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

    private static void TestAnalysisLoadReportDeduplicatesResolvedFilesAcrossInputs() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-dedupe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(artifactsDir);
            var successFile = Path.Combine(artifactsDir, "success.findings.json");
            var badFile = Path.Combine(artifactsDir, "bad.findings.json");
            File.WriteAllText(successFile, "{ \"schema\": \"intelligencex.findings.v1\", \"items\": [ { \"path\": \"src/FileA.cs\", \"line\": 5, \"severity\": \"warning\", \"message\": \"ok\", \"ruleId\": \"IXTEST001\", \"tool\": \"Roslyn\" } ] }");
            File.WriteAllText(badFile, "{");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            void AssertCase(string[] inputs, string nameSuffix) {
                var settings = new ReviewSettings();
                settings.Analysis.Enabled = true;
                settings.Analysis.Results.Inputs = inputs;

                var result = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
                AssertEqual(2, result.Report.ResolvedInputFiles, $"analysis load dedupe resolved files {nameSuffix}");
                AssertEqual(1, result.Report.ParsedInputFiles, $"analysis load dedupe parsed files {nameSuffix}");
                AssertEqual(1, result.Report.FailedInputFiles, $"analysis load dedupe failed files {nameSuffix}");
                AssertEqual(1, result.Findings.Count, $"analysis load dedupe findings count {nameSuffix}");
            }

            AssertCase(new[] {
                "artifacts/*.findings.json",
                "artifacts/success.findings.json",
                "artifacts/bad.findings.json"
            }, "glob-first");
            AssertCase(new[] {
                "artifacts/bad.findings.json",
                "artifacts/success.findings.json",
                "artifacts/*.findings.json"
            }, "glob-last");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisLoadReportCountsSingleFailureForDuplicateBadInput() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-loader-dup-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var artifactsDir = Path.Combine(temp, "artifacts");
            Directory.CreateDirectory(artifactsDir);
            var badFile = Path.Combine(artifactsDir, "bad.findings.json");
            File.WriteAllText(badFile, "{");

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Results.Inputs = new[] {
                "artifacts/bad.findings.json",
                "artifacts/*.findings.json"
            };

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var result = IntelligenceX.Reviewer.AnalysisFindingsLoader.LoadWithReport(settings, Array.Empty<PullRequestFile>());
            AssertEqual(1, result.Report.ResolvedInputFiles, "analysis load duplicate bad resolved");
            AssertEqual(0, result.Report.ParsedInputFiles, "analysis load duplicate bad parsed");
            AssertEqual(1, result.Report.FailedInputFiles, "analysis load duplicate bad failed");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }
}
#endif

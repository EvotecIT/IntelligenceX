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
            settings.Analysis.ConfigMode = AnalysisConfigMode.Respect;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(2, 2, 2, 0);
            var findings = new[] {
                new AnalysisFinding("src/FileA.cs", 42, "Dispose object", "warning", "IXTEST001", "Roslyn"),
                new AnalysisFinding("scripts/test.ps1", 3, "Unknown rule payload", "warning", "PS9999", "PSScriptAnalyzer")
            };

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(findings, report));
            var expectedPolicy = string.Join("\n", new[] {
                "### Static Analysis Policy 🧭",
                "- Config mode: respect",
                "- Packs: IX Test Pack",
                "- Rules: 2 enabled",
                "- Rule list display: up to 10 items per section",
                "- Enabled rules preview: IXTEST001 (Rule one), IXTEST002 (Rule two)",
                "- Result files: 2 input patterns, 2 matched, 2 parsed, 0 failed",
                "- Status: fail ❌",
                "- Rule outcomes: 1 with findings, 1 clean, 1 outside enabled packs",
                "- Failing rules: IXTEST001 (Rule one)=1",
                "- Clean rules: IXTEST002 (Rule two)",
                "- Outside-pack rules: PS9999=1"
            });
            AssertContainsText(policy, "### Static Analysis Policy 🧭", "analysis policy header");
            AssertTextBlockEquals(expectedPolicy, policy, "analysis policy full block snapshot");
            AssertPolicyLineEquals(policy, "Status", "fail ❌", "analysis policy status");
            AssertPolicyLineEquals(policy, "Rule outcomes", "1 with findings, 1 clean, 1 outside enabled packs",
                "analysis policy outcomes");
            AssertPolicyLineEquals(policy, "Failing rules", "IXTEST001 (Rule one)=1",
                "analysis policy failing rules");
            AssertPolicyLineEquals(policy, "Clean rules", "IXTEST002 (Rule two)", "analysis policy clean rules");
            AssertPolicyLineEquals(policy, "Outside-pack rules", "PS9999=1", "analysis policy outside-pack rules");
            AssertPolicyLineEquals(policy, "Result files", "2 input patterns, 2 matched, 2 parsed, 0 failed",
                "analysis policy file stats");
            AssertPolicyLineEquals(policy, "Rule list display", "up to 10 items per section",
                "analysis policy rule list display");
            AssertPolicyLineEquals(policy, "Enabled rules preview", "IXTEST001 (Rule one), IXTEST002 (Rule two)",
                "analysis policy enabled rule preview");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyPackIncludesEnableRules() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-includes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            File.WriteAllText(Path.Combine(packsDir, "ix-wrapper-pack.json"), """
{
  "id": "ix-wrapper-pack",
  "label": "IX Wrapper Pack",
  "includes": ["ix-test-pack"],
  "rules": []
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-wrapper-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), report));

            AssertPolicyLineEquals(policy, "Packs", "IX Wrapper Pack", "analysis policy includes packs line");
            AssertPolicyLineEquals(policy, "Rules", "2 enabled", "analysis policy includes enabled rules");
            AssertContainsText(policy, "Enabled rules preview: IXTEST001 (Rule one), IXTEST002 (Rule two)",
                "analysis policy includes enabled preview");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyBuildUnavailablePolicy() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-unavailable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildUnavailablePolicy(settings,
                "internal error while loading analysis results");

            AssertContainsText(policy, "Status: unavailable", "analysis unavailable policy status");
            AssertContainsText(policy, "Rule outcomes: unavailable (internal error while loading analysis results)",
                "analysis unavailable policy outcomes");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyBuildsUnavailableWhenCatalogLoadFails() {
        var settings = new ReviewSettings();
        settings.Analysis.Enabled = true;
        settings.Analysis.Packs = new[] { "ix-test-pack" };
        settings.Analysis.Results.ShowPolicy = true;

        var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(
            settings,
            new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), new AnalysisLoadReport(1, 1, 1, 0)),
            _ => throw new IOException("disk I/O"));

        AssertPolicyLineEquals(policy, "Status", "unavailable ℹ️", "analysis policy catalog-failure status");
        AssertPolicyLineEquals(policy, "Rule outcomes", "unavailable (I/O error while loading analysis catalog)",
            "analysis policy catalog-failure reason");
        AssertPolicyLineEquals(policy, "Rules", "unavailable (analysis catalog could not be loaded)",
            "analysis policy catalog-failure rules");
    }

    private static void TestAnalysisPolicyUnavailableUsesCatalogFallbackWhenCatalogLoadFails() {
        var settings = new ReviewSettings();
        settings.Analysis.Enabled = true;
        settings.Analysis.Packs = new[] { "ix-test-pack" };
        settings.Analysis.Results.ShowPolicy = true;

        var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildUnavailablePolicy(
            settings,
            "analysis payload malformed",
            _ => throw new UnauthorizedAccessException("denied"));

        AssertPolicyLineEquals(policy, "Status", "unavailable ℹ️",
            "analysis unavailable policy catalog-failure status");
        AssertPolicyLineEquals(policy, "Rule outcomes",
            "unavailable (insufficient permissions while loading analysis catalog)",
            "analysis unavailable policy catalog-failure reason");
    }

    private static void TestAnalysisPolicyCatalogUnavailableNormalizesPackDisplay() {
        var settings = new ReviewSettings();
        settings.Analysis.Enabled = true;
        settings.Analysis.Packs = new[] { "  ", "ix-test-pack ", string.Empty, "ix-second-pack" };
        settings.Analysis.Results.ShowPolicy = true;

        var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(
            settings,
            new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), new AnalysisLoadReport(1, 1, 1, 0)),
            _ => throw new IOException("disk I/O"));

        AssertPolicyLineEquals(policy, "Packs", "ix-test-pack, ix-second-pack",
            "analysis policy catalog-failure packs normalized");
    }

    private static void TestAnalysisPolicyDoesNotSwallowUnexpectedCatalogLoadExceptions() {
        var settings = new ReviewSettings();
        settings.Analysis.Enabled = true;
        settings.Analysis.Packs = new[] { "ix-test-pack" };
        settings.Analysis.Results.ShowPolicy = true;

        AssertThrows<InvalidOperationException>(() =>
            IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(
                settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), new AnalysisLoadReport(1, 1, 1, 0)),
                _ => throw new InvalidOperationException("boom")),
            "analysis policy unexpected catalog exception");
    }

    private static void TestAnalysisLoadFailureEmbedsPolicyWhenSummaryDisabled() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-failure-embed-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;
            settings.Analysis.Results.Summary = false;
            settings.Analysis.Results.SummaryPlacement = "bottom";

            var method = typeof(ReviewerApp).GetMethod("ApplyAnalysisLoadFailure",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method is null) {
                throw new InvalidOperationException("ApplyAnalysisLoadFailure not found.");
            }

            var summary = "### Review Summary\n- baseline";
            var updated = method.Invoke(null, new object[] {
                summary,
                settings,
                new FormatException("malformed payload")
            }) as string ?? string.Empty;

            AssertContainsText(updated, "### Static Analysis Policy 🧭", "analysis failure embeds policy");
            AssertContainsText(updated, "Rule outcomes: unavailable (invalid analysis result format)",
                "analysis failure category reason");
            AssertEqual(false, updated.Contains("### Static Analysis 🔎", StringComparison.Ordinal),
                "analysis failure skips summary when disabled");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisLoadFailureSkipsOutputWhenPolicyAndSummaryDisabled() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-failure-no-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = false;
            settings.Analysis.Results.Summary = false;

            var method = typeof(ReviewerApp).GetMethod("ApplyAnalysisLoadFailure",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method is null) {
                throw new InvalidOperationException("ApplyAnalysisLoadFailure not found.");
            }

            var summary = "### Review Summary\n- baseline";
            var updated = method.Invoke(null, new object[] {
                summary,
                settings,
                new FormatException("malformed payload")
            }) as string ?? string.Empty;

            AssertEqual(summary, updated, "analysis failure no-output unchanged summary");
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
            AssertPolicyLineEquals(policy, "Status", "partial ⚠️", "analysis policy outside-only status");
            AssertPolicyLineEquals(policy, "Rule outcomes", "0 with findings, 0 clean, 1 outside enabled packs",
                "analysis policy outside-only outcomes");
            AssertPolicyLineEquals(policy, "Failing rules", "none", "analysis policy outside-only failing rules");
            AssertPolicyLineEquals(policy, "Clean rules", "none", "analysis policy outside-only clean rules");
            AssertPolicyLineEquals(policy, "Outside-pack rules", "PS9999=1", "analysis policy outside-only outside rules");
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
            AssertContainsText(policy, "Enabled rules preview: none", "analysis policy no-enabled-rules preview");
            AssertEqual(false, policy.Contains("(truncated)", StringComparison.Ordinal),
                "analysis policy no-enabled-rules truncation absence");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyRulePreviewLimitIsConfigurable() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-preview-limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;
            settings.Analysis.Results.PolicyRulePreviewItems = 1;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), report));

            AssertPolicyLineEquals(policy, "Rule list display", "up to 1 item per section",
                "analysis policy configurable display limit");
            AssertPolicyLineEquals(policy, "Enabled rules preview",
                "IXTEST001 (Rule one) (truncated)",
                "analysis policy configurable enabled preview");
            AssertPolicyLineEquals(policy, "Clean rules",
                "IXTEST001 (Rule one) (truncated)",
                "analysis policy configurable clean preview");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyRulePreviewLinesCanBeHidden() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-preview-hidden-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;
            settings.Analysis.Results.PolicyRulePreviewItems = 0;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var findings = new[] {
                new AnalysisFinding("src/FileA.cs", 42, "Dispose object", "warning", "IXTEST001", "Roslyn"),
                new AnalysisFinding("scripts/test.ps1", 3, "Unknown rule payload", "warning", "PS9999", "PSScriptAnalyzer")
            };
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(findings, report));

            AssertPolicyLineEquals(policy, "Rule list display", "hidden (policyRulePreviewItems=0)",
                "analysis policy hidden display limit");
            AssertPolicyLineEquals(policy, "Enabled rules preview", AnalysisPolicyFormatting.RulePreviewHiddenValue,
                "analysis policy hidden enabled preview");
            AssertPolicyLineEquals(policy, "Failing rules", AnalysisPolicyFormatting.RulePreviewHiddenValue,
                "analysis policy hidden failing rules");
            AssertPolicyLineEquals(policy, "Clean rules", AnalysisPolicyFormatting.RulePreviewHiddenValue,
                "analysis policy hidden clean rules");
            AssertPolicyLineEquals(policy, "Outside-pack rules", AnalysisPolicyFormatting.RulePreviewHiddenValue,
                "analysis policy hidden outside rules");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyRulePreviewLimitNegativeClampsToHidden() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-preview-negative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;
            settings.Analysis.Results.PolicyRulePreviewItems = -1;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), report));

            AssertPolicyLineEquals(policy, "Rule list display", "hidden (policyRulePreviewItems=0)",
                "analysis policy negative hidden display limit");
            AssertPolicyLineEquals(policy, "Enabled rules preview", AnalysisPolicyFormatting.RulePreviewHiddenValue,
                "analysis policy negative hidden enabled preview");
            AssertPolicyLineEquals(policy, "Clean rules", AnalysisPolicyFormatting.RulePreviewHiddenValue,
                "analysis policy negative hidden clean rules");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyRulePreviewLimitClampsToMax() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-preview-max-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            WriteAnalysisCatalogFixture(temp);
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-test-pack" };
            settings.Analysis.Results.ShowPolicy = true;
            settings.Analysis.Results.PolicyRulePreviewItems = AnalysisPolicyFormatting.MaxConfigurableRulePreviewItems + 1;

            var report = new AnalysisLoadReport(1, 1, 1, 0);
            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), report));

            AssertPolicyLineEquals(policy, "Rule list display",
                $"up to {AnalysisPolicyFormatting.MaxConfigurableRulePreviewItems} items per section",
                "analysis policy max clamp display limit");
            AssertPolicyLineEquals(policy, "Enabled rules preview",
                "IXTEST001 (Rule one), IXTEST002 (Rule two)",
                "analysis policy max clamp enabled preview");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void TestAnalysisPolicyEnabledRulePreviewTruncatesAndFallsBackToId() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-analysis-policy-preview-truncation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var previousWorkspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        try {
            var rulesDir = Path.Combine(temp, "Analysis", "Catalog", "rules", "internal");
            var packsDir = Path.Combine(temp, "Analysis", "Packs");
            Directory.CreateDirectory(rulesDir);
            Directory.CreateDirectory(packsDir);

            var longTitle = "Rule 3 " + new string('X', 120);
            var ruleIds = new List<string>();
            for (var i = 1; i <= AnalysisPolicyFormatting.MaxRulePreviewItems + 1; i++) {
                var id = $"IXPREV{i:000}";
                var title = i == 2 ? string.Empty : (i == 3 ? longTitle : $"Rule {i}");
                ruleIds.Add(id);
                File.WriteAllText(Path.Combine(rulesDir, $"{id}.json"), $$"""
{
  "id": "{{id}}",
  "language": "internal",
  "tool": "IntelligenceX.Maintainability",
  "toolRuleId": "{{id}}",
  "title": "{{title}}",
  "description": "{{id}}",
  "category": "Maintainability",
  "defaultSeverity": "warning"
}
""");
            }

            var ruleListJson = string.Join(", ", ruleIds.Select(id => $"\"{id}\""));
            File.WriteAllText(Path.Combine(packsDir, "ix-preview-pack.json"), $$"""
{
  "id": "ix-preview-pack",
  "label": "IX Preview Pack",
  "rules": [{{ruleListJson}}]
}
""");

            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", temp);

            var settings = new ReviewSettings();
            settings.Analysis.Enabled = true;
            settings.Analysis.Packs = new[] { "ix-preview-pack" };
            settings.Analysis.Results.ShowPolicy = true;

            var policy = IntelligenceX.Reviewer.AnalysisPolicyBuilder.BuildPolicy(settings,
                new AnalysisLoadResult(Array.Empty<AnalysisFinding>(), new AnalysisLoadReport(1, 1, 1, 0)));

            var preview = GetPolicyLineValue(policy, "Enabled rules preview", "analysis policy preview line");
            var expectedTruncatedTitle = BuildExpectedTruncatedTitle(longTitle);
            AssertContainsText(preview, $"IXPREV003 ({expectedTruncatedTitle})",
                "analysis policy preview truncated title");
            AssertContainsText(preview, "IXPREV010 (Rule 10)", "analysis policy preview includes boundary rule");
            AssertEqual(false, preview.Contains("IXPREV011", StringComparison.Ordinal),
                "analysis policy preview excludes overflow rules");
            AssertEqual(1, CountOccurrences(preview, AnalysisPolicyFormatting.TruncatedPreviewSuffix),
                "analysis policy preview single truncation marker");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_WORKSPACE", previousWorkspace);
            if (Directory.Exists(temp)) {
                Directory.Delete(temp, true);
            }
        }
    }

    private static void AssertPolicyLineEquals(string policy, string label, string expectedValue, string name) {
        var actualValue = GetPolicyLineValue(policy, label, name);
        AssertEqual(expectedValue, actualValue, name);
    }

    private static string GetPolicyLineValue(string policy, string label, string name) {
        var prefix = $"- {label}: ";
        foreach (var rawLine in policy.Split('\n')) {
            var line = rawLine.TrimEnd();
            if (line.StartsWith(prefix, StringComparison.Ordinal)) {
                return line.Substring(prefix.Length);
            }
        }
        throw new InvalidOperationException($"Expected {name} line '{prefix}' to exist.");
    }

    private static void AssertTextBlockEquals(string expected, string actual, string name) {
        var normalizedExpected = NormalizeNewlines(expected).Trim();
        var normalizedActual = NormalizeNewlines(actual).Trim();
        AssertEqual(normalizedExpected, normalizedActual, name);
    }

    private static string NormalizeNewlines(string? value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        return value.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static int CountOccurrences(string value, string marker) {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(marker)) {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(marker, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += marker.Length;
        }
        return count;
    }

    private static string BuildExpectedTruncatedTitle(string title) {
        var resolved = title.Trim();
        var info = new global::System.Globalization.StringInfo(resolved);
        if (info.LengthInTextElements <= AnalysisPolicyFormatting.MaxRulePreviewTitleTextElements) {
            return resolved;
        }
        return info.SubstringByTextElements(0, AnalysisPolicyFormatting.MaxRulePreviewTitleTextElements) +
               AnalysisPolicyFormatting.TruncationEllipsis;
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

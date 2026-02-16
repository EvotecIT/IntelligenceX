namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestVisionCheckClassifiesOutOfScopeWhenOutTokensDominate() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        );
        var candidate = new IntelligenceX.Cli.Todo.VisionCheckRunner.PullRequestCandidate(
            "pr#77",
            77,
            "Website rebrand and marketing refresh",
            "https://example/pr/77",
            new[] { "website" },
            44.2
        );
        var assessment = IntelligenceX.Cli.Todo.VisionCheckRunner.EvaluateAlignment(candidate, signals);
        AssertEqual("likely-out-of-scope", assessment.Classification, "vision classification");
        AssertEqual(true, assessment.OutOfScopeMatches.Count >= 2, "out-of-scope token matches");
    }

    private static void TestVisionCheckClassifiesAlignedWhenInTokensMatch() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        );
        var candidate = new IntelligenceX.Cli.Todo.VisionCheckRunner.PullRequestCandidate(
            "pr#78",
            78,
            "API security hardening for stability",
            "https://example/pr/78",
            new[] { "security" },
            88.9
        );
        var assessment = IntelligenceX.Cli.Todo.VisionCheckRunner.EvaluateAlignment(candidate, signals);
        AssertEqual("aligned", assessment.Classification, "vision classification");
        AssertEqual(true, assessment.InScopeMatches.Count >= 2, "in-scope token matches");
    }

    private static void TestVisionCheckExplicitRejectPolicyTakesPrecedence() {
        var signals = new IntelligenceX.Cli.Todo.VisionCheckRunner.VisionSignals(
            new HashSet<string>(new[] { "api", "stability", "security" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "website", "marketing", "rebrand" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "marketing" }, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(new[] { "migration" }, StringComparer.OrdinalIgnoreCase)
        );
        var candidate = new IntelligenceX.Cli.Todo.VisionCheckRunner.PullRequestCandidate(
            "pr#79",
            79,
            "Marketing redesign PR cleanup",
            "https://example/pr/79",
            Array.Empty<string>(),
            22.4
        );
        var assessment = IntelligenceX.Cli.Todo.VisionCheckRunner.EvaluateAlignment(candidate, signals);
        AssertEqual(true, assessment.ExplicitRejectMatches.Count >= 1, "explicit reject matches");
        AssertEqual("likely-out-of-scope", assessment.Classification, "explicit reject policy classification");
    }

    private static void TestVisionCheckParseSignalsSupportsExplicitPolicyPrefixes() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Website redesign and marketing",
                string.Empty,
                "## Maintainer Guidance",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var signals = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionSignals(visionPath);
            AssertEqual(true, signals.ExplicitAcceptTokens.Contains("security"), "accept policy token");
            AssertEqual(true, signals.ExplicitRejectTokens.Contains("marketing"), "reject policy token");
            AssertEqual(true, signals.ExplicitReviewTokens.Contains("migration"), "review policy token");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckParseDocumentSupportsStrictContract() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Ignore unrelated website redesign work",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing campaign redesign",
                string.Empty,
                "## Decision Principles",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var result = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionDocument(visionPath);
            AssertEqual(true, result.Contract.IsValid, "strict contract should be valid");
            AssertEqual(0, result.Contract.MissingSections.Count, "no missing sections");
            AssertEqual(true, result.Contract.ExplicitAcceptBullets >= 1, "aligned policy bullet present");
            AssertEqual(true, result.Contract.ExplicitRejectBullets >= 1, "reject policy bullet present");
            AssertEqual(true, result.Contract.ExplicitReviewBullets >= 1, "review policy bullet present");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckParseDocumentSupportsHeadingVariants() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-heading-variants-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Mission",
                "- Keep delivery quality high",
                string.Empty,
                "## Non Goals",
                "- Ignore unrelated website redesign work",
                string.Empty,
                "## In   Scope:",
                "- API stability and security hardening",
                string.Empty,
                "## Out of Scope",
                "- Marketing campaign redesign",
                string.Empty,
                "## Decision principles",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var result = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionDocument(visionPath);
            AssertEqual(true, result.Contract.IsValid, "heading variants should satisfy strict contract");
            AssertEqual(0, result.Contract.MissingSections.Count, "no required sections missing with heading variants");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckParseDocumentSupportsBacktickedPolicyPrefixes() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-policy-backticks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Ignore unrelated website redesign work",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing campaign redesign",
                string.Empty,
                "## Decision Principles",
                "- `aligned`: security hardening",
                "- `likely-out-of-scope`: marketing redesign",
                "- `needs-human-review`: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var result = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionDocument(visionPath);
            AssertEqual(true, result.Contract.IsValid, "backticked policy prefixes should satisfy strict contract");
            AssertEqual(true, result.Contract.ExplicitAcceptBullets >= 1, "aligned policy bullet detected with backticks");
            AssertEqual(true, result.Contract.ExplicitRejectBullets >= 1, "reject policy bullet detected with backticks");
            AssertEqual(true, result.Contract.ExplicitReviewBullets >= 1, "review policy bullet detected with backticks");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckRunEnforceContractSupportsBacktickedPolicyPrefixes() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-backticks-enforce-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var indexPath = Path.Combine(tempDir, "ix-triage-index.json");
            var outputPath = Path.Combine(tempDir, "ix-vision-check.json");
            var summaryPath = Path.Combine(tempDir, "ix-vision-check.md");

            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Ignore unrelated website redesign work",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing campaign redesign",
                string.Empty,
                "## Decision Principles",
                "- `aligned`: security hardening",
                "- `likely-out-of-scope`: marketing redesign",
                "- `needs-human-review`: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var indexJson = """
{
  "items": [
    {
      "kind": "pull_request",
      "id": "pr#203",
      "number": 203,
      "title": "API security hardening",
      "url": "https://example/pr/203",
      "score": 81.1,
      "labels": [ "security" ]
    }
  ]
}
""";
            File.WriteAllText(indexPath, indexJson);

            var exitCode = IntelligenceX.Cli.Todo.VisionCheckRunner.RunAsync(new[] {
                "--repo", "EvotecIT/IntelligenceX",
                "--vision", visionPath,
                "--no-refresh-index",
                "--index", indexPath,
                "--enforce-contract",
                "--out", outputPath,
                "--summary", summaryPath
            }).GetAwaiter().GetResult();

            AssertEqual(0, exitCode, "enforce contract should pass with backticked policy prefixes");
            AssertEqual(true, File.Exists(outputPath), "output json written");
            AssertEqual(true, File.Exists(summaryPath), "summary markdown written");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckParseDocumentSupportsLegacyDecisionHeading() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-heading-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Ignore unrelated website redesign work",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing campaign redesign",
                string.Empty,
                "## Maintainer Guidance",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var result = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionDocument(visionPath);
            AssertEqual(true, result.Contract.IsValid, "maintainer guidance heading should satisfy decision principles requirement");
            AssertEqual(true, result.Contract.DecisionPrinciplesBullets >= 3, "decision bullets captured under legacy heading");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckParseDocumentReportsMissingRequiredSection() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-contract-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Ignore unrelated website redesign work",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing campaign redesign"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var result = IntelligenceX.Cli.Todo.VisionCheckRunner.ParseVisionDocument(visionPath);
            AssertEqual(false, result.Contract.IsValid, "missing section should invalidate contract");
            AssertEqual(true, result.Contract.MissingSections.Contains("decision-principles", StringComparer.OrdinalIgnoreCase), "missing decision principles section");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckRunFailsOnContractWhenEnforced() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-enforce-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var indexPath = Path.Combine(tempDir, "ix-triage-index.json");
            var outputPath = Path.Combine(tempDir, "ix-vision-check.json");
            var summaryPath = Path.Combine(tempDir, "ix-vision-check.md");

            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing campaign redesign",
                string.Empty,
                "## Decision Principles",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var indexJson = """
{
  "items": [
    {
      "kind": "pull_request",
      "id": "pr#200",
      "number": 200,
      "title": "API security hardening",
      "url": "https://example/pr/200",
      "score": 81.1,
      "labels": [ "security" ]
    }
  ]
}
""";
            File.WriteAllText(indexPath, indexJson);

            var exitCode = IntelligenceX.Cli.Todo.VisionCheckRunner.RunAsync(new[] {
                "--repo", "EvotecIT/IntelligenceX",
                "--vision", visionPath,
                "--no-refresh-index",
                "--index", indexPath,
                "--enforce-contract",
                "--out", outputPath,
                "--summary", summaryPath
            }).GetAwaiter().GetResult();

            AssertEqual(2, exitCode, "enforce contract should fail when required sections are missing");
            AssertEqual(true, File.Exists(outputPath), "output json written");
            AssertEqual(true, File.Exists(summaryPath), "summary markdown written");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckRunFailsOnHighConfidenceDrift() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-drift-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var indexPath = Path.Combine(tempDir, "ix-triage-index.json");
            var outputPath = Path.Combine(tempDir, "ix-vision-check.json");
            var summaryPath = Path.Combine(tempDir, "ix-vision-check.md");

            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Build large marketing campaigns",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing redesign campaigns",
                string.Empty,
                "## Decision Principles",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var indexJson = """
{
  "items": [
    {
      "kind": "pull_request",
      "id": "pr#201",
      "number": 201,
      "title": "Marketing redesign and campaign refresh",
      "url": "https://example/pr/201",
      "score": 22.4,
      "labels": [ "marketing" ]
    }
  ]
}
""";
            File.WriteAllText(indexPath, indexJson);

            var exitCode = IntelligenceX.Cli.Todo.VisionCheckRunner.RunAsync(new[] {
                "--repo", "EvotecIT/IntelligenceX",
                "--vision", visionPath,
                "--no-refresh-index",
                "--index", indexPath,
                "--fail-on-drift",
                "--drift-threshold", "0.70",
                "--out", outputPath,
                "--summary", summaryPath
            }).GetAwaiter().GetResult();

            AssertEqual(3, exitCode, "drift gate should fail on high-confidence likely-out-of-scope PR");
            AssertEqual(true, File.Exists(outputPath), "output json written");
            AssertEqual(true, File.Exists(summaryPath), "summary markdown written");
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }

    private static void TestVisionCheckRejectsMalformedDriftThresholds() {
        var tempDir = Path.Combine(Path.GetTempPath(), "ix-vision-drift-threshold-parse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try {
            var visionPath = Path.Combine(tempDir, "VISION.md");
            var indexPath = Path.Combine(tempDir, "ix-triage-index.json");
            var visionContent = string.Join('\n', new[] {
                "# Vision",
                string.Empty,
                "## Goals",
                "- Keep delivery quality high",
                string.Empty,
                "## Non-Goals",
                "- Build large marketing campaigns",
                string.Empty,
                "## In Scope",
                "- API stability and security hardening",
                string.Empty,
                "## Out Of Scope",
                "- Marketing redesign campaigns",
                string.Empty,
                "## Decision Principles",
                "- aligned: security hardening",
                "- likely-out-of-scope: marketing redesign",
                "- needs-human-review: migration rollout"
            }) + "\n";
            File.WriteAllText(visionPath, visionContent);

            var indexJson = """
{
  "items": [
    {
      "kind": "pull_request",
      "id": "pr#202",
      "number": 202,
      "title": "Marketing redesign and campaign refresh",
      "url": "https://example/pr/202",
      "score": 22.4,
      "labels": [ "marketing" ]
    }
  ]
}
""";
            File.WriteAllText(indexPath, indexJson);

            var malformedThresholds = new[] { "0,70", "1,000", "1.20" };
            for (var i = 0; i < malformedThresholds.Length; i++) {
                var threshold = malformedThresholds[i];
                var outputPath = Path.Combine(tempDir, $"ix-vision-check-invalid-{i}.json");
                var summaryPath = Path.Combine(tempDir, $"ix-vision-check-invalid-{i}.md");
                var exitCode = IntelligenceX.Cli.Todo.VisionCheckRunner.RunAsync(new[] {
                    "--repo", "EvotecIT/IntelligenceX",
                    "--vision", visionPath,
                    "--no-refresh-index",
                    "--index", indexPath,
                    "--fail-on-drift",
                    "--drift-threshold", threshold,
                    "--out", outputPath,
                    "--summary", summaryPath
                }).GetAwaiter().GetResult();

                AssertEqual(0, exitCode, $"invalid threshold {threshold} should route to help flow");
                AssertEqual(false, File.Exists(outputPath), $"invalid threshold {threshold} should not produce json output");
                AssertEqual(false, File.Exists(summaryPath), $"invalid threshold {threshold} should not produce markdown output");
            }
        } finally {
            try {
                Directory.Delete(tempDir, recursive: true);
            } catch {
                // best effort
            }
        }
    }
#endif
}

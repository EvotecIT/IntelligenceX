using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Todo;

internal static partial class VisionCheckRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex NumberedBullet = new(@"^\d+\.\s+", RegexOptions.Compiled);
    private static readonly Regex PolicyPrefix = new(
        @"^`?\s*(aligned|accept|approve|likely-out-of-scope|reject|deny|needs-human-review|human-review|review|required-review)\s*`?\s*:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    private static readonly string[] RequiredSectionNames = {
        "goals",
        "non-goals",
        "in-scope",
        "out-of-scope",
        "decision-principles"
    };

    private const int ExitSuccess = 0;
    private const int ExitGeneralFailure = 1;
    private const int ExitContractFailure = 2;
    private const int ExitDriftFailure = 3;

    internal sealed record VisionSignals(
        IReadOnlySet<string> InScopeTokens,
        IReadOnlySet<string> OutOfScopeTokens,
        IReadOnlySet<string> ExplicitAcceptTokens,
        IReadOnlySet<string> ExplicitRejectTokens,
        IReadOnlySet<string> ExplicitReviewTokens
    );

    internal sealed record PullRequestCandidate(
        string Id,
        int Number,
        string Title,
        string Url,
        IReadOnlyList<string> Labels,
        double? Score
    );

    internal sealed record VisionAssessment(
        string Id,
        int Number,
        string Title,
        string Url,
        string Classification,
        double Confidence,
        double? Score,
        IReadOnlyList<string> InScopeMatches,
        IReadOnlyList<string> OutOfScopeMatches,
        IReadOnlyList<string> ExplicitAcceptMatches,
        IReadOnlyList<string> ExplicitRejectMatches,
        IReadOnlyList<string> ExplicitReviewMatches,
        string Reason
    );

    internal sealed record VisionContract(
        IReadOnlyList<string> MissingSections,
        int GoalsBullets,
        int NonGoalsBullets,
        int InScopeBullets,
        int OutOfScopeBullets,
        int DecisionPrinciplesBullets,
        int ExplicitAcceptBullets,
        int ExplicitRejectBullets,
        int ExplicitReviewBullets,
        IReadOnlyList<string> Diagnostics,
        bool IsValid
    );

    internal sealed record VisionParseResult(
        VisionSignals Signals,
        VisionContract Contract
    );

    private sealed class Options {
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public string VisionPath { get; set; } = "VISION.md";
        public string IndexPath { get; set; } = Path.Combine("artifacts", "triage", "ix-triage-index.json");
        public string OutputPath { get; set; } = Path.Combine("artifacts", "triage", "ix-vision-check.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "triage", "ix-vision-check.md");
        public bool RefreshIndex { get; set; } = true;
        public int MaxPrs { get; set; } = 300;
        public int MaxIssues { get; set; } = 300;
        public int MaxItems { get; set; } = 50;
        public bool EnforceContract { get; set; }
        public bool FailOnDrift { get; set; }
        public double DriftThreshold { get; set; } = 0.70;
        public bool ShowHelp { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return ExitSuccess;
        }

        if (!File.Exists(options.VisionPath)) {
            Console.Error.WriteLine($"Vision document not found: {options.VisionPath}");
            return ExitGeneralFailure;
        }

        if (options.RefreshIndex || !File.Exists(options.IndexPath)) {
            var triageSummaryPath = Path.Combine(Path.GetDirectoryName(options.IndexPath) ?? ".",
                "ix-triage-index.md");
            var triageExit = await TriageIndexRunner.RunAsync(new[] {
                "--repo", options.Repo,
                "--max-prs", options.MaxPrs.ToString(CultureInfo.InvariantCulture),
                "--max-issues", options.MaxIssues.ToString(CultureInfo.InvariantCulture),
                "--out", options.IndexPath,
                "--summary", triageSummaryPath
            }).ConfigureAwait(false);
            if (triageExit != 0) {
                return triageExit;
            }
        }

        var vision = ParseVisionDocument(options.VisionPath);
        var signals = vision.Signals;
        var contract = vision.Contract;
        var candidates = LoadCandidates(options.IndexPath);
        var assessments = candidates
            .Select(candidate => EvaluateAlignment(candidate, signals))
            .OrderBy(assessment => ClassificationRank(assessment.Classification))
            .ThenByDescending(assessment => assessment.Confidence)
            .ThenByDescending(assessment => assessment.Score ?? 0)
            .ToList();
        var highConfidenceLikelyOutOfScope = assessments
            .Where(item => item.Classification == "likely-out-of-scope" && item.Confidence >= options.DriftThreshold)
            .OrderByDescending(item => item.Confidence)
            .ThenByDescending(item => item.Score ?? 0)
            .ToList();

        var report = new {
            schema = "intelligencex.vision-check.v1",
            generatedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            repo = options.Repo,
            vision = new {
                path = options.VisionPath,
                inScopeTokens = signals.InScopeTokens.Count,
                outOfScopeTokens = signals.OutOfScopeTokens.Count,
                explicitAcceptTokens = signals.ExplicitAcceptTokens.Count,
                explicitRejectTokens = signals.ExplicitRejectTokens.Count,
                explicitReviewTokens = signals.ExplicitReviewTokens.Count,
                contract = new {
                    isValid = contract.IsValid,
                    missingSections = contract.MissingSections,
                    diagnostics = contract.Diagnostics,
                    goalsBullets = contract.GoalsBullets,
                    nonGoalsBullets = contract.NonGoalsBullets,
                    inScopeBullets = contract.InScopeBullets,
                    outOfScopeBullets = contract.OutOfScopeBullets,
                    decisionPrinciplesBullets = contract.DecisionPrinciplesBullets,
                    explicitAcceptBullets = contract.ExplicitAcceptBullets,
                    explicitRejectBullets = contract.ExplicitRejectBullets,
                    explicitReviewBullets = contract.ExplicitReviewBullets
                }
            },
            summary = new {
                pullRequestsEvaluated = assessments.Count,
                aligned = assessments.Count(item => item.Classification == "aligned"),
                needsHumanReview = assessments.Count(item => item.Classification == "needs-human-review"),
                likelyOutOfScope = assessments.Count(item => item.Classification == "likely-out-of-scope")
            },
            drift = new {
                threshold = Math.Round(options.DriftThreshold, 2, MidpointRounding.AwayFromZero),
                highConfidenceLikelyOutOfScope = highConfidenceLikelyOutOfScope.Count,
                failOnDrift = options.FailOnDrift,
                wouldFail = options.FailOnDrift && highConfidenceLikelyOutOfScope.Count > 0
            },
            assessments
        };

        var summary = BuildSummaryMarkdown(options, assessments, contract, highConfidenceLikelyOutOfScope);
        WriteText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        WriteText(options.SummaryPath, summary);

        Console.WriteLine($"Generated vision check: {options.OutputPath}");
        Console.WriteLine($"Generated vision summary: {options.SummaryPath}");
        Console.WriteLine($"PRs evaluated: {assessments.Count}");
        Console.WriteLine($"Likely out of scope: {assessments.Count(item => item.Classification == "likely-out-of-scope")}");
        Console.WriteLine($"Vision contract valid: {(contract.IsValid ? "yes" : "no")}");
        Console.WriteLine($"High-confidence likely-out-of-scope (>= {options.DriftThreshold.ToString("0.00", CultureInfo.InvariantCulture)}): {highConfidenceLikelyOutOfScope.Count}");

        if (options.EnforceContract && !contract.IsValid) {
            Console.Error.WriteLine("Vision contract validation failed.");
            foreach (var diagnostic in contract.Diagnostics) {
                Console.Error.WriteLine($"- {diagnostic}");
            }
            return ExitContractFailure;
        }

        if (options.FailOnDrift && highConfidenceLikelyOutOfScope.Count > 0) {
            Console.Error.WriteLine(
                $"Vision drift gate failed: found {highConfidenceLikelyOutOfScope.Count} likely-out-of-scope PR(s) with confidence >= {options.DriftThreshold.ToString("0.00", CultureInfo.InvariantCulture)}.");
            return ExitDriftFailure;
        }

        return ExitSuccess;
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--vision":
                    if (i + 1 < args.Length) {
                        options.VisionPath = args[++i];
                    }
                    break;
                case "--index":
                    if (i + 1 < args.Length) {
                        options.IndexPath = args[++i];
                    }
                    break;
                case "--out":
                    if (i + 1 < args.Length) {
                        options.OutputPath = args[++i];
                    }
                    break;
                case "--summary":
                    if (i + 1 < args.Length) {
                        options.SummaryPath = args[++i];
                    }
                    break;
                case "--refresh-index":
                    options.RefreshIndex = true;
                    break;
                case "--no-refresh-index":
                    options.RefreshIndex = false;
                    break;
                case "--max-prs":
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxPrs)) {
                        options.MaxPrs = Math.Max(1, Math.Min(maxPrs, 2000));
                    }
                    break;
                case "--max-issues":
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxIssues)) {
                        options.MaxIssues = Math.Max(1, Math.Min(maxIssues, 2000));
                    }
                    break;
                case "--max-items":
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxItems)) {
                        options.MaxItems = Math.Max(1, Math.Min(maxItems, 500));
                    }
                    break;
                case "--enforce-contract":
                    options.EnforceContract = true;
                    break;
                case "--no-enforce-contract":
                    options.EnforceContract = false;
                    break;
                case "--fail-on-drift":
                    options.FailOnDrift = true;
                    break;
                case "--no-fail-on-drift":
                    options.FailOnDrift = false;
                    break;
                case "--drift-threshold":
                    if (i + 1 < args.Length && TryParseDriftThreshold(args[++i], out var driftThreshold)) {
                        options.DriftThreshold = driftThreshold;
                    } else {
                        Console.Error.WriteLine("Invalid --drift-threshold value. Expected a number between 0 and 1.");
                        options.ShowHelp = true;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ShowHelp = true;
        }
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo vision-check [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>         Repository to scan (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --vision <path>             Vision document path (default: VISION.md)");
        Console.WriteLine("  --index <path>              Existing triage index JSON path");
        Console.WriteLine("  --refresh-index             Refresh triage index before vision check (default)");
        Console.WriteLine("  --no-refresh-index          Use existing triage index file only");
        Console.WriteLine("  --max-prs <n>               PR scan limit when refreshing index (1-2000, default: 300)");
        Console.WriteLine("  --max-issues <n>            Issue scan limit when refreshing index (1-2000, default: 300)");
        Console.WriteLine("  --max-items <n>             Max items per section in markdown summary (1-500, default: 50)");
        Console.WriteLine("  --enforce-contract          Fail when VISION.md misses required sections/policy bullets");
        Console.WriteLine("  --no-enforce-contract       Do not fail when VISION.md contract is incomplete (default)");
        Console.WriteLine("  --fail-on-drift             Fail when likely-out-of-scope PR confidence exceeds threshold");
        Console.WriteLine("  --no-fail-on-drift          Do not fail on drift matches (default)");
        Console.WriteLine("  --drift-threshold <0-1>     Drift fail threshold (default: 0.70)");
        Console.WriteLine("  --out <path>                JSON output path (default: artifacts/triage/ix-vision-check.json)");
        Console.WriteLine("  --summary <path>            Markdown summary path (default: artifacts/triage/ix-vision-check.md)");
    }

    private static bool TryParseDriftThreshold(string input, out double threshold) {
        threshold = 0;
        if (!double.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)) {
            return false;
        }

        if (double.IsNaN(parsed) || double.IsInfinity(parsed)) {
            return false;
        }

        if (parsed < 0.0 || parsed > 1.0) {
            return false;
        }

        threshold = parsed;
        return true;
    }
}

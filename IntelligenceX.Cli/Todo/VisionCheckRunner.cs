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

internal static class VisionCheckRunner {
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
            return 0;
        }

        if (!File.Exists(options.VisionPath)) {
            Console.Error.WriteLine($"Vision document not found: {options.VisionPath}");
            return 1;
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
            return 2;
        }

        if (options.FailOnDrift && highConfidenceLikelyOutOfScope.Count > 0) {
            Console.Error.WriteLine(
                $"Vision drift gate failed: found {highConfidenceLikelyOutOfScope.Count} likely-out-of-scope PR(s) with confidence >= {options.DriftThreshold.ToString("0.00", CultureInfo.InvariantCulture)}.");
            return 3;
        }

        return 0;
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
                    if (i + 1 < args.Length &&
                        double.TryParse(args[++i], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var driftThreshold)) {
                        options.DriftThreshold = Math.Clamp(driftThreshold, 0.0, 1.0);
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

    internal static VisionSignals ParseVisionSignals(string visionPath) {
        return ParseVisionDocument(visionPath).Signals;
    }

    internal static VisionParseResult ParseVisionDocument(string visionPath) {
        var inScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outOfScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitAccept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitReject = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitReview = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRequiredSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;
        var goalsBullets = 0;
        var nonGoalsBullets = 0;
        var inScopeBullets = 0;
        var outOfScopeBullets = 0;
        var decisionPrinciplesBullets = 0;
        var explicitAcceptBullets = 0;
        var explicitRejectBullets = 0;
        var explicitReviewBullets = 0;

        foreach (var rawLine in File.ReadLines(visionPath)) {
            var line = rawLine?.Trim() ?? string.Empty;
            if (line.Length == 0) {
                continue;
            }

            if (TryMapHeadingToSection(line, out var headingSection, out var requiredSection)) {
                section = headingSection;
                if (!string.IsNullOrWhiteSpace(requiredSection)) {
                    seenRequiredSections.Add(requiredSection);
                }
                continue;
            }

            var lowered = line.ToLowerInvariant();
            if (TryMapLegacySectionLine(lowered, out var mappedSection)) {
                section = mappedSection;
                continue;
            }

            if (!IsBulletLine(line)) {
                continue;
            }

            if (section == "goals") {
                goalsBullets++;
            } else if (section == "non-goals") {
                nonGoalsBullets++;
            } else if (section == "in") {
                inScopeBullets++;
            } else if (section == "out") {
                outOfScopeBullets++;
            } else if (section == "decision-principles") {
                decisionPrinciplesBullets++;
            }

            var content = StripBullet(line);
            var policySection = TryParsePolicySection(content, out var policyBody)
                ? policyBody.Item1
                : section;
            var policyContent = policyBody.Item2;
            var tokens = TriageIndexRunner.Tokenize(policyContent);
            foreach (var token in tokens) {
                allTokens.Add(token);
            }

            if (policySection == "in" || policySection == "goals") {
                foreach (var token in tokens) {
                    inScope.Add(token);
                }
            } else if (policySection == "out" || policySection == "non-goals") {
                foreach (var token in tokens) {
                    outOfScope.Add(token);
                }
            } else if (policySection == "accept") {
                explicitAcceptBullets++;
                foreach (var token in tokens) {
                    explicitAccept.Add(token);
                }
            } else if (policySection == "reject") {
                explicitRejectBullets++;
                foreach (var token in tokens) {
                    explicitReject.Add(token);
                }
            } else if (policySection == "review") {
                explicitReviewBullets++;
                foreach (var token in tokens) {
                    explicitReview.Add(token);
                }
            }
        }

        if (inScope.Count == 0 && allTokens.Count > 0) {
            foreach (var token in allTokens) {
                inScope.Add(token);
            }
        }

        var missingSections = RequiredSectionNames
            .Where(required => !seenRequiredSections.Contains(required))
            .ToList();
        var diagnostics = new List<string>();
        foreach (var missing in missingSections) {
            diagnostics.Add($"Missing required section: {DisplaySectionName(missing)}.");
        }
        if (goalsBullets == 0) {
            diagnostics.Add("Section Goals must include at least one bullet.");
        }
        if (nonGoalsBullets == 0) {
            diagnostics.Add("Section Non-Goals must include at least one bullet.");
        }
        if (inScopeBullets == 0) {
            diagnostics.Add("Section In Scope must include at least one bullet.");
        }
        if (outOfScopeBullets == 0) {
            diagnostics.Add("Section Out Of Scope must include at least one bullet.");
        }
        if (decisionPrinciplesBullets == 0) {
            diagnostics.Add("Section Decision Principles must include at least one bullet.");
        }
        if (explicitAcceptBullets == 0) {
            diagnostics.Add("Decision policy is missing an `aligned:` (or `accept:`) bullet.");
        }
        if (explicitRejectBullets == 0) {
            diagnostics.Add("Decision policy is missing a `likely-out-of-scope:` (or `reject:`) bullet.");
        }
        if (explicitReviewBullets == 0) {
            diagnostics.Add("Decision policy is missing a `needs-human-review:` (or `review:`) bullet.");
        }

        var contract = new VisionContract(
            missingSections,
            goalsBullets,
            nonGoalsBullets,
            inScopeBullets,
            outOfScopeBullets,
            decisionPrinciplesBullets,
            explicitAcceptBullets,
            explicitRejectBullets,
            explicitReviewBullets,
            diagnostics,
            diagnostics.Count == 0
        );

        return new VisionParseResult(new VisionSignals(inScope, outOfScope, explicitAccept, explicitReject, explicitReview), contract);
    }

    private static bool IsBulletLine(string line) {
        return line.StartsWith("- ", StringComparison.Ordinal) ||
               line.StartsWith("* ", StringComparison.Ordinal) ||
               NumberedBullet.IsMatch(line);
    }

    private static string StripBullet(string line) {
        if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)) {
            return line.Substring(2).Trim();
        }
        return NumberedBullet.Replace(line, string.Empty).Trim();
    }

    private static bool TryParsePolicySection(string content, out (string Item1, string Item2) policy) {
        policy = (string.Empty, content);
        var match = PolicyPrefix.Match(content);
        if (!match.Success) {
            return false;
        }

        var directive = match.Groups[1].Value.Trim().ToLowerInvariant();
        var body = match.Groups[2].Value.Trim();
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }

        var section = directive switch {
            "aligned" => "accept",
            "accept" => "accept",
            "approve" => "accept",
            "likely-out-of-scope" => "reject",
            "reject" => "reject",
            "deny" => "reject",
            "needs-human-review" => "review",
            "human-review" => "review",
            "review" => "review",
            "required-review" => "review",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(section)) {
            return false;
        }

        policy = (section, body);
        return true;
    }

    private static bool TryMapHeadingToSection(string line, out string section, out string requiredSection) {
        section = string.Empty;
        requiredSection = string.Empty;
        if (!line.StartsWith("#", StringComparison.Ordinal)) {
            return false;
        }

        var heading = line.TrimStart('#').Trim();
        if (string.IsNullOrWhiteSpace(heading)) {
            return false;
        }

        var words = ExtractHeadingWords(heading);
        if (words.Count == 0) {
            return false;
        }

        if (HeadingHasPhrase(words, "non", "goals") ||
            HeadingHasPhrase(words, "non", "goal") ||
            HeadingHasAnyWord(words, "nongoals", "nongoal")) {
            section = "non-goals";
            requiredSection = "non-goals";
            return true;
        }

        if (HeadingHasAnyWord(words, "goals", "goal", "mission")) {
            section = "goals";
            requiredSection = "goals";
            return true;
        }

        if (HeadingHasPhrase(words, "in", "scope") ||
            HeadingHasAnyWord(words, "inscope", "included")) {
            section = "in";
            requiredSection = "in-scope";
            return true;
        }

        if (HeadingHasPhrase(words, "out", "of", "scope") ||
            HeadingHasPhrase(words, "not", "in", "scope") ||
            HeadingHasAnyWord(words, "outofscope")) {
            section = "out";
            requiredSection = "out-of-scope";
            return true;
        }

        if (HeadingHasPhrase(words, "decision", "principles") ||
            HeadingHasPhrase(words, "decision", "principle") ||
            HeadingHasPhrase(words, "decision", "notes") ||
            HeadingHasPhrase(words, "maintainer", "guidance") ||
            HeadingHasPhrase(words, "maintainers", "guidance")) {
            section = "decision-principles";
            requiredSection = "decision-principles";
            return true;
        }

        if (HeadingHasAnyWord(words, "accept") ||
            HeadingHasPhrase(words, "accept", "guidance") ||
            HeadingHasPhrase(words, "accept", "signals")) {
            section = "accept";
            return true;
        }

        if (HeadingHasAnyWord(words, "reject") ||
            HeadingHasPhrase(words, "reject", "guidance") ||
            HeadingHasPhrase(words, "reject", "signals")) {
            section = "reject";
            return true;
        }

        if (HeadingHasAnyWord(words, "review") ||
            HeadingHasPhrase(words, "needs", "human", "review") ||
            HeadingHasPhrase(words, "human", "review", "guidance")) {
            section = "review";
            return true;
        }

        return false;
    }

    private static bool TryMapLegacySectionLine(string loweredLine, out string section) {
        section = string.Empty;
        if (loweredLine.Contains("in scope", StringComparison.Ordinal) ||
            loweredLine.Contains("goals", StringComparison.Ordinal) ||
            loweredLine.Contains("included", StringComparison.Ordinal)) {
            section = "in";
            return true;
        }
        if (loweredLine.Contains("out of scope", StringComparison.Ordinal) ||
            loweredLine.Contains("non-goals", StringComparison.Ordinal) ||
            loweredLine.Contains("not in scope", StringComparison.Ordinal)) {
            section = "out";
            return true;
        }
        if (loweredLine.Contains("accept guidance", StringComparison.Ordinal) ||
            loweredLine.Contains("accept signals", StringComparison.Ordinal) ||
            loweredLine.Equals("## accept", StringComparison.Ordinal) ||
            loweredLine.Equals("### accept", StringComparison.Ordinal)) {
            section = "accept";
            return true;
        }
        if (loweredLine.Contains("reject guidance", StringComparison.Ordinal) ||
            loweredLine.Contains("reject signals", StringComparison.Ordinal) ||
            loweredLine.Equals("## reject", StringComparison.Ordinal) ||
            loweredLine.Equals("### reject", StringComparison.Ordinal)) {
            section = "reject";
            return true;
        }
        if (loweredLine.Contains("human review guidance", StringComparison.Ordinal) ||
            loweredLine.Contains("needs human review", StringComparison.Ordinal) ||
            loweredLine.Equals("## review", StringComparison.Ordinal) ||
            loweredLine.Equals("### review", StringComparison.Ordinal)) {
            section = "review";
            return true;
        }
        return false;
    }

    private static List<string> ExtractHeadingWords(string heading) {
        var words = new List<string>();
        var token = new StringBuilder(heading.Length);
        foreach (var ch in heading) {
            if (char.IsLetterOrDigit(ch)) {
                token.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (token.Length == 0) {
                continue;
            }
            words.Add(token.ToString());
            token.Clear();
        }

        if (token.Length > 0) {
            words.Add(token.ToString());
        }

        return words;
    }

    private static bool HeadingHasAnyWord(IReadOnlyList<string> words, params string[] expectedWords) {
        for (var i = 0; i < expectedWords.Length; i++) {
            var expected = expectedWords[i];
            for (var j = 0; j < words.Count; j++) {
                if (words[j].Equals(expected, StringComparison.Ordinal)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HeadingHasPhrase(IReadOnlyList<string> words, params string[] expectedPhrase) {
        if (expectedPhrase.Length == 0 || words.Count < expectedPhrase.Length) {
            return false;
        }

        for (var i = 0; i <= words.Count - expectedPhrase.Length; i++) {
            var matches = true;
            for (var j = 0; j < expectedPhrase.Length; j++) {
                if (!words[i + j].Equals(expectedPhrase[j], StringComparison.Ordinal)) {
                    matches = false;
                    break;
                }
            }

            if (matches) {
                return true;
            }
        }

        return false;
    }

    private static string DisplaySectionName(string normalized) {
        return normalized switch {
            "goals" => "Goals",
            "non-goals" => "Non-Goals",
            "in-scope" => "In Scope",
            "out-of-scope" => "Out Of Scope",
            "decision-principles" => "Decision Principles",
            _ => normalized
        };
    }

    private static List<PullRequestCandidate> LoadCandidates(string indexPath) {
        if (!File.Exists(indexPath)) {
            throw new FileNotFoundException($"Triage index not found: {indexPath}");
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
        var root = doc.RootElement;
        if (!TryGetProperty(root, "items", out var items) || items.ValueKind != JsonValueKind.Array) {
            return new List<PullRequestCandidate>();
        }

        var candidates = new List<PullRequestCandidate>();
        foreach (var item in items.EnumerateArray()) {
            var kind = ReadString(item, "kind");
            if (!kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var id = ReadString(item, "id");
            var number = ReadInt(item, "number");
            var title = ReadString(item, "title");
            var url = ReadString(item, "url");
            var score = ReadNullableDouble(item, "score");
            var labels = ReadStringArray(item, "labels");
            if (string.IsNullOrWhiteSpace(id) || number <= 0) {
                continue;
            }
            candidates.Add(new PullRequestCandidate(id, number, title, url, labels, score));
        }
        return candidates;
    }

    internal static VisionAssessment EvaluateAlignment(PullRequestCandidate candidate, VisionSignals signals) {
        var tokens = TriageIndexRunner.Tokenize($"{candidate.Title} {string.Join(' ', candidate.Labels)}");
        var inMatches = tokens
            .Where(token => signals.InScopeTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var outMatches = tokens
            .Where(token => signals.OutOfScopeTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitAcceptMatches = tokens
            .Where(token => signals.ExplicitAcceptTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitRejectMatches = tokens
            .Where(token => signals.ExplicitRejectTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var explicitReviewMatches = tokens
            .Where(token => signals.ExplicitReviewTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string classification;
        double confidence;
        string reason;
        if (explicitRejectMatches.Count > 0 && explicitAcceptMatches.Count == 0) {
            classification = "likely-out-of-scope";
            confidence = Math.Min(0.99, 0.80 + (explicitRejectMatches.Count * 0.06));
            reason = $"Explicit reject-policy matches: {string.Join(", ", explicitRejectMatches.Take(5))}.";
        } else if (explicitAcceptMatches.Count > 0 && explicitRejectMatches.Count == 0 && explicitReviewMatches.Count == 0) {
            classification = "aligned";
            confidence = Math.Min(0.98, 0.78 + (explicitAcceptMatches.Count * 0.05));
            reason = $"Explicit accept-policy matches: {string.Join(", ", explicitAcceptMatches.Take(5))}.";
        } else if (explicitAcceptMatches.Count > 0 && explicitRejectMatches.Count > 0) {
            classification = "needs-human-review";
            confidence = 0.66;
            reason = $"Conflicting explicit policy matches (accept: {string.Join(", ", explicitAcceptMatches.Take(3))}; reject: {string.Join(", ", explicitRejectMatches.Take(3))}).";
        } else if (explicitReviewMatches.Count > 0) {
            classification = "needs-human-review";
            confidence = Math.Min(0.80, 0.60 + (explicitReviewMatches.Count * 0.05));
            reason = $"Explicit human-review policy matches: {string.Join(", ", explicitReviewMatches.Take(5))}.";
        } else if (outMatches.Count >= 2 && inMatches.Count == 0) {
            classification = "likely-out-of-scope";
            confidence = Math.Min(0.98, 0.65 + (outMatches.Count * 0.10));
            reason = $"Out-of-scope token matches: {string.Join(", ", outMatches.Take(5))}.";
        } else if (inMatches.Count >= 2 && outMatches.Count == 0) {
            classification = "aligned";
            confidence = Math.Min(0.95, 0.60 + (inMatches.Count * 0.08));
            reason = $"In-scope token matches: {string.Join(", ", inMatches.Take(5))}.";
        } else if (outMatches.Count > 0 && inMatches.Count > 0) {
            classification = "needs-human-review";
            confidence = 0.55;
            reason = $"Mixed scope signals (in: {string.Join(", ", inMatches.Take(3))}; out: {string.Join(", ", outMatches.Take(3))}).";
        } else {
            classification = "needs-human-review";
            confidence = 0.40;
            reason = "No strong scope evidence in PR title/labels.";
        }

        return new VisionAssessment(
            candidate.Id,
            candidate.Number,
            candidate.Title,
            candidate.Url,
            classification,
            Math.Round(confidence, 2, MidpointRounding.AwayFromZero),
            candidate.Score,
            inMatches,
            outMatches,
            explicitAcceptMatches,
            explicitRejectMatches,
            explicitReviewMatches,
            reason
        );
    }

    private static int ClassificationRank(string classification) {
        return classification switch {
            "likely-out-of-scope" => 0,
            "needs-human-review" => 1,
            _ => 2
        };
    }

    private static string BuildSummaryMarkdown(
        Options options,
        IReadOnlyList<VisionAssessment> assessments,
        VisionContract contract,
        IReadOnlyList<VisionAssessment> highConfidenceLikelyOutOfScope) {
        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Vision Check");
        sb.AppendLine();
        sb.AppendLine($"- Vision file: `{options.VisionPath}`");
        sb.AppendLine($"- Repo: `{options.Repo}`");
        sb.AppendLine($"- PRs evaluated: {assessments.Count}");
        sb.AppendLine();
        sb.AppendLine("## Vision Contract");
        sb.AppendLine();
        sb.AppendLine($"- required sections status: {(contract.MissingSections.Count == 0 ? "ok" : "missing sections detected")}");
        sb.AppendLine($"- goals bullets: {contract.GoalsBullets}");
        sb.AppendLine($"- non-goals bullets: {contract.NonGoalsBullets}");
        sb.AppendLine($"- in-scope bullets: {contract.InScopeBullets}");
        sb.AppendLine($"- out-of-scope bullets: {contract.OutOfScopeBullets}");
        sb.AppendLine($"- decision-principles bullets: {contract.DecisionPrinciplesBullets}");
        sb.AppendLine($"- explicit policy bullets (aligned / out-of-scope / review): {contract.ExplicitAcceptBullets} / {contract.ExplicitRejectBullets} / {contract.ExplicitReviewBullets}");
        if (contract.Diagnostics.Count == 0) {
            sb.AppendLine("- contract diagnostics: none");
        } else {
            sb.AppendLine("- contract diagnostics:");
            foreach (var diagnostic in contract.Diagnostics.Take(options.MaxItems)) {
                sb.AppendLine($"  - {diagnostic}");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## Drift Gate");
        sb.AppendLine();
        sb.AppendLine($"- threshold: {options.DriftThreshold.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- high-confidence likely-out-of-scope: {highConfidenceLikelyOutOfScope.Count}");
        sb.AppendLine($"- fail-on-drift: {(options.FailOnDrift ? "enabled" : "disabled")}");
        sb.AppendLine();
        if (highConfidenceLikelyOutOfScope.Count > 0) {
            sb.AppendLine("### High-Confidence Drift Items");
            sb.AppendLine();
            foreach (var item in highConfidenceLikelyOutOfScope.Take(options.MaxItems)) {
                var scoreLabel = item.Score.HasValue ? $" | triage score {item.Score.Value.ToString("0.00", CultureInfo.InvariantCulture)}" : string.Empty;
                sb.AppendLine($"- #{item.Number} ({item.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}){scoreLabel}: [{item.Title}]({item.Url})");
                sb.AppendLine($"  - {item.Reason}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Classification Summary");
        sb.AppendLine();
        sb.AppendLine($"- likely-out-of-scope: {assessments.Count(item => item.Classification == "likely-out-of-scope")}");
        sb.AppendLine($"- needs-human-review: {assessments.Count(item => item.Classification == "needs-human-review")}");
        sb.AppendLine($"- aligned: {assessments.Count(item => item.Classification == "aligned")}");
        sb.AppendLine();

        AppendSection(sb, "Likely Out Of Scope", assessments.Where(item => item.Classification == "likely-out-of-scope").Take(options.MaxItems).ToList());
        AppendSection(sb, "Needs Human Review", assessments.Where(item => item.Classification == "needs-human-review").Take(options.MaxItems).ToList());
        AppendSection(sb, "Aligned", assessments.Where(item => item.Classification == "aligned").Take(options.MaxItems).ToList());

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSection(StringBuilder sb, string title, IReadOnlyList<VisionAssessment> items) {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (items.Count == 0) {
            sb.AppendLine("None.");
            sb.AppendLine();
            return;
        }
        foreach (var item in items) {
            var scoreLabel = item.Score.HasValue ? $" | triage score {item.Score.Value.ToString("0.00", CultureInfo.InvariantCulture)}" : string.Empty;
            sb.AppendLine($"- #{item.Number} ({item.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}){scoreLabel}: [{item.Title}]({item.Url})");
            sb.AppendLine($"  - {item.Reason}");
        }
        sb.AppendLine();
    }

    private static void WriteText(string path, string content) {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static int ReadInt(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static double? ReadNullableDouble(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop)) {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (prop.ValueKind != JsonValueKind.Number || !prop.TryGetDouble(out var value)) {
            return null;
        }
        return value;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }
        var list = new List<string>();
        foreach (var item in prop.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.String) {
                continue;
            }
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) {
                list.Add(value);
            }
        }
        return list;
    }
}

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
        @"^(aligned|accept|approve|likely-out-of-scope|reject|deny|needs-human-review|human-review|review|required-review)\s*:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

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

        var signals = ParseVisionSignals(options.VisionPath);
        var candidates = LoadCandidates(options.IndexPath);
        var assessments = candidates
            .Select(candidate => EvaluateAlignment(candidate, signals))
            .OrderBy(assessment => ClassificationRank(assessment.Classification))
            .ThenByDescending(assessment => assessment.Confidence)
            .ThenByDescending(assessment => assessment.Score ?? 0)
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
                explicitReviewTokens = signals.ExplicitReviewTokens.Count
            },
            summary = new {
                pullRequestsEvaluated = assessments.Count,
                aligned = assessments.Count(item => item.Classification == "aligned"),
                needsHumanReview = assessments.Count(item => item.Classification == "needs-human-review"),
                likelyOutOfScope = assessments.Count(item => item.Classification == "likely-out-of-scope")
            },
            assessments
        };

        var summary = BuildSummaryMarkdown(options, assessments);
        WriteText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        WriteText(options.SummaryPath, summary);

        Console.WriteLine($"Generated vision check: {options.OutputPath}");
        Console.WriteLine($"Generated vision summary: {options.SummaryPath}");
        Console.WriteLine($"PRs evaluated: {assessments.Count}");
        Console.WriteLine($"Likely out of scope: {assessments.Count(item => item.Classification == "likely-out-of-scope")}");
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
        Console.WriteLine("  --out <path>                JSON output path (default: artifacts/triage/ix-vision-check.json)");
        Console.WriteLine("  --summary <path>            Markdown summary path (default: artifacts/triage/ix-vision-check.md)");
    }

    internal static VisionSignals ParseVisionSignals(string visionPath) {
        var inScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outOfScope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitAccept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitReject = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var explicitReview = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = string.Empty;

        foreach (var rawLine in File.ReadLines(visionPath)) {
            var line = rawLine?.Trim() ?? string.Empty;
            if (line.Length == 0) {
                continue;
            }

            var lowered = line.ToLowerInvariant();
            if (lowered.Contains("in scope", StringComparison.Ordinal) ||
                lowered.Contains("goals", StringComparison.Ordinal) ||
                lowered.Contains("included", StringComparison.Ordinal)) {
                section = "in";
                continue;
            }
            if (lowered.Contains("out of scope", StringComparison.Ordinal) ||
                lowered.Contains("non-goals", StringComparison.Ordinal) ||
                lowered.Contains("not in scope", StringComparison.Ordinal)) {
                section = "out";
                continue;
            }
            if (lowered.Contains("accept guidance", StringComparison.Ordinal) ||
                lowered.Contains("accept signals", StringComparison.Ordinal) ||
                lowered.Equals("## accept", StringComparison.Ordinal) ||
                lowered.Equals("### accept", StringComparison.Ordinal)) {
                section = "accept";
                continue;
            }
            if (lowered.Contains("reject guidance", StringComparison.Ordinal) ||
                lowered.Contains("reject signals", StringComparison.Ordinal) ||
                lowered.Equals("## reject", StringComparison.Ordinal) ||
                lowered.Equals("### reject", StringComparison.Ordinal)) {
                section = "reject";
                continue;
            }
            if (lowered.Contains("human review guidance", StringComparison.Ordinal) ||
                lowered.Contains("needs human review", StringComparison.Ordinal) ||
                lowered.Equals("## review", StringComparison.Ordinal) ||
                lowered.Equals("### review", StringComparison.Ordinal)) {
                section = "review";
                continue;
            }

            if (!IsBulletLine(line)) {
                continue;
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

            if (policySection == "in") {
                foreach (var token in tokens) {
                    inScope.Add(token);
                }
            } else if (policySection == "out") {
                foreach (var token in tokens) {
                    outOfScope.Add(token);
                }
            } else if (policySection == "accept") {
                foreach (var token in tokens) {
                    explicitAccept.Add(token);
                }
            } else if (policySection == "reject") {
                foreach (var token in tokens) {
                    explicitReject.Add(token);
                }
            } else if (policySection == "review") {
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

        return new VisionSignals(inScope, outOfScope, explicitAccept, explicitReject, explicitReview);
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

    private static string BuildSummaryMarkdown(Options options, IReadOnlyList<VisionAssessment> assessments) {
        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Vision Check");
        sb.AppendLine();
        sb.AppendLine($"- Vision file: `{options.VisionPath}`");
        sb.AppendLine($"- Repo: `{options.Repo}`");
        sb.AppendLine($"- PRs evaluated: {assessments.Count}");
        sb.AppendLine();
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

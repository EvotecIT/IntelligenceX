using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace IntelligenceX.Cli.Todo;

internal static partial class VisionCheckRunner {
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

            switch (policySection) {
                case "accept":
                    explicitAcceptBullets++;
                    break;
                case "reject":
                    explicitRejectBullets++;
                    break;
                case "review":
                    explicitReviewBullets++;
                    break;
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
}

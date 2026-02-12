using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using IntelligenceX.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeRunCommand {
    private const int MaxTagWarningDetails = 5;
    private const string InternalMaxLinesRuleId = "IXLOC001";
    private const string InternalDuplicationRuleId = "IXDUP001";
    private const string DuplicationWindowLinesTagPrefix = "dup-window-lines:";
    private const string MaxDuplicationPercentTagPrefix = "max-duplication-percent:";
    private const string MaxDuplicationPercentByLanguageTagPrefix = "max-duplication-percent-";
    private const string IncludeExtensionTagPrefix = "include-ext:";
    private const int DefaultDuplicationWindowLines = 8;
    private const double DefaultMaxDuplicationPercent = 25.0;
    private static readonly string[] DefaultIncludedSourceExtensions = {
        ".cs",
        ".ps1",
        ".psm1",
        ".psd1",
        ".js",
        ".jsx",
        ".mjs",
        ".cjs",
        ".ts",
        ".tsx",
        ".py"
    };
    private static readonly string[] MaxLinesSupportedTagPrefixes = {
        MaxLinesTagPrefix,
        IncludeExtensionTagPrefix,
        GeneratedSuffixTagPrefix,
        GeneratedMarkerTagPrefix,
        GeneratedHeaderLinesTagPrefix,
        ExcludedDirectoryTagPrefix
    };
    private static readonly string[] DuplicationSupportedTagPrefixes = {
        DuplicationWindowLinesTagPrefix,
        MaxDuplicationPercentTagPrefix,
        MaxDuplicationPercentByLanguageTagPrefix,
        IncludeExtensionTagPrefix,
        GeneratedSuffixTagPrefix,
        GeneratedMarkerTagPrefix,
        GeneratedHeaderLinesTagPrefix,
        ExcludedDirectoryTagPrefix
    };
    private static readonly Regex PowerShellTokenRegex = new(
        "\"(?:`.|[^\"])*\"|'(?:''|[^'])*'|\\$[A-Za-z_][A-Za-z0-9_]*|[A-Za-z_][A-Za-z0-9_-]*|\\d+(?:\\.\\d+)?|==|!=|<=|>=|\\+=|-=|\\*=|/=|%=|&&|\\|\\||::|=>|\\+\\+|--|[-+*/%=!<>|&.:?]+|[()\\[\\]{};,]",
        RegexOptions.Compiled);
    private static readonly Regex JavaScriptTokenRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`|\\d+(?:\\.\\d+)?|[A-Za-z_$][A-Za-z0-9_$]*|===|!==|==|!=|<=|>=|=>|\\+\\+|--|\\+=|-=|\\*=|/=|%=|&&|\\|\\||\\*\\*|[-+*/%=!<>|&.:?]+|[()\\[\\]{};,]",
        RegexOptions.Compiled);
    private static readonly Regex PythonTokenRegex = new(
        "\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|\\d+(?:\\.\\d+)?|[A-Za-z_][A-Za-z0-9_]*|==|!=|<=|>=|:=|->|\\+=|-=|\\*=|/=|%=|//=|\\*\\*|[-+*/%=!<>|&^~.:?]+|[()\\[\\]{};,]",
        RegexOptions.Compiled);
    private static readonly HashSet<string> PowerShellKeywordTokens = new(StringComparer.OrdinalIgnoreCase) {
        "begin",
        "break",
        "catch",
        "class",
        "continue",
        "data",
        "do",
        "dynamicparam",
        "else",
        "elseif",
        "end",
        "enum",
        "exit",
        "filter",
        "finally",
        "for",
        "foreach",
        "from",
        "function",
        "if",
        "in",
        "inlinescript",
        "parallel",
        "param",
        "process",
        "return",
        "switch",
        "throw",
        "trap",
        "try",
        "until",
        "using",
        "var",
        "while",
        "workflow"
    };
    private static readonly HashSet<string> JavaScriptKeywordTokens = new(StringComparer.OrdinalIgnoreCase) {
        "await",
        "break",
        "case",
        "catch",
        "class",
        "const",
        "continue",
        "debugger",
        "default",
        "delete",
        "do",
        "else",
        "export",
        "extends",
        "finally",
        "for",
        "function",
        "if",
        "import",
        "in",
        "instanceof",
        "let",
        "new",
        "return",
        "static",
        "super",
        "switch",
        "this",
        "throw",
        "try",
        "typeof",
        "var",
        "void",
        "while",
        "with",
        "yield"
    };
    private static readonly HashSet<string> PythonKeywordTokens = new(StringComparer.OrdinalIgnoreCase) {
        "and",
        "as",
        "assert",
        "async",
        "await",
        "break",
        "case",
        "class",
        "continue",
        "def",
        "del",
        "elif",
        "else",
        "except",
        "finally",
        "for",
        "from",
        "global",
        "if",
        "import",
        "in",
        "is",
        "lambda",
        "match",
        "nonlocal",
        "not",
        "or",
        "pass",
        "raise",
        "return",
        "try",
        "while",
        "with",
        "yield"
    };

    private static InternalMaintainabilityResult RunInternalMaintainabilityChecks(string workspace,
        string outputDirectory,
        IReadOnlyList<AnalysisPolicyRule> rules, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var duplicationRuleMetrics = new List<DuplicationRuleMetrics>();
        if (rules is null || rules.Count == 0) {
            return new InternalMaintainabilityResult(findings, duplicationRuleMetrics);
        }

        var maxLinesRules = rules
            .Where(rule => rule?.Rule is not null && IsMaxLinesRule(rule.Rule))
            .ToList();
        var duplicationRules = rules
            .Where(rule => rule?.Rule is not null && IsDuplicationRule(rule.Rule))
            .ToList();
        if (maxLinesRules.Count == 0 && duplicationRules.Count == 0) {
            return new InternalMaintainabilityResult(findings, duplicationRuleMetrics);
        }

        // Build a candidate source set once; each rule applies its own filtering tags on top.
        var includedSourceExtensions = ResolveIncludedSourceExtensionsForRules(
            maxLinesRules.Concat(duplicationRules)
                .Where(static item => item?.Rule is not null)
                .Select(static item => item.Rule),
            warnings);
        var excludedDirectorySegments = new HashSet<string>(DefaultExcludedDirectorySegments, StringComparer.OrdinalIgnoreCase);
        var excludedOutputPath = TryGetRelativePathWithinWorkspace(workspace, outputDirectory);

        var sourceFiles = EnumerateSourceFiles(workspace, includedSourceExtensions, excludedDirectorySegments, excludedOutputPath,
                warnings)
            .Select(path => Path.GetFullPath(path))
            .Select(fullPath => new SourceFileEntry {
                FullPath = fullPath,
                RelativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/')
            })
            .ToList();
        if (sourceFiles.Count == 0) {
            Console.WriteLine("Internal maintainability checks: no eligible source files.");
            return new InternalMaintainabilityResult(findings, duplicationRuleMetrics);
        }

        findings.AddRange(RunMaxLinesChecks(maxLinesRules, sourceFiles, excludedOutputPath, warnings));
        var duplicationResult = RunDuplicationChecks(duplicationRules, sourceFiles, excludedOutputPath, warnings);
        findings.AddRange(duplicationResult.Findings);
        duplicationRuleMetrics.AddRange(duplicationResult.RuleMetrics);

        Console.WriteLine($"Internal maintainability findings: {findings.Count} item(s).");
        return new InternalMaintainabilityResult(findings, duplicationRuleMetrics);
    }

    private static IReadOnlyList<AnalysisFindingItem> RunMaxLinesChecks(IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        if (rules is null || rules.Count == 0) {
            return findings;
        }
        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            var next = EvaluateMaxLinesRule(rule, sourceFiles, excludedOutputPath, warnings);
            findings.AddRange(next);
        }
        return findings;
    }

    private static IReadOnlyList<AnalysisFindingItem> EvaluateMaxLinesRule(AnalysisPolicyRule policyRule,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var severity = NormalizeSeverity(policyRule.Severity);
        if (string.IsNullOrWhiteSpace(severity)) {
            Console.WriteLine($"Internal maintainability rule {policyRule.Rule.Id} is disabled by policy severity.");
            return findings;
        }
        var filteredFiles = FilterSourceFilesForRule(policyRule.Rule, sourceFiles, excludedOutputPath, warnings);
        if (filteredFiles.Count == 0) {
            return findings;
        }

        var maxLinesLimit = ResolveMaxLinesLimit(policyRule.Rule, warnings);
        ValidateInternalMaintainabilityTags(policyRule.Rule, MaxLinesSupportedTagPrefixes, warnings);

        var emittedRuleId = string.IsNullOrWhiteSpace(policyRule.Rule.ToolRuleId)
            ? policyRule.Rule.Id
            : policyRule.Rule.ToolRuleId;
        var emittedTool = string.IsNullOrWhiteSpace(policyRule.Rule.Tool)
            ? InternalToolName
            : policyRule.Rule.Tool;

        foreach (var sourceFile in filteredFiles) {
            var relativePath = sourceFile.RelativePath;
            int lineCount;
            try {
                lineCount = CountFileLines(sourceFile.FullPath);
            } catch (Exception ex) {
                warnings.Add($"Failed to read file for line-count check ({relativePath}): {ex.Message}");
                continue;
            }
            if (lineCount <= maxLinesLimit) {
                continue;
            }

            findings.Add(new AnalysisFindingItem {
                Path = relativePath,
                Line = 1,
                Severity = severity,
                Message = $"File has {lineCount} lines (limit {maxLinesLimit}). Split into smaller units.",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = $"{policyRule.Rule.Id}:{relativePath}:{lineCount}:{maxLinesLimit}"
            });
        }

        return findings;
    }

    private static int ResolveMaxLinesLimit(AnalysisRule rule, List<string> warnings) {
        var limit = DefaultMaxFileLinesLimit;
        if (rule is null || rule.Tags is null || rule.Tags.Count == 0) {
            return limit;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(MaxLinesTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var value = tag.Substring(MaxLinesTagPrefix.Length).Trim();
            if (int.TryParse(value, out var parsed) && parsed > 0) {
                return parsed;
            }
            warnings.Add(
                $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{MaxLinesTagPrefix}<positive-int>'; using {DefaultMaxFileLinesLimit}.");
            return limit;
        }
        return limit;
    }

    private static DuplicationEvaluationResult RunDuplicationChecks(IReadOnlyList<AnalysisPolicyRule> rules,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var ruleMetrics = new List<DuplicationRuleMetrics>();
        if (rules is null || rules.Count == 0) {
            return new DuplicationEvaluationResult(findings, ruleMetrics);
        }
        foreach (var rule in rules.Where(static candidate => candidate?.Rule is not null)) {
            var next = EvaluateDuplicationRule(rule, sourceFiles, excludedOutputPath, warnings);
            findings.AddRange(next.Findings);
            ruleMetrics.Add(next.RuleMetrics);
        }
        return new DuplicationEvaluationResult(findings, ruleMetrics);
    }

    private static DuplicationRuleEvaluationResult EvaluateDuplicationRule(AnalysisPolicyRule policyRule,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        var findings = new List<AnalysisFindingItem>();
        var severity = NormalizeSeverity(policyRule.Severity);
        if (string.IsNullOrWhiteSpace(severity)) {
            Console.WriteLine($"Internal maintainability rule {policyRule.Rule.Id} is disabled by policy severity.");
            return new DuplicationRuleEvaluationResult(findings, BuildEmptyDuplicationRuleMetrics(policyRule));
        }

        ValidateInternalMaintainabilityTags(policyRule.Rule, DuplicationSupportedTagPrefixes, warnings);
        var windowLines = ResolveDuplicationWindowLines(policyRule.Rule, warnings);
        var maxDuplicationPercent = ResolveMaxDuplicationPercent(policyRule.Rule, warnings);
        var maxDuplicationPercentByLanguage = ResolveMaxDuplicationPercentByLanguage(policyRule.Rule, warnings);
        var filteredFiles = FilterSourceFilesForRule(policyRule.Rule, sourceFiles, excludedOutputPath, warnings);
        if (filteredFiles.Count == 0) {
            Console.WriteLine(
                $"Internal duplication summary ({policyRule.Rule.Id}): 0.00% (0/0 significant lines) [threshold {FormatPercent(maxDuplicationPercent)}%].");
            return new DuplicationRuleEvaluationResult(findings, BuildEmptyDuplicationRuleMetrics(policyRule, windowLines,
                maxDuplicationPercent));
        }
        var emittedRuleId = string.IsNullOrWhiteSpace(policyRule.Rule.ToolRuleId)
            ? policyRule.Rule.Id
            : policyRule.Rule.ToolRuleId;
        var emittedTool = string.IsNullOrWhiteSpace(policyRule.Rule.Tool)
            ? InternalToolName
            : policyRule.Rule.Tool;

        var duplicationFiles = new List<DuplicationSourceFile>();
        foreach (var sourceFile in filteredFiles) {
            var significantLines = ReadSignificantLines(sourceFile, warnings);
            duplicationFiles.Add(new DuplicationSourceFile(sourceFile, significantLines));
        }

        var filesWithComparableLines = duplicationFiles
            .Where(file => file.SignificantLines.Count >= windowLines)
            .ToList();
        if (filesWithComparableLines.Count == 0) {
            Console.WriteLine(
                $"Internal duplication summary ({policyRule.Rule.Id}): 0.00% (0/0 significant lines) [threshold {FormatPercent(maxDuplicationPercent)}%].");
            return new DuplicationRuleEvaluationResult(findings, BuildEmptyDuplicationRuleMetrics(policyRule, windowLines,
                maxDuplicationPercent));
        }

        var signatures = new Dictionary<string, List<WindowOccurrence>>(StringComparer.Ordinal);
        for (var fileIndex = 0; fileIndex < filesWithComparableLines.Count; fileIndex++) {
            var file = filesWithComparableLines[fileIndex];
            for (var start = 0; start <= file.SignificantLines.Count - windowLines; start++) {
                var signature = BuildWindowSignature(file.SignificantLines, start, windowLines);
                if (!signatures.TryGetValue(signature, out var occurrences)) {
                    occurrences = new List<WindowOccurrence>();
                    signatures[signature] = occurrences;
                }
                occurrences.Add(new WindowOccurrence(fileIndex, start));
            }
        }

        var duplicatedLineMasks = new List<bool[]>();
        foreach (var file in filesWithComparableLines) {
            duplicatedLineMasks.Add(new bool[file.SignificantLines.Count]);
        }

        foreach (var occurrenceGroup in signatures.Values) {
            if (occurrenceGroup.Count <= 1) {
                continue;
            }
            if (!HasDistinctWindowOccurrences(occurrenceGroup)) {
                continue;
            }

            foreach (var occurrence in occurrenceGroup) {
                var mask = duplicatedLineMasks[occurrence.FileIndex];
                for (var offset = 0; offset < windowLines && occurrence.StartIndex + offset < mask.Length; offset++) {
                    mask[occurrence.StartIndex + offset] = true;
                }
            }
        }

        var totalComparableLines = 0;
        var totalDuplicatedLines = 0;
        var fileMetrics = new List<DuplicationFileMetrics>();
        for (var fileIndex = 0; fileIndex < filesWithComparableLines.Count; fileIndex++) {
            var file = filesWithComparableLines[fileIndex];
            var duplicatedLines = duplicatedLineMasks[fileIndex].Count(static value => value);
            totalComparableLines += file.SignificantLines.Count;
            totalDuplicatedLines += duplicatedLines;

            var duplicatedPercent = ComputePercent(duplicatedLines, file.SignificantLines.Count);
            var fileLanguage = ResolveLanguageFromPath(file.Source.RelativePath);
            var fileMaxDuplicationPercent = ResolveMaxDuplicationPercentForLanguage(
                fileLanguage,
                maxDuplicationPercent,
                maxDuplicationPercentByLanguage);
            var firstDuplicatedIndex = Array.FindIndex(duplicatedLineMasks[fileIndex], static value => value);
            var firstDuplicatedLine = firstDuplicatedIndex >= 0
                ? file.SignificantLines[firstDuplicatedIndex].OriginalLine
                : 1;
            var perFileFingerprint =
                $"{policyRule.Rule.Id}:{file.Source.RelativePath}:{duplicatedLines}:{file.SignificantLines.Count}:{windowLines}";
            fileMetrics.Add(new DuplicationFileMetrics {
                Path = file.Source.RelativePath,
                Language = fileLanguage,
                FirstDuplicatedLine = firstDuplicatedLine,
                SignificantLines = file.SignificantLines.Count,
                DuplicatedLines = duplicatedLines,
                DuplicatedPercent = duplicatedPercent,
                ConfiguredMaxPercent = fileMaxDuplicationPercent,
                Fingerprint = perFileFingerprint
            });

            if (duplicatedPercent - fileMaxDuplicationPercent <= double.Epsilon) {
                continue;
            }
            if (duplicatedLines == 0) {
                continue;
            }

            findings.Add(new AnalysisFindingItem {
                Path = file.Source.RelativePath,
                Line = firstDuplicatedLine,
                Severity = severity,
                Message =
                    $"Duplicated significant lines: {duplicatedLines}/{file.SignificantLines.Count} ({FormatPercent(duplicatedPercent)}%) exceeds limit {FormatPercent(fileMaxDuplicationPercent)}%.",
                RuleId = emittedRuleId,
                Tool = emittedTool,
                Fingerprint = perFileFingerprint
            });
        }

        var overallPercent = ComputePercent(totalDuplicatedLines, totalComparableLines);
        var metrics = new DuplicationRuleMetrics {
            RuleId = emittedRuleId,
            Tool = emittedTool,
            WindowLines = windowLines,
            ConfiguredMaxPercent = maxDuplicationPercent,
            TotalSignificantLines = totalComparableLines,
            DuplicatedSignificantLines = totalDuplicatedLines,
            OverallDuplicatedPercent = overallPercent,
            Files = fileMetrics
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        Console.WriteLine(
            $"Internal duplication summary ({policyRule.Rule.Id}): {FormatPercent(overallPercent)}% ({totalDuplicatedLines}/{totalComparableLines} significant lines) [threshold {FormatPercent(maxDuplicationPercent)}%].");
        return new DuplicationRuleEvaluationResult(findings, metrics);
    }

    private static int ResolveDuplicationWindowLines(AnalysisRule rule, List<string> warnings) {
        var window = DefaultDuplicationWindowLines;
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return window;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(DuplicationWindowLinesTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var value = tag.Substring(DuplicationWindowLinesTagPrefix.Length).Trim();
            if (int.TryParse(value, out var parsed) && parsed >= 2) {
                return parsed;
            }
            warnings.Add(
                $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{DuplicationWindowLinesTagPrefix}<int>=2'; using {DefaultDuplicationWindowLines}.");
            return window;
        }
        return window;
    }

    private static double ResolveMaxDuplicationPercent(AnalysisRule rule, List<string> warnings) {
        var value = DefaultMaxDuplicationPercent;
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return value;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(MaxDuplicationPercentTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var raw = tag.Substring(MaxDuplicationPercentTagPrefix.Length).Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                parsed is >= 0 and <= 100) {
                return parsed;
            }
            warnings.Add(
                $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{MaxDuplicationPercentTagPrefix}<0-100>'; using {FormatPercent(DefaultMaxDuplicationPercent)}.");
            return value;
        }
        return value;
    }

    private static IReadOnlyDictionary<string, double> ResolveMaxDuplicationPercentByLanguage(AnalysisRule rule,
        List<string> warnings) {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return map;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(MaxDuplicationPercentByLanguageTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var payload = tag.Substring(MaxDuplicationPercentByLanguageTagPrefix.Length).Trim();
            var split = payload.IndexOf(':');
            if (split <= 0 || split >= payload.Length - 1) {
                warnings.Add(
                    $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{MaxDuplicationPercentByLanguageTagPrefix}<language>:<0-100>'; tag ignored.");
                continue;
            }

            var languageRaw = payload.Substring(0, split).Trim();
            var valueRaw = payload.Substring(split + 1).Trim();
            var language = NormalizeDuplicationLanguage(languageRaw);
            if (string.IsNullOrWhiteSpace(language)) {
                warnings.Add(
                    $"Rule {rule.Id} has unsupported duplication language in tag '{tag}'. Supported: csharp, powershell, javascript, typescript, python.");
                continue;
            }
            if (!double.TryParse(valueRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                parsed is < 0 or > 100) {
                warnings.Add(
                    $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{MaxDuplicationPercentByLanguageTagPrefix}<language>:<0-100>'; tag ignored.");
                continue;
            }

            map[language] = parsed;
        }
        return map;
    }

    private static double ResolveMaxDuplicationPercentForLanguage(string language, double fallback,
        IReadOnlyDictionary<string, double> perLanguageOverrides) {
        if (perLanguageOverrides is null || perLanguageOverrides.Count == 0) {
            return fallback;
        }
        var key = NormalizeDuplicationLanguage(language);
        if (!string.IsNullOrWhiteSpace(key) && perLanguageOverrides.TryGetValue(key, out var value)) {
            return value;
        }
        return fallback;
    }

    private static string? NormalizeDuplicationLanguage(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "cs" or "csharp" => "csharp",
            "ps" or "powershell" => "powershell",
            "js" or "javascript" => "javascript",
            "ts" or "typescript" => "typescript",
            "py" or "python" => "python",
            _ => null
        };
    }

    private static DuplicationRuleMetrics BuildEmptyDuplicationRuleMetrics(AnalysisPolicyRule policyRule, int windowLines = 0,
        double maxDuplicationPercent = 0) {
        var emittedRuleId = string.IsNullOrWhiteSpace(policyRule.Rule.ToolRuleId)
            ? policyRule.Rule.Id
            : policyRule.Rule.ToolRuleId;
        var emittedTool = string.IsNullOrWhiteSpace(policyRule.Rule.Tool)
            ? InternalToolName
            : policyRule.Rule.Tool;
        return new DuplicationRuleMetrics {
            RuleId = emittedRuleId,
            Tool = emittedTool,
            WindowLines = windowLines,
            ConfiguredMaxPercent = maxDuplicationPercent,
            TotalSignificantLines = 0,
            DuplicatedSignificantLines = 0,
            OverallDuplicatedPercent = 0,
            Files = new List<DuplicationFileMetrics>()
        };
    }

    private static List<SignificantLine> ReadSignificantLines(SourceFileEntry sourceFile, List<string> warnings) {
        try {
            var content = File.ReadAllText(sourceFile.FullPath);
            var extension = Path.GetExtension(sourceFile.FullPath);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromRoslynTokens(content);
            }
            if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".psd1", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromPowerShellTokens(content);
            }
            if (extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cjs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tsx", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromJavaScriptTokens(content);
            }
            if (extension.Equals(".py", StringComparison.OrdinalIgnoreCase)) {
                return BuildSignificantLinesFromPythonTokens(content);
            }
            return BuildSignificantLinesFallback(content);
        } catch (Exception ex) {
            warnings.Add($"Failed to read file for duplication check ({sourceFile.RelativePath}): {ex.Message}");
            return new List<SignificantLine>();
        }
    }

    private static List<SignificantLine> BuildSignificantLinesFromRoslynTokens(string content) {
        var lines = new Dictionary<int, List<string>>();
        var tree = CSharpSyntaxTree.ParseText(content ?? string.Empty);
        var root = tree.GetRoot();
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false)) {
            if (ShouldSkipDuplicationToken(token)) {
                continue;
            }
            var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (line <= 0) {
                continue;
            }
            if (!lines.TryGetValue(line, out var tokens)) {
                tokens = new List<string>();
                lines[line] = tokens;
            }
            tokens.Add(NormalizeDuplicationToken(token));
        }

        var significant = new List<SignificantLine>(lines.Count);
        foreach (var entry in lines.OrderBy(item => item.Key)) {
            if (entry.Value.Count == 0) {
                continue;
            }
            significant.Add(new SignificantLine(entry.Key, string.Join(" ", entry.Value)));
        }
        return significant;
    }

    private static bool ShouldSkipDuplicationToken(SyntaxToken token) {
        if (token.IsKind(SyntaxKind.OpenBraceToken) ||
            token.IsKind(SyntaxKind.CloseBraceToken) ||
            token.IsKind(SyntaxKind.SemicolonToken)) {
            return true;
        }
        var parent = token.Parent;
        if (parent is null) {
            return true;
        }
        if (parent is UsingDirectiveSyntax || parent is BaseNamespaceDeclarationSyntax) {
            return true;
        }
        return false;
    }

    private static string NormalizeDuplicationToken(SyntaxToken token) {
        if (token.IsKind(SyntaxKind.IdentifierToken)) {
            return "__ID__";
        }
        if (token.IsKind(SyntaxKind.NumericLiteralToken) ||
            token.IsKind(SyntaxKind.StringLiteralToken) ||
            token.IsKind(SyntaxKind.CharacterLiteralToken) ||
            token.IsKind(SyntaxKind.Utf8StringLiteralToken)) {
            return "__LIT__";
        }
        if (token.IsKind(SyntaxKind.InterpolatedStringTextToken)) {
            return "__LIT__";
        }
        var kindName = token.Kind().ToString();
        if (kindName.EndsWith("Token", StringComparison.Ordinal)) {
            return kindName.Substring(0, kindName.Length - "Token".Length);
        }
        return kindName;
    }

    private static List<SignificantLine> BuildSignificantLinesFromPowerShellTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            if (!TryStripPowerShellComment(lines[index], out var stripped)) {
                stripped = lines[index] ?? string.Empty;
            }

            var normalizedTokens = new List<string>();
            foreach (Match match in PowerShellTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizePowerShellToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }
        return result;
    }

    private static bool TryStripPowerShellComment(string input, out string stripped) {
        stripped = input ?? string.Empty;
        if (stripped.Length == 0) {
            return false;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < stripped.Length; i++) {
            var ch = stripped[i];
            if (ch == '\'' && !inDoubleQuote) {
                if (inSingleQuote && i + 1 < stripped.Length && stripped[i + 1] == '\'') {
                    i++;
                    continue;
                }
                inSingleQuote = !inSingleQuote;
                continue;
            }
            if (ch == '"' && !inSingleQuote) {
                var escaped = i > 0 && stripped[i - 1] == '`';
                if (!escaped) {
                    inDoubleQuote = !inDoubleQuote;
                }
                continue;
            }
            if (ch == '#' && !inSingleQuote && !inDoubleQuote) {
                stripped = stripped.Substring(0, i);
                return true;
            }
        }

        return false;
    }

    private static string NormalizePowerShellToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsPowerShellStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (trimmed.StartsWith("$", StringComparison.Ordinal)) {
            return "__ID__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_') {
            if (PowerShellKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsPowerShellStructureOnlyToken(string token) {
        return token is "{" or "}" or ";" or "(" or ")" or "[" or "]" or ",";
    }

    private static List<SignificantLine> BuildSignificantLinesFromJavaScriptTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inBlockComment = false;
        for (var index = 0; index < lines.Length; index++) {
            var stripped = StripJavaScriptComments(lines[index] ?? string.Empty, ref inBlockComment);
            var normalizedTokens = new List<string>();
            foreach (Match match in JavaScriptTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizeJavaScriptToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }

        return result;
    }

    private static string StripJavaScriptComments(string input, ref bool inBlockComment) {
        if (string.IsNullOrEmpty(input)) {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inTemplateString = false;
        for (var i = 0; i < input.Length; i++) {
            var ch = input[i];
            var next = i + 1 < input.Length ? input[i + 1] : '\0';

            if (inBlockComment) {
                if (ch == '*' && next == '/') {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && !inTemplateString) {
                if (ch == '/' && next == '/') {
                    break;
                }
                if (ch == '/' && next == '*') {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            if (ch == '\'' && !inDoubleQuote && !inTemplateString) {
                var escaped = i > 0 && input[i - 1] == '\\';
                if (!escaped) {
                    inSingleQuote = !inSingleQuote;
                }
                sb.Append(ch);
                continue;
            }

            if (ch == '"' && !inSingleQuote && !inTemplateString) {
                var escaped = i > 0 && input[i - 1] == '\\';
                if (!escaped) {
                    inDoubleQuote = !inDoubleQuote;
                }
                sb.Append(ch);
                continue;
            }

            if (ch == '`' && !inSingleQuote && !inDoubleQuote) {
                var escaped = i > 0 && input[i - 1] == '\\';
                if (!escaped) {
                    inTemplateString = !inTemplateString;
                }
                sb.Append(ch);
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string NormalizeJavaScriptToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsJavaScriptStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("`", StringComparison.Ordinal) && trimmed.EndsWith("`", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_' || trimmed[0] == '$') {
            if (JavaScriptKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsJavaScriptStructureOnlyToken(string token) {
        return token is "{" or "}" or ";" or "(" or ")" or "[" or "]" or "," or ".";
    }

    private static List<SignificantLine> BuildSignificantLinesFromPythonTokens(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            if (!TryStripPythonComment(lines[index], out var stripped)) {
                stripped = lines[index] ?? string.Empty;
            }

            var normalizedTokens = new List<string>();
            foreach (Match match in PythonTokenRegex.Matches(stripped)) {
                if (!match.Success) {
                    continue;
                }
                var normalized = NormalizePythonToken(match.Value);
                if (string.IsNullOrWhiteSpace(normalized)) {
                    continue;
                }
                normalizedTokens.Add(normalized);
            }
            if (normalizedTokens.Count == 0) {
                continue;
            }

            result.Add(new SignificantLine(index + 1, string.Join(" ", normalizedTokens)));
        }
        return result;
    }

    private static bool TryStripPythonComment(string input, out string stripped) {
        stripped = input ?? string.Empty;
        if (stripped.Length == 0) {
            return false;
        }

        var inSingleQuote = false;
        var inDoubleQuote = false;
        for (var i = 0; i < stripped.Length; i++) {
            var ch = stripped[i];
            if (ch == '\'' && !inDoubleQuote) {
                var escaped = i > 0 && stripped[i - 1] == '\\';
                if (!escaped) {
                    inSingleQuote = !inSingleQuote;
                }
                continue;
            }
            if (ch == '"' && !inSingleQuote) {
                var escaped = i > 0 && stripped[i - 1] == '\\';
                if (!escaped) {
                    inDoubleQuote = !inDoubleQuote;
                }
                continue;
            }
            if (ch == '#' && !inSingleQuote && !inDoubleQuote) {
                stripped = stripped.Substring(0, i);
                return true;
            }
        }

        return false;
    }

    private static string NormalizePythonToken(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }
        var trimmed = token.Trim();
        if (trimmed.Length == 0 || IsPythonStructureOnlyToken(trimmed)) {
            return string.Empty;
        }
        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal)) ||
            (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)) ||
            double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) {
            return "__LIT__";
        }
        if (char.IsLetter(trimmed[0]) || trimmed[0] == '_') {
            if (PythonKeywordTokens.Contains(trimmed)) {
                return trimmed.ToLowerInvariant();
            }
            return "__ID__";
        }
        return trimmed;
    }

    private static bool IsPythonStructureOnlyToken(string token) {
        return token is "(" or ")" or "[" or "]" or "{" or "}" or "," or "." or ":";
    }

    private static List<SignificantLine> BuildSignificantLinesFallback(string content) {
        var result = new List<SignificantLine>();
        var lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var index = 0; index < lines.Length; index++) {
            var normalized = lines[index]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("//", StringComparison.Ordinal) ||
                normalized.StartsWith("#", StringComparison.Ordinal)) {
                continue;
            }
            result.Add(new SignificantLine(index + 1, normalized));
        }
        return result;
    }

    private static string BuildWindowSignature(IReadOnlyList<SignificantLine> lines, int startIndex, int windowSize) {
        var builder = new StringBuilder(windowSize * 24);
        for (var i = 0; i < windowSize; i++) {
            if (i > 0) {
                builder.Append('\n');
            }
            builder.Append(lines[startIndex + i].Value);
        }
        return builder.ToString();
    }

    private static bool HasDistinctWindowOccurrences(IReadOnlyList<WindowOccurrence> occurrences) {
        if (occurrences is null || occurrences.Count < 2) {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences) {
            seen.Add($"{occurrence.FileIndex}:{occurrence.StartIndex}");
            if (seen.Count > 1) {
                return true;
            }
        }
        return false;
    }

    private static double ComputePercent(int part, int whole) {
        if (whole <= 0) {
            return 0;
        }
        return Math.Round((part * 100.0) / whole, 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatPercent(double value) {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static bool IsMaxLinesRule(AnalysisRule rule) {
        if (rule is null) {
            return false;
        }
        if (rule.Id.Equals(InternalMaxLinesRuleId, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return HasTagWithPrefix(rule.Tags, MaxLinesTagPrefix);
    }

    private static bool IsDuplicationRule(AnalysisRule rule) {
        if (rule is null) {
            return false;
        }
        if (rule.Id.Equals(InternalDuplicationRuleId, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return HasTagWithPrefix(rule.Tags, DuplicationWindowLinesTagPrefix) ||
            HasTagWithPrefix(rule.Tags, MaxDuplicationPercentTagPrefix);
    }

    private static IReadOnlyCollection<string> ResolveGeneratedSuffixes(AnalysisRule rule, List<string> warnings) {
        var suffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        if (rule?.Tags is not null && rule.Tags.Count > 0) {
            foreach (var tag in rule.Tags) {
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(GeneratedSuffixTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var value = NormalizeGeneratedSuffixTagValue(tag.Substring(GeneratedSuffixTagPrefix.Length));
                if (!string.IsNullOrWhiteSpace(value)) {
                    suffixes.Add(value);
                } else {
                    malformedTags.Add(tag);
                }
            }
        }
        AddMalformedTagWarning(rule?.Id, malformedTags, GeneratedSuffixTagPrefix, warnings);

        return suffixes;
    }

    private static string? NormalizeGeneratedSuffixTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim();
        while (value.StartsWith("*", StringComparison.Ordinal)) {
            value = value.Substring(1);
        }
        while (value.StartsWith("/", StringComparison.Ordinal)) {
            value = value.Substring(1);
        }
        if (value.Length == 0) {
            return null;
        }
        if (!value.StartsWith(".", StringComparison.Ordinal)) {
            value = "." + value;
        }
        return value;
    }

    private static IReadOnlyCollection<string> ResolveGeneratedHeaderMarkers(AnalysisRule rule, List<string> warnings) {
        var markers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        if (rule?.Tags is not null && rule.Tags.Count > 0) {
            foreach (var tag in rule.Tags) {
                if (string.IsNullOrWhiteSpace(tag) || !tag.StartsWith(GeneratedMarkerTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                var marker = NormalizeGeneratedHeaderMarkerTagValue(tag.Substring(GeneratedMarkerTagPrefix.Length));
                if (!string.IsNullOrWhiteSpace(marker)) {
                    markers.Add(marker);
                } else {
                    malformedTags.Add(tag);
                }
            }
        }
        AddMalformedTagWarning(rule?.Id, malformedTags, GeneratedMarkerTagPrefix, warnings);
        return markers;
    }

    private static int ResolveGeneratedHeaderLinesToInspect(AnalysisRule rule, List<string> warnings) {
        if (rule is null || rule.Tags is null || rule.Tags.Count == 0) {
            return GeneratedHeaderLinesToInspect;
        }
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(GeneratedHeaderLinesTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var value = tag.Substring(GeneratedHeaderLinesTagPrefix.Length).Trim();
            if (int.TryParse(value, out var parsed) && parsed >= 0) {
                return parsed;
            }
            warnings.Add(
                $"Rule {rule.Id} has malformed tag '{tag}'. Expected '{GeneratedHeaderLinesTagPrefix}<non-negative-int>'; using {GeneratedHeaderLinesToInspect}.");
            return GeneratedHeaderLinesToInspect;
        }
        return GeneratedHeaderLinesToInspect;
    }

    private static void ValidateInternalMaintainabilityTags(AnalysisRule rule, IReadOnlyCollection<string> supportedPrefixes,
        List<string> warnings) {
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return;
        }

        var unknownTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedPrefixes = (supportedPrefixes ?? Array.Empty<string>())
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .ToArray();
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }
            var isSupported = normalizedPrefixes.Any(prefix => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (isSupported) {
                continue;
            }
            if (!tag.Contains(':', StringComparison.Ordinal)) {
                continue;
            }
            unknownTags.Add(tag);
        }
        if (unknownTags.Count == 0) {
            return;
        }

        var sample = string.Join(", ", unknownTags.Take(MaxTagWarningDetails).Select(tag => $"'{tag}'"));
        var suffix = unknownTags.Count > MaxTagWarningDetails
            ? $" (+{unknownTags.Count - MaxTagWarningDetails} more)"
            : string.Empty;
        var supported = normalizedPrefixes.Length == 0 ? "<none>" : string.Join(", ", normalizedPrefixes);
        warnings.Add(
            $"Rule {rule.Id} has unknown maintainability tags: {sample}{suffix}. Supported prefixes: {supported}.");
    }

    private static void AddMalformedTagWarning(string? ruleId, IReadOnlyList<string> malformedTags, string expectedPrefix,
        List<string> warnings) {
        if (malformedTags is null || malformedTags.Count == 0) {
            return;
        }

        var sample = string.Join(", ", malformedTags.Take(MaxTagWarningDetails).Select(tag => $"'{tag}'"));
        var suffix = malformedTags.Count > MaxTagWarningDetails
            ? $" (+{malformedTags.Count - MaxTagWarningDetails} more)"
            : string.Empty;
        warnings.Add(
            $"Rule {ruleId ?? "<unknown>"} has malformed tags: {sample}{suffix}. Expected '{expectedPrefix}<value>'.");
    }

    private static IReadOnlySet<string> ResolveExcludedDirectorySegments(AnalysisRule rule, List<string> warnings) {
        var segments = new HashSet<string>(DefaultExcludedDirectorySegments, StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return segments;
        }

        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(ExcludedDirectoryTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var segment = NormalizeExcludedDirectoryTagValue(tag.Substring(ExcludedDirectoryTagPrefix.Length));
            if (!string.IsNullOrWhiteSpace(segment)) {
                segments.Add(segment);
            } else {
                malformedTags.Add(tag);
            }
        }
        AddMalformedTagWarning(rule.Id, malformedTags, ExcludedDirectoryTagPrefix, warnings);

        return segments;
    }

    private static IReadOnlySet<string> ResolveIncludedSourceExtensionsForRules(IEnumerable<AnalysisRule> rules,
        List<string> warnings) {
        var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rules is not null) {
            foreach (var rule in rules.Where(static item => item is not null)) {
                foreach (var extension in ResolveIncludedSourceExtensionsForRule(rule, warnings)) {
                    union.Add(extension);
                }
            }
        }

        if (union.Count == 0) {
            foreach (var extension in DefaultIncludedSourceExtensions) {
                union.Add(extension);
            }
        }
        return union;
    }

    private static IReadOnlySet<string> ResolveIncludedSourceExtensionsForRule(AnalysisRule rule, List<string> warnings) {
        var defaults = new HashSet<string>(DefaultIncludedSourceExtensions, StringComparer.OrdinalIgnoreCase);
        if (rule?.Tags is null || rule.Tags.Count == 0) {
            return defaults;
        }

        var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformedTags = new List<string>();
        var sawIncludeTag = false;
        foreach (var tag in rule.Tags) {
            if (string.IsNullOrWhiteSpace(tag) ||
                !tag.StartsWith(IncludeExtensionTagPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            sawIncludeTag = true;
            var normalized = NormalizeIncludeExtensionTagValue(tag.Substring(IncludeExtensionTagPrefix.Length));
            if (!string.IsNullOrWhiteSpace(normalized)) {
                configured.Add(normalized);
            } else {
                malformedTags.Add(tag);
            }
        }

        AddMalformedTagWarning(rule.Id, malformedTags, IncludeExtensionTagPrefix, warnings);
        if (sawIncludeTag && configured.Count > 0) {
            return configured;
        }
        return defaults;
    }

    private static IReadOnlyList<SourceFileEntry> FilterSourceFilesForRule(AnalysisRule rule,
        IReadOnlyList<SourceFileEntry> sourceFiles, string? excludedOutputPath, List<string> warnings) {
        if (sourceFiles is null || sourceFiles.Count == 0) {
            return Array.Empty<SourceFileEntry>();
        }

        var includedExtensions = ResolveIncludedSourceExtensionsForRule(rule, warnings);
        var generatedSuffixes = ResolveGeneratedSuffixes(rule, warnings);
        var generatedHeaderMarkers = ResolveGeneratedHeaderMarkers(rule, warnings);
        var generatedHeaderLinesToInspect = ResolveGeneratedHeaderLinesToInspect(rule, warnings);
        var excludedDirectorySegments = ResolveExcludedDirectorySegments(rule, warnings);

        return sourceFiles
            .Where(file => IsPathInIncludedExtensions(file.RelativePath, includedExtensions))
            .Where(file => !IsExcludedSourceFile(file.FullPath, file.RelativePath, generatedSuffixes, generatedHeaderMarkers,
                excludedDirectorySegments, generatedHeaderLinesToInspect, excludedOutputPath))
            .ToList();
    }

    private static bool IsPathInIncludedExtensions(string path, IReadOnlySet<string> includedExtensions) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return includedExtensions?.Contains(extension) == true;
    }

    private static string? NormalizeIncludeExtensionTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim().Replace('*', ' ').Trim();
        if (value.Length == 0) {
            return null;
        }
        if (value.StartsWith("/", StringComparison.Ordinal)) {
            return null;
        }
        if (!value.StartsWith(".", StringComparison.Ordinal)) {
            value = "." + value;
        }
        return value.ToLowerInvariant();
    }

    private static string? NormalizeExcludedDirectoryTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim().Replace('\\', '/').Trim('/');
        if (value.Length == 0) {
            return null;
        }
        if (value.Contains('/', StringComparison.Ordinal)) {
            return null;
        }
        return value;
    }

    private static string? NormalizeGeneratedHeaderMarkerTagValue(string rawValue) {
        if (string.IsNullOrWhiteSpace(rawValue)) {
            return null;
        }
        var value = rawValue.Trim();
        return value.Length == 0 ? null : value;
    }

    private static IEnumerable<string> EnumerateSourceFiles(string workspace, IReadOnlySet<string> includedExtensions,
        IReadOnlySet<string> excludedDirectorySegments, string? excludedOutputPath, List<string> warnings) {
        var pending = new Stack<string>();
        pending.Push(workspace);
        var normalizedExtensions = new HashSet<string>(
            includedExtensions
            .Where(static ext => !string.IsNullOrWhiteSpace(ext))
            .Select(static ext => ext.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        if (normalizedExtensions.Count == 0) {
            foreach (var extension in DefaultIncludedSourceExtensions) {
                normalizedExtensions.Add(extension);
            }
        }

        while (pending.Count > 0) {
            var currentDirectory = pending.Pop();

            IEnumerable<string> subdirectories;
            try {
                subdirectories = Directory.EnumerateDirectories(currentDirectory);
            } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException) {
                var relativePath = Path.GetRelativePath(workspace, currentDirectory).Replace('\\', '/');
                warnings.Add($"Skipped inaccessible directory during line-count scan ({relativePath}): {ex.Message}");
                continue;
            }

            foreach (var subdirectory in subdirectories) {
                if (!IsExcludedDirectory(workspace, subdirectory, excludedDirectorySegments, excludedOutputPath)) {
                    pending.Push(subdirectory);
                }
            }

            IEnumerable<string> files;
            try {
                files = Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly);
            } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException) {
                var relativePath = Path.GetRelativePath(workspace, currentDirectory).Replace('\\', '/');
                warnings.Add($"Skipped inaccessible directory during line-count scan ({relativePath}): {ex.Message}");
                continue;
            }

            foreach (var file in files) {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (!normalizedExtensions.Contains(extension)) {
                    continue;
                }
                yield return file;
            }
        }
    }

    private static bool IsExcludedDirectory(string workspace, string fullPath, IReadOnlySet<string> excludedDirectorySegments,
        string? excludedOutputPath) {
        var relativePath = Path.GetRelativePath(workspace, fullPath).Replace('\\', '/');
        return ContainsExcludedDirectorySegment(relativePath, excludedDirectorySegments) ||
            IsPathUnderRelativeRoot(relativePath, excludedOutputPath);
    }

    private static bool IsExcludedSourceFile(string fullPath, string relativePath, IReadOnlyCollection<string> generatedSuffixes,
        IReadOnlyCollection<string> generatedHeaderMarkers, IReadOnlySet<string> excludedDirectorySegments,
        int generatedHeaderLinesToInspect, string? excludedOutputPath) {
        if (string.IsNullOrWhiteSpace(relativePath)) {
            return true;
        }
        if (ContainsExcludedDirectorySegment(relativePath, excludedDirectorySegments)) {
            return true;
        }
        if (IsPathUnderRelativeRoot(relativePath, excludedOutputPath)) {
            return true;
        }

        var normalized = relativePath.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        if ((generatedSuffixes ?? Array.Empty<string>()).Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) {
            return true;
        }

        return HasGeneratedFileHeader(fullPath, generatedHeaderMarkers, generatedHeaderLinesToInspect);
    }

    private static bool ContainsExcludedDirectorySegment(string relativePath, IReadOnlySet<string> excludedDirectorySegments) {
        var segments = relativePath
            .Replace('\\', '/')
            .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => excludedDirectorySegments.Contains(segment));
    }

    private static string? TryGetRelativePathWithinWorkspace(string workspace, string path) {
        if (string.IsNullOrWhiteSpace(workspace) || string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        try {
            var workspaceFullPath = Path.GetFullPath(workspace);
            var candidateFullPath = Path.GetFullPath(path);
            var relativePath = Path.GetRelativePath(workspaceFullPath, candidateFullPath).Replace('\\', '/');
            if (relativePath.StartsWith("../", StringComparison.Ordinal) || relativePath.Equals("..", StringComparison.Ordinal)) {
                return null;
            }
            return relativePath.Trim('/');
        } catch {
            return null;
        }
    }

    private static bool IsPathUnderRelativeRoot(string relativePath, string? rootRelativePath) {
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(rootRelativePath)) {
            return false;
        }
        var normalizedPath = relativePath.Replace('\\', '/').Trim('/');
        var normalizedRoot = rootRelativePath.Replace('\\', '/').Trim('/');
        if (normalizedPath.Length == 0 || normalizedRoot.Length == 0) {
            return false;
        }
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGeneratedFileHeader(string fullPath, IReadOnlyCollection<string> generatedHeaderMarkers,
        int generatedHeaderLinesToInspect) {
        if (generatedHeaderLinesToInspect <= 0) {
            return false;
        }
        try {
            using var reader = new StreamReader(fullPath);
            var inBlockComment = false;
            for (var i = 0; i < generatedHeaderLinesToInspect; i++) {
                var line = reader.ReadLine();
                if (line is null) {
                    break;
                }
                var normalized = line.Trim();
                if (normalized.Length == 0) {
                    continue;
                }

                var isLineComment = normalized.StartsWith("//", StringComparison.Ordinal);
                if (normalized.StartsWith("/*", StringComparison.Ordinal)) {
                    inBlockComment = true;
                }
                var isCommentContext = inBlockComment || isLineComment || normalized.StartsWith("*", StringComparison.Ordinal);
                if (!isCommentContext) {
                    break;
                }

                foreach (var marker in generatedHeaderMarkers ?? Array.Empty<string>()) {
                    if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
                        return true;
                    }
                }

                if (inBlockComment && normalized.Contains("*/", StringComparison.Ordinal)) {
                    inBlockComment = false;
                }
            }
        } catch {
            // Treat read failures as non-generated here; caller will report read failure during counting.
        }
        return false;
    }

    private static string ResolveLanguageFromPath(string? path) {
        var extension = Path.GetExtension(path ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension)) {
            return "unknown";
        }
        return extension.ToLowerInvariant() switch {
            ".cs" => "csharp",
            ".ps1" => "powershell",
            ".psm1" => "powershell",
            ".psd1" => "powershell",
            ".js" => "javascript",
            ".jsx" => "javascript",
            ".mjs" => "javascript",
            ".cjs" => "javascript",
            ".ts" => "typescript",
            ".tsx" => "typescript",
            ".py" => "python",
            _ => "unknown"
        };
    }

    private static bool HasTagWithPrefix(IReadOnlyList<string>? tags, string prefix) {
        if (tags is null || tags.Count == 0 || string.IsNullOrWhiteSpace(prefix)) {
            return false;
        }
        foreach (var tag in tags) {
            if (!string.IsNullOrWhiteSpace(tag) && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static int CountFileLines(string path) {
        var count = 0;
        using var reader = new StreamReader(path);
        while (reader.ReadLine() is not null) {
            count++;
        }
        return count;
    }

    private static string? NormalizeSeverity(string? severity) {
        if (string.IsNullOrWhiteSpace(severity)) {
            return "warning";
        }
        return severity.Trim().ToLowerInvariant() switch {
            "none" => null,
            "off" => null,
            "disable" => null,
            "disabled" => null,
            "suppress" => null,
            "critical" => "error",
            "high" => "error",
            "error" => "error",
            "warning" => "warning",
            "warn" => "warning",
            "medium" => "warning",
            "info" => "info",
            "information" => "info",
            "low" => "info",
            _ => "warning"
        };
    }

    private sealed class SourceFileEntry {
        public string FullPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
    }

    private sealed record InternalMaintainabilityResult(
        IReadOnlyList<AnalysisFindingItem> Findings,
        IReadOnlyList<DuplicationRuleMetrics> DuplicationRuleMetrics);

    private sealed record DuplicationEvaluationResult(
        IReadOnlyList<AnalysisFindingItem> Findings,
        IReadOnlyList<DuplicationRuleMetrics> RuleMetrics);

    private sealed record DuplicationRuleEvaluationResult(
        IReadOnlyList<AnalysisFindingItem> Findings,
        DuplicationRuleMetrics RuleMetrics);

    private sealed record SignificantLine(int OriginalLine, string Value);
    private sealed record DuplicationSourceFile(SourceFileEntry Source, IReadOnlyList<SignificantLine> SignificantLines);
    private sealed record WindowOccurrence(int FileIndex, int StartIndex);
}

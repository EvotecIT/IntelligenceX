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
    private const string InternalWriteToolSchemaRuleId = "IXTOOL001";
    private const string InternalAdRequiredDomainHelperRuleId = "IXTOOL002";
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
    private static readonly string[] WriteToolSchemaSupportedTagPrefixes = {
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
        var writeToolSchemaRules = rules
            .Where(rule => rule?.Rule is not null && IsWriteToolSchemaRule(rule.Rule))
            .ToList();
        var adRequiredDomainHelperRules = rules
            .Where(rule => rule?.Rule is not null && IsAdRequiredDomainHelperRule(rule.Rule))
            .ToList();
        if (maxLinesRules.Count == 0 && duplicationRules.Count == 0 && writeToolSchemaRules.Count == 0 &&
            adRequiredDomainHelperRules.Count == 0) {
            return new InternalMaintainabilityResult(findings, duplicationRuleMetrics);
        }

        // Build a candidate source set once; each rule applies its own filtering tags on top.
        var includedSourceExtensions = ResolveIncludedSourceExtensionsForRules(
            maxLinesRules.Concat(duplicationRules).Concat(writeToolSchemaRules).Concat(adRequiredDomainHelperRules)
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
        findings.AddRange(RunWriteToolSchemaChecks(writeToolSchemaRules, sourceFiles, excludedOutputPath, warnings));
        findings.AddRange(
            RunAdRequiredDomainHelperChecks(adRequiredDomainHelperRules, sourceFiles, excludedOutputPath, warnings));

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

        var duplicatedWindowGroups = 0;
        var duplicatedWindowOccurrences = 0;
        foreach (var occurrenceGroup in signatures.Values) {
            if (occurrenceGroup.Count <= 1) {
                continue;
            }
            if (!HasDistinctWindowOccurrences(occurrenceGroup)) {
                continue;
            }

            duplicatedWindowGroups++;
            duplicatedWindowOccurrences += occurrenceGroup.Count;
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
                $"{emittedRuleId}:{file.Source.RelativePath}:{duplicatedLines}:{file.SignificantLines.Count}:{windowLines}";
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
            DuplicatedWindowGroups = duplicatedWindowGroups,
            DuplicatedWindowOccurrences = duplicatedWindowOccurrences,
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
            DuplicatedWindowGroups = 0,
            DuplicatedWindowOccurrences = 0,
            Files = new List<DuplicationFileMetrics>()
        };
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
            HasTagWithPrefix(rule.Tags, MaxDuplicationPercentTagPrefix) ||
            HasTagWithPrefix(rule.Tags, MaxDuplicationPercentByLanguageTagPrefix);
    }

    private static bool IsWriteToolSchemaRule(AnalysisRule rule) {
        if (rule is null) {
            return false;
        }
        return rule.Id.Equals(InternalWriteToolSchemaRuleId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAdRequiredDomainHelperRule(AnalysisRule rule) {
        if (rule is null) {
            return false;
        }
        return rule.Id.Equals(InternalAdRequiredDomainHelperRuleId, StringComparison.OrdinalIgnoreCase);
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

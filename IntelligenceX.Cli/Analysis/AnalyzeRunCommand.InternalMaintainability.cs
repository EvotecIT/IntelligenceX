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
    private const string InternalMaxResultsMetaHelperRuleId = "IXTOOL003";
    private const string InternalCanonicalBoundedIntHelperRuleId = "IXTOOL004";
    private const string InternalEventLogMaxResultsHelperRuleId = "IXTOOL005";
    private const string DuplicationWindowLinesTagPrefix = "dup-window-lines:";
    private const string MaxDuplicationPercentTagPrefix = "max-duplication-percent:";
    private const string MaxDuplicationPercentByLanguageTagPrefix = "max-duplication-percent-";
    private const string IncludeExtensionTagPrefix = "include-ext:";
    private const int DefaultDuplicationWindowLines = 8;
    private const double DefaultMaxDuplicationPercent = 25.0;
    private static readonly string[] DefaultIncludedSourceExtensions = BuildDefaultIncludedSourceExtensions();

    private static string[] BuildDefaultIncludedSourceExtensions() {
        var extensions = new List<string>(
            SourceLanguageConventions.CSharpSourceExtensions.Length +
            SourceLanguageConventions.PowerShellSourceExtensions.Length +
            SourceLanguageConventions.JavaScriptSourceExtensions.Length +
            SourceLanguageConventions.PythonSourceExtensions.Length);
        extensions.AddRange(SourceLanguageConventions.CSharpSourceExtensions);
        extensions.AddRange(SourceLanguageConventions.PowerShellSourceExtensions);
        extensions.AddRange(SourceLanguageConventions.JavaScriptSourceExtensions);
        extensions.AddRange(SourceLanguageConventions.PythonSourceExtensions);
        return extensions.ToArray();
    }

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
    private static readonly Func<AnalysisRule, bool> NeverMatchInternalRule = static _ => false;
    // Handler order defines first-match precedence when predicates overlap.
    private static readonly InternalMaintainabilityRuleHandler[] InternalMaintainabilityRuleHandlers = {
        new(
            "max-lines",
            new[] { InternalMaxLinesRuleId },
            IsMaxLinesRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) =>
            new InternalMaintainabilityResult(
                RunMaxLinesChecks(rules, sourceFiles, excludedOutputPath, warnings),
                Array.Empty<DuplicationRuleMetrics>())),
        new(
            "duplication",
            new[] { InternalDuplicationRuleId },
            IsDuplicationRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) => {
            var result = RunDuplicationChecks(rules, sourceFiles, excludedOutputPath, warnings);
            return new InternalMaintainabilityResult(result.Findings, result.RuleMetrics);
        }),
        new(
            "write-tool-schema",
            new[] { InternalWriteToolSchemaRuleId },
            NeverMatchInternalRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) =>
            new InternalMaintainabilityResult(
                RunWriteToolSchemaChecks(rules, sourceFiles, excludedOutputPath, warnings),
                Array.Empty<DuplicationRuleMetrics>())),
        new(
            "ad-required-domain-helper",
            new[] { InternalAdRequiredDomainHelperRuleId },
            NeverMatchInternalRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) =>
            new InternalMaintainabilityResult(
                RunAdRequiredDomainHelperChecks(rules, sourceFiles, excludedOutputPath, warnings),
                Array.Empty<DuplicationRuleMetrics>())),
        new(
            "max-results-meta-helper",
            new[] { InternalMaxResultsMetaHelperRuleId },
            NeverMatchInternalRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) =>
            new InternalMaintainabilityResult(
                RunMaxResultsMetaHelperChecks(rules, sourceFiles, excludedOutputPath, warnings),
                Array.Empty<DuplicationRuleMetrics>())),
        new(
            "canonical-bounded-int-helper",
            new[] { InternalCanonicalBoundedIntHelperRuleId },
            NeverMatchInternalRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) =>
            new InternalMaintainabilityResult(
                RunCanonicalBoundedIntHelperChecks(rules, sourceFiles, excludedOutputPath, warnings),
                Array.Empty<DuplicationRuleMetrics>())),
        new(
            "eventlog-max-results-helper",
            new[] { InternalEventLogMaxResultsHelperRuleId },
            NeverMatchInternalRule,
            static (rules, sourceFiles, excludedOutputPath, warnings) =>
            new InternalMaintainabilityResult(
                RunEventLogMaxResultsHelperChecks(rules, sourceFiles, excludedOutputPath, warnings),
                Array.Empty<DuplicationRuleMetrics>()))
    };
    private static readonly IReadOnlyDictionary<string, int> InternalMaintainabilityRuleIdToHandlerIndex =
        BuildInternalMaintainabilityRuleIdToHandlerIndex();
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

        var mappedRulesByHandler = new List<AnalysisPolicyRule>[InternalMaintainabilityRuleHandlers.Length];
        var mappedRuleCount = 0;
        foreach (var policyRule in rules.Where(static rule => rule?.Rule is not null)) {
            var (firstMatchIndex, matchCount) = ResolveInternalMaintainabilityHandler(policyRule.Rule);

            if (firstMatchIndex < 0) {
                warnings.Add(
                    $"Internal maintainability rule {policyRule.Rule.Id} is configured but has no registered handler and will be skipped.");
                continue;
            }

            if (matchCount > 1) {
                warnings.Add(
                    $"Internal maintainability rule {policyRule.Rule.Id} matched multiple handlers; using first registered handler. Ensure handler predicates are mutually exclusive.");
            }

            mappedRulesByHandler[firstMatchIndex] ??= new List<AnalysisPolicyRule>();
            mappedRulesByHandler[firstMatchIndex]!.Add(policyRule);
            mappedRuleCount++;
        }

        if (mappedRuleCount == 0) {
            return new InternalMaintainabilityResult(findings, duplicationRuleMetrics);
        }

        // Build a candidate source set once; each rule applies its own filtering tags on top.
        var includedSourceExtensions = ResolveIncludedSourceExtensionsForRules(
            mappedRulesByHandler
                .Where(static group => group is { Count: > 0 })
                .SelectMany(static group => group!)
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

        for (var i = 0; i < InternalMaintainabilityRuleHandlers.Length; i++) {
            var rulesForHandler = mappedRulesByHandler[i];
            if (rulesForHandler is null || rulesForHandler.Count == 0) {
                continue;
            }

            var result = InternalMaintainabilityRuleHandlers[i].Run(rulesForHandler, sourceFiles, excludedOutputPath,
                warnings);
            findings.AddRange(result.Findings);
            duplicationRuleMetrics.AddRange(result.DuplicationRuleMetrics);
        }

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

}
